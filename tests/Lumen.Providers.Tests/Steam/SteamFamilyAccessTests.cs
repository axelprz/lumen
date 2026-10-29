// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using System.Text;
using System.Text.Json;
using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

public sealed class SteamFamilyAccessTests
{
    // Synthetic: valid shape, not a real account.
    private const string SteamId = "76561190000000001";

    private readonly StubHttpHandler _http = new();
    private readonly FakeSession _session = new();

    [Fact]
    public void StorePage_SignedIn_GivesTheToken()
    {
        var token = SteamStorePage.ParseConfig(ScriptResult(
            $$"""{"logged_in":true,"steamid":"{{SteamId}}","country_code":"AR"}""",
            """{"webapi_token":"synthetic.web.token"}"""));

        token.ShouldBe(new SteamWebToken(SteamId, "synthetic.web.token"));
    }

    [Theory]
    [InlineData("""{"logged_in":false}""", """{"webapi_token":"synthetic.web.token"}""")]
    [InlineData("""{"logged_in":true,"steamid":"123"}""", """{"webapi_token":"synthetic.web.token"}""")]
    [InlineData("""{"logged_in":true,"steamid":"76561190000000001"}""", """{}""")]
    public void StorePage_NotSignedInOrIncomplete_GivesNothing(string user, string store) =>
        SteamStorePage.ParseConfig(ScriptResult(user, store)).ShouldBeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("\"<html>\"")]
    public void StorePage_Garbage_GivesNothing(string? result) => SteamStorePage.ParseConfig(result).ShouldBeNull();

    [Fact]
    public async Task NotSignedIn_AsksNothing()
    {
        (await Access().GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.NotConnected);

        _session.Reads.ShouldBe(0);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SignedIn_ReadsFamilyGames_ReusingTheTokenUntilItExpires()
    {
        _session.SignedIn = true;
        _session.Token = new SteamWebToken(SteamId, Jwt(DateTimeOffset.UtcNow.AddHours(20)));
        _http.Respond = request => Json(request.RequestUri!.AbsolutePath.Contains("GetFamilyGroupForUser", StringComparison.Ordinal)
            ? Fixtures.Read("steam/family-group.json")
            : Fixtures.Read("steam/family-shared-apps.json"));
        var access = Access();

        var first = await access.GetSharedGamesAsync(CancellationToken.None);
        await access.GetSharedGamesAsync(CancellationToken.None);

        first.Status.ShouldBe(SteamFamilyStatus.Ok);
        first.Games.Select(g => g.AppId).ShouldBe(["1000020", "1000030"]);
        _session.Reads.ShouldBe(1); // the background store page is opened once, not on every scan
        _http.Requests.ShouldAllBe(u => u.Host == "api.steampowered.com");
    }

    [Fact]
    public async Task SessionEndedAtSteam_AsksToSignInAgain()
    {
        _session.SignedIn = true;
        _session.Token = null;
        var access = Access();

        var result = await access.GetSharedGamesAsync(CancellationToken.None);

        result.Status.ShouldBe(SteamFamilyStatus.NeedsReconnect);
        access.NeedsReconnect.ShouldBeTrue();
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RejectedToken_IsDropped_SoTheNextScanReadsANewOne()
    {
        _session.SignedIn = true;
        _session.Token = new SteamWebToken(SteamId, Jwt(DateTimeOffset.UtcNow.AddHours(20)));
        _http.RespondWith(HttpStatusCode.Unauthorized);
        var access = Access();

        (await access.GetSharedGamesAsync(CancellationToken.None)).Status.ShouldBe(SteamFamilyStatus.Failed);
        await access.GetSharedGamesAsync(CancellationToken.None);

        _session.Reads.ShouldBe(2);
    }

    private SteamFamilyAccess Access() => new(_session, new SteamAccountApi(new HttpClient(_http)));

    // What WebView2's ExecuteScriptAsync returns for our script: a JSON string holding {user, store}.
    private static string ScriptResult(string user, string store) =>
        JsonSerializer.Serialize(JsonSerializer.Serialize(new { user, store }));

    private static string Jwt(DateTimeOffset expires)
    {
        static string Part(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Part("""{"alg":"none"}""")}.{Part($$"""{"sub":"{{SteamId}}","exp":{{expires.ToUnixTimeSeconds()}}}""")}.sig";
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeSession : ISteamWebSession
    {
        public bool SignedIn { get; set; }

        public SteamWebToken? Token { get; set; }

        public int Reads { get; private set; }

        public Task<bool> IsSignedInAsync(CancellationToken ct) => Task.FromResult(SignedIn);

        public Task<SteamWebToken?> GetTokenAsync(CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(Token);
        }
    }
}
