// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Providers.Epic;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Epic;

// Synthetic responses shaped like Epic's account, library and catalog services (tests/fixtures/epic), following
// what Legendary reads. Account and tokens are made up.
public sealed class EpicAccountTests
{
    private const string Code = "0123456789abcdef0123456789abcdef";

    private readonly StubHttpHandler _http = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeLocalSettings _settings = new();
    private readonly List<HttpRequestMessage> _sent = [];

    [Theory]
    [InlineData(Code, Code)]
    [InlineData("  " + Code + "  ", Code)]
    [InlineData("{\"redirectUrl\":\"https://localhost/launcher/authorized\",\"authorizationCode\":\"" + Code + "\",\"sid\":null}", Code)]
    [InlineData("not a code", null)]
    [InlineData("{\"authorizationCode\":null}", null)]
    [InlineData("", null)]
    public void PastedCode_IsRecognized(string pasted, string? expected) => EpicAccountApi.ExtractCode(pasted).ShouldBe(expected);

    [Fact]
    public void SignInPage_IsEpicsOwn_ForTheLauncherClient()
    {
        EpicAccountApi.SignInPage.Host.ShouldBe("www.epicgames.com");
        Uri.UnescapeDataString(EpicAccountApi.SignInPage.Query).ShouldContain("clientId=" + EpicAccountApi.LauncherClientId);
    }

    [Fact]
    public void Session_Errors_AndCorrectiveAction_AreParsed()
    {
        var session = EpicAccountApi.ParseSession(Fixtures.Read("epic/token.json")).Value.ShouldNotBeNull();
        session.DisplayName.ShouldBe("SamplePlayer");
        session.AccessExpires.ShouldBe(new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero));

        EpicAccountApi.ParseSession(Fixtures.Read("epic/token-bad-code.json")).Error.ShouldBe(EpicAccountError.Unauthorized);
        EpicAccountApi.ParseSession(Fixtures.Read("epic/token-corrective.json")).Error.ShouldBe(EpicAccountError.ActionRequired);
    }

    [Fact]
    public void Catalog_SaysGameAppOrSkip()
    {
        EpicAccountApi.ParseCatalogInfo(Fixtures.Read("epic/catalog-game.json"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1").ShouldBe(new EpicCatalogInfo("Sample Quest", false, false));
        EpicAccountApi.ParseCatalogInfo(Fixtures.Read("epic/catalog-dlc.json"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2")!.Skip.ShouldBeTrue();
        EpicAccountApi.ParseCatalogInfo(Fixtures.Read("epic/catalog-software.json"), "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa3")!.IsApp.ShouldBeTrue();
        EpicAccountApi.ParseCatalogInfo("{}", "missing").ShouldBeNull();
    }

    [Fact]
    public async Task Library_FollowsTheCursor_AndSkipsMarketplaceAndBundles()
    {
        Route("/library/api/public/items", r => r.RequestUri!.Query.Contains("cursor=cursor-2") ? "epic/library-page2.json" : "epic/library-page1.json");

        var library = await Api().GetLibraryAsync("eg1~synthetic-access", CancellationToken.None);

        library.Value!.Select(r => r.AppName).ShouldBe(["SampleQuest", "SampleDlc", "SampleTool"]);
        _sent.ShouldAllBe(r => r.Headers.Authorization!.Scheme == "bearer" && !r.RequestUri!.Query.Contains("eg1~"));
    }

    [Fact]
    public async Task CodeExchange_UsesTheLauncherClient_OverBasicAuth_ToEpicOnly()
    {
        Route("/oauth/token", _ => "epic/token.json");

        (await Api().ExchangeCodeAsync(Code, CancellationToken.None)).Error.ShouldBe(EpicAccountError.None);

        var request = _sent.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.Host.ShouldBe("account-public-service-prod03.ol.epicgames.com");
        request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        request.RequestUri.Query.ShouldNotContain(Code); // the code travels in the body
    }

    [Fact]
    public async Task Connect_StoresOnlyTheRefreshToken_AndRefreshKeepsTheNewestOne()
    {
        Route("/oauth/token", r => r.Content!.ReadAsStringAsync().Result.Contains("grant_type=refresh_token") ? "epic/token-refreshed.json" : "epic/token.json");
        var account = new EpicAccount(Api(), _secrets, _settings, new ExpiredClock());

        (await account.ConnectAsync(Code, CancellationToken.None)).Result.ShouldBe(EpicConnectResult.Connected);
        _secrets.Values.ShouldBe(new Dictionary<string, string> { [SecretNames.EpicRefreshToken] = "eg1~synthetic-refresh-1" });
        (await account.GetInfoAsync(CancellationToken.None))!.DisplayName.ShouldBe("SamplePlayer");

        Route("/library/api/public/items", _ => "epic/library-page2.json");
        await account.GetOwnedAsync(CancellationToken.None); // the clock says the access token expired: refresh

        _secrets.Values[SecretNames.EpicRefreshToken].ShouldBe("eg1~synthetic-refresh-2");
    }

    [Fact]
    public async Task Connect_ExplainsWhatWentWrong()
    {
        Route("/oauth/token", _ => "epic/token-bad-code.json", HttpStatusCode.BadRequest);
        var account = new EpicAccount(Api(), _secrets, _settings);

        (await account.ConnectAsync("nonsense", CancellationToken.None)).Result.ShouldBe(EpicConnectResult.NotACode);
        (await account.ConnectAsync(Code, CancellationToken.None)).Result.ShouldBe(EpicConnectResult.CodeRejected);
        _secrets.Values.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disconnect_EndsTheSession_ThenDeletes_EvenOffline()
    {
        _secrets.Values[SecretNames.EpicRefreshToken] = "eg1~synthetic-refresh-1";
        _settings.Values[LocalSettingKeys.EpicAccountId] = "00000000000000000000000000000001";
        Route("/oauth/token", _ => "epic/token.json");
        Route("/sessions/kill/", _ => null, HttpStatusCode.NoContent);

        (await new EpicAccount(Api(), _secrets, _settings).DisconnectAsync(CancellationToken.None)).ShouldBeTrue();
        _secrets.Values.ShouldBeEmpty();
        _settings.Values.ShouldBeEmpty();

        _secrets.Values[SecretNames.EpicRefreshToken] = "eg1~synthetic-refresh-1";
        _http.Respond = _ => throw new HttpRequestException("offline");
        (await new EpicAccount(Api(), _secrets, _settings).DisconnectAsync(CancellationToken.None)).ShouldBeFalse();
        _secrets.Values.ShouldBeEmpty();
    }

    [Fact]
    public async Task OwnedScan_TitlesFromTheCatalog_CachesThem_AndSkipsDlc()
    {
        _secrets.Values[SecretNames.EpicRefreshToken] = "eg1~synthetic-refresh-1";
        _settings.Values[LocalSettingKeys.EpicAccountId] = "00000000000000000000000000000001";
        Route("/oauth/token", _ => "epic/token.json");
        Route("/library/api/public/items", r => r.RequestUri!.Query.Contains("cursor=cursor-2") ? "epic/library-page2.json" : "epic/library-page1.json");
        Route("/catalog/api/shared/namespace/", r => r.RequestUri!.Query.Contains("aaa1") ? "epic/catalog-game.json"
            : r.RequestUri.Query.Contains("aaa2") ? "epic/catalog-dlc.json" : "epic/catalog-software.json");
        var cache = new MemoryMetadata();
        var provider = new EpicProvider(new FakeRegistry(), new InMemoryFileSystem(), new FakeKnownFolders(), new EpicAccount(Api(), _secrets, _settings), cache);

        var result = await provider.ScanOwnedAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Ok);
        result.Entries.Select(e => (e.Ref.ToString(), e.Title, e.Kind, e.State))
            .ShouldBe([("epic:SampleQuest", "Sample Quest", ItemKind.Game, InstallState.NotInstalled), ("epic:SampleTool", "Sample Tool", ItemKind.App, InstallState.NotInstalled)]);
        cache.Values.Count.ShouldBe(3);

        var catalogCalls = _sent.Count(r => r.RequestUri!.AbsolutePath.Contains("/catalog/"));
        provider.InvalidateCache();
        await provider.ScanOwnedAsync(CancellationToken.None);
        _sent.Count(r => r.RequestUri!.AbsolutePath.Contains("/catalog/")).ShouldBe(catalogCalls); // titles came from the cache
    }

    [Fact]
    public async Task OwnedScan_WithoutSignIn_IsNotConfigured()
    {
        var provider = new EpicProvider(new FakeRegistry(), new InMemoryFileSystem(), new FakeKnownFolders(), new EpicAccount(Api(), _secrets, _settings));

        (await provider.ScanOwnedAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotConfigured);
    }

    private EpicAccountApi Api() => new(new HttpClient(_http));

    private void Route(string path, Func<HttpRequestMessage, string?> fixture, HttpStatusCode status = HttpStatusCode.OK)
    {
        var previous = _http.Respond;
        _http.Respond = r =>
        {
            if (!r.RequestUri!.AbsolutePath.Contains(path, StringComparison.Ordinal))
            {
                return previous(r);
            }

            _sent.Add(r);
            var name = fixture(r);
            return new HttpResponseMessage(status) { Content = new StringContent(name is null ? string.Empty : Fixtures.Read(name)) };
        };
    }

    // Makes every cached access token look expired, so the next call refreshes.
    private sealed class ExpiredClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2031, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class MemoryMetadata : IMetadataCache
    {
        public Dictionary<string, string> Values { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> GetManyAsync(IEnumerable<string> keys, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(keys.Where(Values.ContainsKey).ToDictionary(k => k, k => Values[k]));

        public Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct)
        {
            foreach (var (k, v) in values)
            {
                Values[k] = v;
            }

            return Task.CompletedTask;
        }
    }
}
