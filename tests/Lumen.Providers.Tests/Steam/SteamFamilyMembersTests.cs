// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Settings;
using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

public sealed class SteamFamilyMembersTests
{
    // Synthetic: valid shapes, not real accounts.
    private const string Me = "76561190000000001";
    private const string Sibling = "76561190000000002";
    private const string Parent = "76561190000000003";
    private const string FakeKey = "TESTKEY0000000000000000000000000";

    private readonly StubHttpHandler _http = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeLocalSettings _settings = new();

    public SteamFamilyMembersTests()
    {
        _secrets.Values[SecretNames.SteamWebApiKey] = FakeKey;
        _settings.Values[LocalSettingKeys.SteamId64] = Me;
    }

    [Fact]
    public void ParsesFriendsProfilesAndVanity()
    {
        SteamWebApi.ParseFriendList(Fixtures.Read("steam/friend_list.json")).Profiles.Select(p => p.SteamId).ShouldBe([Sibling, Parent]);
        SteamWebApi.ParseProfiles(Fixtures.Read("steam/player_summaries.json")).Profiles
            .ShouldBe([new SteamProfile(Sibling, "Sample Sibling"), new SteamProfile(Parent, Parent)]);
        SteamWebApi.ParseVanity(Fixtures.Read("steam/resolve_vanity.json")).ShouldBe(Sibling);
        SteamWebApi.ParseVanity("""{"response":{"success":42,"message":"No match"}}""").ShouldBeNull();
    }

    [Theory]
    [InlineData("https://steamcommunity.com/id/sample_name/", "sample_name")]
    [InlineData("steamcommunity.com/id/Sample-Name", "Sample-Name")]
    [InlineData("https://steamcommunity.com/profiles/76561190000000002", null)]
    [InlineData("https://evil.example/id/x", null)]
    public void ExtractsVanityNames(string input, string? expected) => SteamWebApi.ExtractVanityName(input).ShouldBe(expected);

    [Fact]
    public async Task SavedMembers_AreCleaned()
    {
        var family = Family();

        await family.SetIdsAsync([Sibling, Sibling, Me, "garbage", Parent, "76561190000000004", "76561190000000005", "76561190000000006", "76561190000000007"], CancellationToken.None);

        (await family.GetIdsAsync(CancellationToken.None)).ShouldBe([Sibling, Parent, "76561190000000004", "76561190000000005", "76561190000000006"]);
    }

    [Fact]
    public async Task SharedGames_CountOwners_LeaveOutFreeGamesAndTheirPlaytime()
    {
        _settings.Values[LocalSettingKeys.SteamFamilyMembers] = $"{Sibling},{Parent}";
        _http.Respond = request => Json(request.RequestUri!.Query.Contains(Sibling, StringComparison.Ordinal)
            ? """{"response":{"games":[{"appid":10,"name":"Shared One","playtime_forever":900},{"appid":20,"name":"Only Sibling","playtime_forever":5}]}}"""
            : """{"response":{"games":[{"appid":10,"name":"Shared One","playtime_forever":30}]}}""");

        var result = await Family().GetSharedGamesAsync(CancellationToken.None);

        result.Status.ShouldBe(SteamFamilyStatus.Ok);
        result.Games.OrderBy(g => g.AppId).ShouldBe([new SteamSharedGame("10", "Shared One", 2, 0, 0), new SteamSharedGame("20", "Only Sibling", 1, 0, 0)]);
        _http.Requests.ShouldAllBe(u => u.Query.Contains("include_played_free_games=0", StringComparison.Ordinal));
        _http.Requests.ShouldAllBe(u => u.Host == "api.steampowered.com" && u.AbsolutePath == "/IPlayerService/GetOwnedGames/v1/");
    }

    [Fact]
    public async Task PrivateMembers_AreCounted_NotFatal()
    {
        _settings.Values[LocalSettingKeys.SteamFamilyMembers] = $"{Sibling},{Parent}";
        _http.Respond = request => Json(request.RequestUri!.Query.Contains(Sibling, StringComparison.Ordinal)
            ? """{"response":{"games":[{"appid":10,"name":"Shared One"}]}}"""
            : """{"response":{}}""");

        var result = await Family().GetSharedGamesAsync(CancellationToken.None);

        result.Status.ShouldBe(SteamFamilyStatus.Ok);
        result.PrivateMembers.ShouldBe(1);
        result.Games.Count.ShouldBe(1);
    }

    [Fact]
    public async Task NoMembersOrNoKey_AsksNothing()
    {
        (await Family().GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.NotConnected);

        _settings.Values[LocalSettingKeys.SteamFamilyMembers] = Sibling;
        _secrets.Values.Clear();
        (await Family().GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.NotConnected);

        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryMemberFailing_IsFailed()
    {
        _settings.Values[LocalSettingKeys.SteamFamilyMembers] = Sibling;
        _http.RespondWith(HttpStatusCode.ServiceUnavailable);

        (await Family().GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.Failed);
    }

    private SteamFamilyMembers Family() =>
        new(new SteamWebApi(new HttpClient(_http) { BaseAddress = new Uri(SteamWebApi.BaseAddress) }), _secrets, _settings);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
}
