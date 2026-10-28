// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Providers.SteamGridDb;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.SteamGridDb;

// Synthetic fixtures following the documented API v2 shapes; the 401 body was checked against the live API (2026-10).
public sealed class SteamGridDbTests
{
    private const string Key = "TESTKEY0000000000000000000000000";

    private readonly StubHttpHandler _http = new();
    private readonly FakeSecretStore _secrets = new();
    private readonly MemoryLookups _lookups = new();
    private readonly LibraryEntry _steamGame = Entry("steam", "1000010", "Sample Quest");
    private readonly LibraryEntry _epicGame = Entry("epic", "OldApp", "Old Game");

    [Fact]
    public async Task KeyGoesInTheAuthorizationHeader_NeverInTheUrl()
    {
        HttpRequestMessage? seen = null;
        _http.Respond = r =>
        {
            seen = r;
            return Json(HttpStatusCode.OK, "steamgriddb/game.json");
        };

        await Client().GetGameBySteamAppIdAsync(Key, "1000010", CancellationToken.None);

        seen!.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        seen.Headers.Authorization.Parameter.ShouldBe(Key);
        seen.RequestUri!.ToString().ShouldNotContain(Key);
        seen.RequestUri.ToString().ShouldBe("https://www.steamgriddb.com/api/v2/games/steam/1000010");
    }

    [Fact]
    public async Task Images_FilterExplicitArt_UntrustedHosts_AndPlainHttp()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("steamgriddb/grids.json"));

        var result = await Client().GetImagesAsync(Key, 900001, ArtworkKind.Cover, CancellationToken.None);

        result.Value!.Select(u => u.ToString()).ShouldBe(
        [
            "https://cdn2.steamgriddb.com/grid/0000000000000000000000000000aaaa.png",
            "https://cdn2.steamgriddb.com/grid/0000000000000000000000000000eeee.webp",
            "https://cdn2.steamgriddb.com/grid/0000000000000000000000000000ffff.png",
        ]);
        _http.Requests.Single().Query.ShouldContain("dimensions=600x900");
        _http.Requests.Single().Query.ShouldContain("nsfw=false");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SgdbError.InvalidKey)]
    [InlineData(HttpStatusCode.NotFound, SgdbError.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, SgdbError.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, SgdbError.Network)]
    public async Task Errors_AreMapped(HttpStatusCode status, SgdbError expected)
    {
        _http.RespondWith(status, Fixtures.Read("steamgriddb/unauthorized.json"));

        (await Client().SearchAsync(Key, "x", CancellationToken.None)).Error.ShouldBe(expected);
    }

    [Fact]
    public async Task NoKey_IsUnavailable_AndMakesNoRequest()
    {
        var result = await Source().GetCandidatesAsync(_epicGame, ArtworkKind.Cover, CancellationToken.None);

        result.ShouldBe(ArtworkCandidates.Unavailable);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task SteamEntry_IsLookedUpByAppId_ThenCached()
    {
        _secrets.Values[SecretNames.SteamGridDbApiKey] = Key;
        Route(r => r.AbsolutePath.EndsWith("/games/steam/1000010"), "steamgriddb/game.json");
        Route(r => r.AbsolutePath.EndsWith("/grids/game/900001"), "steamgriddb/grids.json");

        var first = await Source().GetCandidatesAsync(_steamGame, ArtworkKind.Cover, CancellationToken.None);
        await Source().GetCandidatesAsync(_steamGame, ArtworkKind.Hero, CancellationToken.None);

        first.Definitive.ShouldBeTrue();
        first.Urls.Count.ShouldBe(3);
        _http.Requests.Count(u => u.AbsolutePath.Contains("/games/steam/")).ShouldBe(1); // second call used the cache
    }

    [Fact]
    public async Task TitleSearch_OnlyAcceptsAMatchingTitle()
    {
        _secrets.Values[SecretNames.SteamGridDbApiKey] = Key;
        Route(r => r.AbsolutePath.Contains("/search/autocomplete/"), "steamgriddb/search.json");
        Route(r => r.AbsolutePath.EndsWith("/grids/game/900002"), "steamgriddb/grids.json");

        var (id, available) = await Source().FindGameIdAsync(Key, _epicGame, CancellationToken.None);

        available.ShouldBeTrue();
        id.ShouldBe(900002); // "Old Game™" matches "Old Game"; "Old Games Collection" does not
    }

    [Fact]
    public async Task TitleWithSubtitle_FallsBackToTheMainTitle()
    {
        _secrets.Values[SecretNames.SteamGridDbApiKey] = Key;
        Route(r => r.AbsolutePath.Contains("/search/autocomplete/"), "steamgriddb/search.json");
        var searches = new List<string>();
        var previous = _http.Respond; // record each search, then answer as routed above
        _http.Respond = r =>
        {
            if (r.RequestUri!.AbsolutePath.Contains("/search/autocomplete/"))
            {
                searches.Add(Uri.UnescapeDataString(r.RequestUri.AbsolutePath.Split('/')[^1]));
            }

            return previous(r);
        };

        var (id, _) = await Source().FindGameIdAsync(Key, Entry("epic", "x", "Old Game - A Unique Adventure"), CancellationToken.None);

        id.ShouldBe(900002);
        searches.ShouldBe(["Old Game - A Unique Adventure", "Old Game"]);
    }

    [Theory]
    [InlineData("Home - A Unique Horror Adventure", "Home")]
    [InlineData("Sample: The Sequel", "Sample")]
    [InlineData("Sample Quest", null)]
    [InlineData("- Odd", null)]
    public void MainTitle_IsThePartBeforeTheSubtitle(string title, string? expected)
    {
        SteamGridDbArtworkSource.MainTitle(title).ShouldBe(expected);
    }

    [Fact]
    public async Task NoMatchingTitle_IsADefinitiveNotFound_AndRemembered()
    {
        _secrets.Values[SecretNames.SteamGridDbApiKey] = Key;
        Route(r => r.AbsolutePath.Contains("/search/autocomplete/"), "steamgriddb/search.json");
        var unknown = Entry("gog", "1", "Completely Unknown Game");

        var result = await Source().GetCandidatesAsync(unknown, ArtworkKind.Cover, CancellationToken.None);

        result.ShouldBe(ArtworkCandidates.None);
        (await _lookups.GetAsync("sgdb:gog:1", CancellationToken.None))!.Id.ShouldBeNull();
    }

    [Fact]
    public async Task InvalidKey_IsUnavailable_NotAMiss()
    {
        _secrets.Values[SecretNames.SteamGridDbApiKey] = Key;
        _http.RespondWith(HttpStatusCode.Unauthorized, Fixtures.Read("steamgriddb/unauthorized.json"));

        var result = await Source().GetCandidatesAsync(_epicGame, ArtworkKind.Cover, CancellationToken.None);

        result.ShouldBe(ArtworkCandidates.Unavailable);
        (await _lookups.GetAsync("sgdb:epic:OldApp", CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData("https://cdn2.steamgriddb.com/grid/a.png", true)]
    [InlineData("https://steamgriddb.com/a.png", true)]
    [InlineData("http://cdn2.steamgriddb.com/grid/a.png", false)]
    [InlineData("https://steamgriddb.com.evil.example/a.png", false)]
    [InlineData("https://evilsteamgriddb.com/a.png", false)]
    public void TrustedImageHosts(string url, bool expected)
    {
        SteamGridDbClient.IsTrustedImageUrl(new Uri(url)).ShouldBe(expected);
    }

    private static LibraryEntry Entry(string source, string id, string title) =>
        new(new ItemRef(source, id), ItemKind.Game, title, InstallState.Installed, null, null, null, LibraryEntry.NoExtra);

    private static HttpResponseMessage Json(HttpStatusCode status, string fixture) =>
        new(status) { Content = new StringContent(Fixtures.Read(fixture)) };

    private void Route(Func<Uri, bool> match, string fixture)
    {
        var previous = _http.Respond;
        _http.Respond = r => match(r.RequestUri!) ? Json(HttpStatusCode.OK, fixture) : previous(r);
    }

    private SteamGridDbClient Client() => new(new HttpClient(_http));

    private SteamGridDbArtworkSource Source() => new(_secrets, Client(), _lookups, TitleCleanup.CreateNormalizer());

    private sealed class MemoryLookups : IArtworkLookupCache
    {
        private readonly Dictionary<string, ArtworkLookup> _values = [];

        public Task<ArtworkLookup?> GetAsync(string key, CancellationToken ct) => Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, long? id, CancellationToken ct)
        {
            _values[key] = new ArtworkLookup(id, DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct)
        {
            _values.Clear();
            return Task.CompletedTask;
        }
    }
}
