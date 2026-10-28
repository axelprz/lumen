// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Providers.Gog;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Gog;

// Synthetic responses shaped like GOG's auth.gog.com / embed.gog.com answers (tests/fixtures/gog), following what
// Heroic's gogdl reads. Account and tokens are made up.
public sealed class GogAccountTests
{
    private const string Code = "SyntheticCode_0123456789-abcdefABCDEF";

    private readonly StubHttpHandler _http = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly FakeLocalSettings _settings = new();

    [Theory]
    [InlineData("https://embed.gog.com/on_login_success?origin=client&code=" + Code, Code)]
    [InlineData("  " + Code + " ", Code)]
    [InlineData("https://embed.gog.com/on_login_success?origin=client", null)]
    [InlineData("nope", null)]
    [InlineData("", null)]
    public void PastedAddressOrCode_IsRecognized(string pasted, string? expected) => GogAccountApi.ExtractCode(pasted).ShouldBe(expected);

    [Fact]
    public void SignInPage_IsGogsOwn_ForTheGalaxyClient()
    {
        GogAccountApi.SignInPage.Host.ShouldBe("auth.gog.com");
        GogAccountApi.SignInPage.Query.ShouldContain("client_id=" + GogAccountApi.GalaxyClientId);
    }

    [Fact]
    public void Session_And_Error_AreParsed()
    {
        GogAccountApi.ParseSession(Fixtures.Read("gog/token.json")).Value!.UserId.ShouldBe("48600000000000001");
        GogAccountApi.ParseSession(Fixtures.Read("gog/token-invalid.json")).Error.ShouldBe(GogAccountError.Unauthorized);
    }

    [Fact]
    public async Task OwnedGames_AllPages_GamesOnly()
    {
        Route("/account/getFilteredProducts", r => r.RequestUri!.Query.Contains("page=2") ? "gog/products-page2.json" : "gog/products-page1.json");

        var owned = await Api().GetOwnedGamesAsync("synthetic-gog-access", CancellationToken.None);

        owned.Value!.ShouldBe([new GogOwnedGame("1000000001", "Sample Adventure"), new GogOwnedGame("1000000003", "Sample Strategy")]);
        _http.Requests.ShouldAllBe(u => u.Host == "embed.gog.com" && !u.Query.Contains("synthetic"));
    }

    [Fact]
    public async Task Connect_StoresOnlyTheRefreshToken_AndRefreshKeepsTheNewest()
    {
        Route("/token", r => r.RequestUri!.Query.Contains("grant_type=refresh_token") ? "gog/token-refreshed.json" : "gog/token.json");
        Route("/userData.json", _ => "gog/user.json");
        Route("/account/getFilteredProducts", _ => "gog/products-page2.json");
        var account = new GogAccount(Api(), _secrets, _settings, new LateClock());

        (await account.ConnectAsync("https://embed.gog.com/on_login_success?origin=client&code=" + Code, CancellationToken.None)).Result
            .ShouldBe(GogConnectResult.Connected);
        _secrets.Values.ShouldBe(new Dictionary<string, string> { [SecretNames.GogRefreshToken] = "synthetic-gog-refresh-1" });
        (await account.GetInfoAsync(CancellationToken.None))!.UserName.ShouldBe("sample_player");

        (await account.GetOwnedAsync(CancellationToken.None)).Status.ShouldBe(GogOwnedStatus.Ok); // clock: token expired → refresh
        _secrets.Values[SecretNames.GogRefreshToken].ShouldBe("synthetic-gog-refresh-2");
    }

    [Fact]
    public async Task RejectedCode_AndRejectedRefresh_AreReported()
    {
        Route("/token", _ => "gog/token-invalid.json", HttpStatusCode.BadRequest);
        var account = new GogAccount(Api(), _secrets, _settings);

        (await account.ConnectAsync(Code, CancellationToken.None)).Result.ShouldBe(GogConnectResult.CodeRejected);
        (await account.ConnectAsync("garbage", CancellationToken.None)).Result.ShouldBe(GogConnectResult.NotACode);

        _secrets.Values[SecretNames.GogRefreshToken] = "old";
        (await account.GetOwnedAsync(CancellationToken.None)).Status.ShouldBe(GogOwnedStatus.NeedsReconnect);
    }

    [Fact]
    public async Task Disconnect_DeletesEverythingLocally()
    {
        _secrets.Values[SecretNames.GogRefreshToken] = "synthetic-gog-refresh-1";
        _settings.Values[LocalSettingKeys.GogAccountId] = "48600000000000001";
        _settings.Values[LocalSettingKeys.GogAccountName] = "sample_player";

        await new GogAccount(Api(), _secrets, _settings).DisconnectAsync(CancellationToken.None);

        _secrets.Values.ShouldBeEmpty();
        _settings.Values.ShouldBeEmpty();
        _http.Requests.ShouldBeEmpty(); // GOG has nothing to revoke against
    }

    [Fact]
    public async Task OwnedScan_GivesNotInstalledGames_OpenedInGalaxy()
    {
        _secrets.Values[SecretNames.GogRefreshToken] = "synthetic-gog-refresh-1";
        Route("/token", _ => "gog/token.json");
        Route("/account/getFilteredProducts", r => r.RequestUri!.Query.Contains("page=2") ? "gog/products-page2.json" : "gog/products-page1.json");
        var provider = new GogProvider(new FakeRegistry(), new InMemoryFileSystem(), new GogAccount(Api(), _secrets, _settings));

        var result = await provider.ScanOwnedAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Ok);
        var first = result.Entries.First();
        first.Ref.ShouldBe(new ItemRef("gog", "1000000001"));
        first.State.ShouldBe(InstallState.NotInstalled);
        first.Install.ShouldBe(new LaunchSpec(LaunchKind.Uri, "goggalaxy://openGameView/1000000001"));
        (await new GogProvider(new FakeRegistry(), new InMemoryFileSystem()).ScanOwnedAsync(CancellationToken.None)).Status
            .ShouldBe(ProviderStatus.NotConfigured);
    }

    private GogAccountApi Api() => new(new HttpClient(_http));

    private void Route(string path, Func<HttpRequestMessage, string> fixture, HttpStatusCode status = HttpStatusCode.OK)
    {
        var previous = _http.Respond;
        _http.Respond = r => r.RequestUri!.AbsolutePath.EndsWith(path, StringComparison.Ordinal) || r.RequestUri.AbsolutePath.Contains(path + "/", StringComparison.Ordinal)
            ? new HttpResponseMessage(status) { Content = new StringContent(Fixtures.Read(fixture(r))) }
            : previous(r);
    }

    // Makes the in-memory access token look expired, so the next call refreshes.
    private sealed class LateClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(5);
    }
}
