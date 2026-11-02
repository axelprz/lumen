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
        result.Games.Select(g => g.Title).ShouldBe(["Sample Free Quest", "Sample Coming Soon", "Sample Odd Slug", "Sample Zero Price"]);

        var now = result.Games[0];
        now.IsUpcoming.ShouldBeFalse();
        now.EndsAt.ShouldBe(new DateTimeOffset(2026, 11, 8, 15, 0, 0, TimeSpan.Zero));
        now.Url.ShouldBe("https://store.epicgames.com/en-US/p/sample-free-quest");
        now.Worth.ShouldBe("$19.99");
        result.Games.Single(g => g.Title == "Sample Zero Price").Worth.ShouldBeNull();
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

        _http.Respond = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");
    }
}
