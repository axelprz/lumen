// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Deals;
using Lumen.Providers.Deals;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Deals;

public sealed class FreeGamesSourcesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly StubHttpHandler _http = new();

    [Fact]
    public void Epic_ReadsFreeNowAndUpcoming_AndSkipsPartialDiscountsAndNamelessEntries()
    {
        var result = EpicFreeGamesClient.Parse(Fixtures.Read("deals/epic_free_games.json"), Now);

        result.Ok.ShouldBeTrue();
        result.Games.Select(g => g.Title).ShouldBe(["Sample Free Quest", "Sample Coming Soon", "Sample Odd Slug"]);

        var now = result.Games[0];
        now.IsUpcoming.ShouldBeFalse();
        now.EndsAt.ShouldBe(new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero));
        now.Url.ShouldBe("https://store.epicgames.com/en-US/p/sample-free-quest");
        now.Worth.ShouldBe("$19.99");
        now.Store.ShouldBe("Epic Games Store");

        var later = result.Games[1];
        later.IsUpcoming.ShouldBeTrue();
        later.StartsAt.ShouldBe(new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero));
        later.Url.ShouldBe("https://store.epicgames.com/en-US/p/sample-coming-soon");
    }

    [Fact]
    public void Epic_AnOddSlugFromTheFeed_NeverBecomesPartOfTheLink()
    {
        var odd = EpicFreeGamesClient.Parse(Fixtures.Read("deals/epic_free_games.json"), Now).Games.Single(g => g.Title == "Sample Odd Slug");

        odd.Url.ShouldBe("https://store.epicgames.com/en-US/free-games");
        DealUrls.IsSafeToOpen(odd.Url).ShouldBeTrue();
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("{}")]
    [InlineData("{ \"data\": { \"Catalog\": null } }")]
    public void Epic_UnexpectedAnswers_AreAFailedSource(string body)
    {
        EpicFreeGamesClient.Parse(body, Now).Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task Epic_RequestGoesToTheFeedWithOnlyAFixedLocale()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("deals/epic_free_games.json"));

        var result = await new EpicFreeGamesClient(new HttpClient(_http)).GetAsync(CancellationToken.None);

        result.Ok.ShouldBeTrue();
        var uri = _http.Requests.Single();
        uri.Host.ShouldBe("store-site-backend-static.ak.epicgames.com");
        uri.AbsolutePath.ShouldBe("/freeGamesPromotions");
        uri.Query.ShouldBe("?locale=en-US&country=US&allowCountries=US");
    }

    [Fact]
    public async Task HttpErrorsAndNetworkFailures_AreAFailedSource_NotAnException()
    {
        _http.RespondWith(HttpStatusCode.InternalServerError);
        (await new EpicFreeGamesClient(new HttpClient(_http)).GetAsync(CancellationToken.None)).Ok.ShouldBeFalse();
        (await new GamerPowerClient(new HttpClient(_http)).GetAsync(CancellationToken.None)).Ok.ShouldBeFalse();

        _http.Respond = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");
        (await new GamerPowerClient(new HttpClient(_http)).GetAsync(CancellationToken.None)).Ok.ShouldBeFalse();
    }

    [Fact]
    public void GamerPower_KeepsActiveGamesOnLumensStoresWithSafeLinks_AndLeavesOutTheRest()
    {
        var result = GamerPowerClient.Parse(Fixtures.Read("deals/gamerpower_giveaways.json"));

        result.Ok.ShouldBeTrue();
        result.Games.Select(g => g.Title).ShouldBe(["Sample Giveaway Game", "Sample Endless Game", "Sample Steam And Itch"]);

        var first = result.Games[0];
        first.Store.ShouldBe("Steam");
        first.Worth.ShouldBe("$9.99");
        first.EndsAt.ShouldBe(new DateTimeOffset(2026, 11, 10, 23, 59, 0, TimeSpan.Zero));
        first.Url.ShouldBe("https://www.gamerpower.com/open/sample-giveaway-game");

        var endless = result.Games[1];
        endless.EndsAt.ShouldBeNull();
        endless.Worth.ShouldBeNull();
        endless.Store.ShouldBe("GOG");
        result.Games[2].Store.ShouldBe("Steam"); // itch.io is not one of Lumen's stores
        result.Games.ShouldAllBe(g => DealUrls.IsSafeToOpen(g.Url));
    }

    [Theory]
    [InlineData("Sorry We're Closed (Epic Games) Giveaway", "Sorry We're Closed")]
    [InlineData("Some Game Giveaway", "Some Game")]
    [InlineData("Some Game (Steam) giveaway", "Some Game")]
    [InlineData("Plain Name", "Plain Name")]
    [InlineData("Giveaway", "Giveaway")]
    public void GamerPower_CleanTitle_DropsTheGiveawaySuffix(string raw, string expected)
    {
        GamerPowerClient.CleanTitle(raw).ShouldBe(expected);
    }

    [Fact]
    public void GamerPower_NoGiveaways_IsAnEmptyAnswer_AndGarbageIsAFailure()
    {
        GamerPowerClient.Parse("{ \"status\": 0, \"status_message\": \"No active giveaways available\" }").Games.ShouldBeEmpty();
        GamerPowerClient.Parse("{ \"status\": 0 }").Ok.ShouldBeTrue();
        GamerPowerClient.Parse("<html>").Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task GamerPower_RequestHasTheFiltersOnly()
    {
        _http.RespondWith(HttpStatusCode.OK, Fixtures.Read("deals/gamerpower_giveaways.json"));

        await new GamerPowerClient(new HttpClient(_http)).GetAsync(CancellationToken.None);

        var uri = _http.Requests.Single();
        uri.Host.ShouldBe("www.gamerpower.com");
        uri.AbsolutePath.ShouldBe("/api/giveaways");
        uri.Query.ShouldBe("?platform=pc&type=game&sort-by=popularity");
    }
}
