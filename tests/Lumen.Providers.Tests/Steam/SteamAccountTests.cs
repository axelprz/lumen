// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

// Synthetic responses shaped like IAuthenticationService / IFamilyGroupsService output (tests/fixtures/steam).
// The tokens are unsigned JWTs with a made-up SteamID; nothing here is a real account.
public sealed class SteamAccountTests
{
    private const string SteamId = "76561190000000001";

    private readonly StubHttpHandler _http = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeLocalSettings _settings = new();

    [Fact]
    public void BeginQr_IsParsed_AndOnlyHttpsChallengesAreAccepted()
    {
        var session = SteamAccountApi.ParseBeginQr(Fixtures.Read("steam/auth-begin-qr.json")).Value.ShouldNotBeNull();

        session.ClientId.ShouldBe("1234567890123456789");
        session.ChallengeUrl.ShouldBe("https://s.team/q/1/1234567890123456789");
        session.Interval.ShouldBe(TimeSpan.FromSeconds(5));
        SteamAccountApi.ParseBeginQr("""{"response":{"client_id":"1","request_id":"x","challenge_url":"http://evil.example"}}""").Error
            .ShouldBe(SteamAccountError.Unexpected);
    }

    [Fact]
    public void Poll_Waiting_NewQr_Approved()
    {
        SteamAccountApi.ParsePoll(Fixtures.Read("steam/auth-poll-waiting.json")).IsApproved.ShouldBeFalse();
        SteamAccountApi.ParsePoll(Fixtures.Read("steam/auth-poll-new-url.json")).NewChallengeUrl.ShouldBe("https://s.team/q/1/1234567890123456790");

        var approved = SteamAccountApi.ParsePoll(Fixtures.Read("steam/auth-poll-approved.json"));
        approved.IsApproved.ShouldBeTrue();
        approved.AccountName.ShouldBe("sample_player");
    }

    [Fact]
    public void TokenClaims_GiveTheSteamIdAndExpiry_AndGarbageIsNull()
    {
        var approved = SteamAccountApi.ParsePoll(Fixtures.Read("steam/auth-poll-approved.json"));

        var claims = SteamTokens.Read(approved.RefreshToken!).ShouldNotBeNull();
        claims.SteamId.ShouldBe(SteamId);
        claims.Expires.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1893456000));
        SteamTokens.Read("not.a.jwt").ShouldBeNull();
        SteamTokens.Read("nope").ShouldBeNull();
    }

    [Fact]
    public void Family_GroupAndSharedGames_AreParsed_OnlyShareableGamesKept()
    {
        SteamAccountApi.ParseFamilyGroup(Fixtures.Read("steam/family-group.json")).ShouldBe("9000000000000001");
        SteamAccountApi.ParseFamilyGroup(Fixtures.Read("steam/family-not-member.json")).ShouldBeNull();

        var games = SteamAccountApi.ParseSharedGames(Fixtures.Read("steam/family-shared-apps.json"));

        games.Select(g => g.AppId).ShouldBe(["1000020", "1000030"]);
        games[0].ShouldBe(new SteamSharedGame("1000020", "Shared Sample Quest", 1, 95, 1780000000));
        games[1].OwnerCount.ShouldBe(2);
    }

    [Fact]
    public async Task Api_MapsSteamErrors_AndOnlyTalksToApiSteampowered()
    {
        _http.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"response":{}}""") };
            response.Headers.Add("x-eresult", "9");
            return response;
        };

        var poll = await Api().PollAsync(new SteamQrSession("1", "x", "https://s.team/q/1/1", TimeSpan.FromSeconds(5)), CancellationToken.None);

        poll.Error.ShouldBe(SteamAccountError.SessionExpired);
        _http.Requests.ShouldAllBe(u => u.Host == "api.steampowered.com" && u.Scheme == "https");
        _http.RespondWith(HttpStatusCode.Unauthorized);
        (await Api().RefreshAccessTokenAsync("r", SteamId, CancellationToken.None)).Error.ShouldBe(SteamAccountError.Unauthorized);
    }

    [Fact]
    public async Task Renewal_SendsPlainFormFields_InTheBody()
    {
        string? body = null;
        _http.Respond = r =>
        {
            body = r.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixtures.Read("steam/auth-access-token.json")) };
        };

        (await Api().RefreshAccessTokenAsync("refresh-value", SteamId, CancellationToken.None)).Error.ShouldBe(SteamAccountError.None);

        body.ShouldBe($"refresh_token=refresh-value&steamid={SteamId}&renewal_type=0");
        _http.Requests.ShouldHaveSingleItem().Query.ShouldBeEmpty(); // the token never goes in the URL
    }

    [Fact]
    public async Task ApprovedSignIn_StoresOnlyTheRefreshToken_AndTheAccountForSettings()
    {
        Route("PollAuthSessionStatus", "steam/auth-poll-approved.json");
        var account = Account();
        var changed = 0;
        account.Changed += (_, _) => changed++;

        var poll = await account.PollAsync(new SteamQrSession("1", "x", "https://s.team/q/1/1", TimeSpan.FromSeconds(5)), CancellationToken.None);

        poll.IsApproved.ShouldBeTrue();
        poll.AccessToken.ShouldBeNull(); // tokens never leave SteamAccount
        _secrets.Values.Keys.ShouldBe([SecretNames.SteamRefreshToken]);
        _settings.Values[LocalSettingKeys.SteamAccountId].ShouldBe(SteamId);
        _settings.Values[LocalSettingKeys.SteamAccountName].ShouldBe("sample_player");
        (await account.GetInfoAsync(CancellationToken.None))!.AccountName.ShouldBe("sample_player");
        changed.ShouldBe(1);
    }

    [Fact]
    public async Task SharedGames_NotConnected_Ok_NotInFamily_NeedsReconnect()
    {
        (await Account().GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.NotConnected);

        SignedIn();
        Route("GenerateAccessTokenForApp", "steam/auth-access-token.json");
        Route("GetFamilyGroupForUser", "steam/family-group.json");
        Route("GetSharedLibraryApps", "steam/family-shared-apps.json");
        var ok = await Account().GetSharedGamesAsync(CancellationToken.None);
        ok.Status.ShouldBe(SteamFamilyStatus.Ok);
        ok.Games.Count.ShouldBe(2);

        Route("GetFamilyGroupForUser", "steam/family-not-member.json");
        (await Account().GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.NotInFamily);

        _http.RespondWith(HttpStatusCode.Unauthorized);
        var account = Account();
        var changed = 0;
        account.Changed += (_, _) => changed++;
        (await account.GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.NeedsReconnect);
        (await account.GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.NeedsReconnect);
        account.NeedsReconnect.ShouldBeTrue();
        changed.ShouldBe(1); // announced once, not on every scan
    }

    [Fact]
    public void SignIn_IsTheMobileAppType_SoAccessCanBeRenewed()
    {
        SteamAccountApi.PlatformMobileApp.ShouldBe(3);
    }

    [Fact]
    public async Task Disconnect_RevokesAtSteam_ThenDeletesEverything()
    {
        SignedIn();
        Route("GenerateAccessTokenForApp", "steam/auth-access-token.json");
        Route("RevokeToken", "steam/auth-poll-waiting.json"); // {"response":{}} = done

        (await Account().DisconnectAsync(CancellationToken.None)).ShouldBeTrue();

        _secrets.Values.ShouldBeEmpty();
        _settings.Values.ShouldBeEmpty();
        _http.Requests.ShouldContain(u => u.AbsolutePath.Contains("RevokeToken"));
    }

    [Fact]
    public async Task Disconnect_StillDeletesLocally_WhenSteamCanNotBeReached()
    {
        SignedIn();
        _http.Respond = _ => throw new HttpRequestException("offline");

        (await Account().DisconnectAsync(CancellationToken.None)).ShouldBeFalse();

        _secrets.Values.ShouldBeEmpty();
        _settings.Values.ShouldBeEmpty();
    }

    private SteamAccountApi Api() => new(new HttpClient(_http));

    private SteamAccount Account() => new(Api(), _secrets, _settings);

    private void SignedIn()
    {
        _secrets.Values[SecretNames.SteamRefreshToken] = SteamAccountApi.ParsePoll(Fixtures.Read("steam/auth-poll-approved.json")).RefreshToken!;
        _settings.Values[LocalSettingKeys.SteamAccountId] = SteamId;
        _settings.Values[LocalSettingKeys.SteamAccountName] = "sample_player";
        _settings.Values[LocalSettingKeys.SteamAccountSince] = "2026-10-28T00:00:00.0000000+00:00";
    }

    private void Route(string method, string fixture)
    {
        var previous = _http.Respond;
        _http.Respond = r => r.RequestUri!.AbsolutePath.Contains(method, StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixtures.Read(fixture)) }
            : previous(r);
    }
}

public sealed class SteamFamilyInProviderTests
{
    private static readonly SteamSharedGame Shared = new("1000020", "Shared Sample Quest", 1, 95, 1780000000);
    private static readonly SteamSharedGame AlsoOwned = new("10", "Owned Game", 1, 0, 0);

    [Fact]
    public void NeitherKeyNorSignIn_IsNotConfigured()
    {
        Combine(ProviderResult.NotConfigured("no key"), new(SteamFamilyStatus.NotConnected, [])).Status.ShouldBe(ProviderStatus.NotConfigured);
    }

    [Fact]
    public void FamilyGames_AreAdded_MarkedFamily_AndOwnedCopiesWin()
    {
        var owned = ProviderResult.FromScan([Owned("10", "Owned Game")], []);

        var result = Combine(owned, new(SteamFamilyStatus.Ok, [Shared, AlsoOwned]));

        result.Status.ShouldBe(ProviderStatus.Ok);
        result.Entries.Select(e => e.Ref.ExternalId).ShouldBe(["10", "1000020"]);
        var family = result.Entries.Single(e => e.Ref.ExternalId == "1000020");
        family.Extra["family"].ShouldBe("true");
        family.Extra["playtimeMinutes"].ShouldBe("95");
        family.State.ShouldBe(InstallState.NotInstalled);
        result.Entries.Single(e => e.Ref.ExternalId == "10").Extra.ContainsKey("family").ShouldBeFalse();
    }

    [Fact]
    public void SignInOnly_Works_WithoutAKey()
    {
        var result = Combine(ProviderResult.NotConfigured("no key"), new(SteamFamilyStatus.Ok, [Shared]));

        result.Status.ShouldBe(ProviderStatus.Ok);
        result.Entries.ShouldHaveSingleItem().Title.ShouldBe("Shared Sample Quest");
    }

    [Fact]
    public void OnePartFailing_IsPartial_WithAWarning_BothFailing_IsFailed()
    {
        var owned = ProviderResult.FromScan([Owned("10", "Owned Game")], []);

        var reconnect = Combine(owned, new(SteamFamilyStatus.NeedsReconnect, []));
        reconnect.Status.ShouldBe(ProviderStatus.Partial);
        reconnect.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("connect again", StringComparison.Ordinal));

        Combine(ProviderResult.Failed("key rejected"), new(SteamFamilyStatus.Ok, [Shared])).Status.ShouldBe(ProviderStatus.Partial);
        Combine(ProviderResult.Failed("key rejected"), new(SteamFamilyStatus.Failed, [])).Status.ShouldBe(ProviderStatus.Failed);
    }

    private static ProviderResult Combine(ProviderResult owned, SteamFamilyResult family) =>
        new SteamProvider(new FakeRegistry(), new InMemoryFileSystem(), new FakeSecretStore(), new FakeLocalSettings(), new SteamWebApi(new HttpClient(new StubHttpHandler())))
            .CombineOwnedAndFamily(owned, family);

    private static LibraryEntry Owned(string id, string title) =>
        new(new ItemRef("steam", id), ItemKind.Game, title, InstallState.NotInstalled, null, null, null, new Dictionary<string, string> { ["appid"] = id });
}
