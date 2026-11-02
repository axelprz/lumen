// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Deals;
using Lumen.Providers.Deals;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Deals;

public sealed class SteamStoreClientTests
{
    private readonly StubHttpHandler _http = new();

    private SteamStoreClient Client(TimeProvider? time = null) => new(new HttpClient(_http), time);

    [Fact]
    public async Task FreeGames_AreTheSpecialsAt100PercentWithAValidIdAndName()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("deals/steam_featured.json"));

        var result = await Client().GetAsync(CancellationToken.None);

        result.Ok.ShouldBeTrue();
        result.Games.Select(g => g.Title).ShouldBe(["Sample Free Promo", "Sample Free No End"]);

        var promo = result.Games[0];
        promo.Source.ShouldBe("steam");
        promo.Store.ShouldBe("Steam");
        promo.Url.ShouldBe("https://store.steampowered.com/app/1000010/");
        promo.EndsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1793750000));
        promo.Worth.ShouldBe("$9.99");
        promo.IsUpcoming.ShouldBeFalse();

        result.Games[1].EndsAt.ShouldBeNull();
        result.Games[1].Worth.ShouldBeNull();
        result.Games.ShouldAllBe(g => DealUrls.IsSafeToOpen(g.Url));
    }

    [Fact]
    public async Task TopSellers_AreListedOnce_WithPricesInDollars_AndBrokenEntriesSkipped()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("deals/steam_featured.json"));

        var result = await Client().TopSellersAsync(CancellationToken.None);

        result.Ok.ShouldBeTrue();
        result.Games.Select(g => g.Title).ShouldBe(["Sample Top One", "Sample Top Two", "Sample Free To Play"]);
        result.Games[0].Sale.ShouldBe(59.99m);
        result.Games[1].Sale.ShouldBe(30m);
        result.Games[1].Normal.ShouldBe(40m);
        result.Games[1].DiscountPercent.ShouldBe(25);
        result.Games[2].Sale.ShouldBe(0m);
        result.Games.ShouldAllBe(g => DealUrls.IsSafeToOpen(g.Url));
    }

    [Fact]
    public async Task OneRequest_ServesBothLists_WithAFixedCountryAndLanguage_AndIsReusedBriefly()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("deals/steam_featured.json"));
        var client = Client();

        await client.GetAsync(CancellationToken.None);
        await client.TopSellersAsync(CancellationToken.None);

        var uri = _http.Requests.Single();
        uri.Host.ShouldBe("store.steampowered.com");
        uri.AbsolutePath.ShouldBe("/api/featuredcategories");
        uri.Query.ShouldBe("?cc=us&l=english");
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("{}")]
    [InlineData("{ \"status\": 1 }")]
    public async Task UnexpectedAnswers_AreAFailedSource(string body)
    {
        _http.RespondWith(HttpStatusCode.OK, body);

        (await Client().GetAsync(CancellationToken.None)).Ok.ShouldBeFalse();
        (await Client().TopSellersAsync(CancellationToken.None)).Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task HttpErrorsAndNetworkFailures_AreAFailedSource_NotAnException()
    {
        _http.RespondWith(HttpStatusCode.InternalServerError);
        (await Client().GetAsync(CancellationToken.None)).Ok.ShouldBeFalse();

        _http.Respond = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");
        (await Client().TopSellersAsync(CancellationToken.None)).Ok.ShouldBeFalse();
    }
}
