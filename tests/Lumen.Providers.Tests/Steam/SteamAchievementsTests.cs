// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Settings;
using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

public sealed class SteamAchievementsTests
{
    // Synthetic: valid shape, not a real account.
    private const string SteamId = "76561190000000001";
    private const string FakeKey = "TESTKEY0000000000000000000000000";

    private readonly StubHttpHandler _http = new();

    private SteamWebApi Api() => new(new HttpClient(_http) { BaseAddress = new Uri(SteamWebApi.BaseAddress) });

    [Fact]
    public void Parse_CountsUnlockedAndTotal()
    {
        var result = SteamWebApi.ParseAchievements(Fixtures.Read("steam/achievements.json"));

        result.Error.ShouldBe(SteamApiError.None);
        result.Unlocked.ShouldBe(2);
        result.Total.ShouldBe(4);
    }

    [Fact]
    public void Parse_GameWithoutStats_IsNoAchievements()
    {
        SteamWebApi.ParseAchievements(Fixtures.Read("steam/achievements_none.json")).Error.ShouldBe(SteamApiError.NoAchievements);
    }

    [Fact]
    public void Parse_PrivateProfile()
    {
        SteamWebApi.ParseAchievements(Fixtures.Read("steam/achievements_private.json")).Error.ShouldBe(SteamApiError.PrivateProfile);
    }

    [Fact]
    public void Parse_Garbage_IsUnexpected()
    {
        SteamWebApi.ParseAchievements("<html>maintenance</html>").Error.ShouldBe(SteamApiError.Unexpected);
    }

    [Fact]
    public async Task Request_HasExpectedEndpointAndParameters()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("steam/achievements.json"));

        var result = await Api().GetAchievementsAsync(FakeKey, SteamId, "1000010", CancellationToken.None);

        result.Unlocked.ShouldBe(2);
        var uri = _http.Requests.Single();
        uri.Host.ShouldBe("api.steampowered.com");
        uri.AbsolutePath.ShouldBe("/ISteamUserStats/GetPlayerAchievements/v1/");
        uri.Query.ShouldContain("steamid=" + SteamId);
        uri.Query.ShouldContain("appid=1000010");
    }

    [Fact]
    public async Task Steam400WithNoStats_IsNoAchievements_NotAnError()
    {
        _http.RespondWith(HttpStatusCode.BadRequest, Fixtures.Read("steam/achievements_none.json"));

        (await Api().GetAchievementsAsync(FakeKey, SteamId, "1000010", CancellationToken.None)).Error.ShouldBe(SteamApiError.NoAchievements);
    }

    [Fact]
    public async Task Steam403WithPrivateProfile_IsPrivate_ButAPlain403IsAnInvalidKey()
    {
        _http.RespondWith(HttpStatusCode.Forbidden, Fixtures.Read("steam/achievements_private.json"));
        (await Api().GetAchievementsAsync(FakeKey, SteamId, "1000010", CancellationToken.None)).Error.ShouldBe(SteamApiError.PrivateProfile);

        _http.RespondWith(HttpStatusCode.Forbidden, "<html>Forbidden</html>");
        var result = await Api().GetAchievementsAsync(FakeKey, SteamId, "1000010", CancellationToken.None);
        result.Error.ShouldBe(SteamApiError.InvalidKey);
        result.Detail!.ShouldNotContain(FakeKey);
    }

    [Fact]
    public async Task BadIds_AreRefusedBeforeAnyRequest()
    {
        await Should.ThrowAsync<ArgumentException>(() => Api().GetAchievementsAsync(FakeKey, "123", "1000010", CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => Api().GetAchievementsAsync(FakeKey, SteamId, "10&x=1", CancellationToken.None));
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Service_WithoutKeyOrSignIn_NeedsSetup()
    {
        var service = new SteamAchievementsService(Api(), new FakeSecretStore(), new FakeLocalSettings());

        (await service.GetAsync("1000010", CancellationToken.None)).Status.ShouldBe(AchievementsStatus.NeedsSetup);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Service_WithKey_ReturnsCounts_AndReusesTheAnswer()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("steam/achievements.json"));
        var secrets = new FakeSecretStore();
        await secrets.SetAsync(SecretNames.SteamWebApiKey, FakeKey, CancellationToken.None);
        var settings = new FakeLocalSettings();
        await settings.SetAsync(LocalSettingKeys.SteamId64, SteamId, CancellationToken.None);
        var service = new SteamAchievementsService(Api(), secrets, settings);

        var first = await service.GetAsync("1000010", CancellationToken.None);
        var second = await service.GetAsync("1000010", CancellationToken.None);

        first.ShouldBe(new AchievementsResult(AchievementsStatus.Available, 2, 4));
        first.Percent.ShouldBe(50);
        second.ShouldBe(first);
        _http.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Service_PrivateOrBroken_IsReported_AndNotRemembered()
    {
        var secrets = new FakeSecretStore();
        await secrets.SetAsync(SecretNames.SteamWebApiKey, FakeKey, CancellationToken.None);
        var settings = new FakeLocalSettings();
        await settings.SetAsync(LocalSettingKeys.SteamId64, SteamId, CancellationToken.None);
        var service = new SteamAchievementsService(Api(), secrets, settings);

        _http.RespondWith(HttpStatusCode.Forbidden, Fixtures.Read("steam/achievements_private.json"));
        (await service.GetAsync("1000010", CancellationToken.None)).Status.ShouldBe(AchievementsStatus.Private);

        _http.RespondWith(HttpStatusCode.InternalServerError);
        (await service.GetAsync("1000010", CancellationToken.None)).Status.ShouldBe(AchievementsStatus.Failed);

        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("steam/achievements.json"));
        (await service.GetAsync("1000010", CancellationToken.None)).Status.ShouldBe(AchievementsStatus.Available);
    }

    [Fact]
    public async Task Service_NotAnAppId_IsNotAvailable_WithoutAskingSteam()
    {
        var service = new SteamAchievementsService(Api(), new FakeSecretStore(), new FakeLocalSettings());

        (await service.GetAsync("../x", CancellationToken.None)).Status.ShouldBe(AchievementsStatus.NotAvailable);
        _http.Requests.ShouldBeEmpty();
    }
}
