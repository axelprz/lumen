// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using Lumen.Core.Deals;
using Lumen.Providers.Deals;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Deals;

public sealed class CheapSharkClientTests
{
    private readonly StubHttpHandler _http = new();

    private CheapSharkClient Client() => new(new HttpClient(_http));

    private void ServeFixtures(string deals)
    {
        _http.Respond = request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("/stores", StringComparison.Ordinal)
                ? Fixtures.Read("deals/cheapshark_stores.json")
                : deals),
        };
    }

    [Fact]
    public void Stores_OnlyTheActiveOnesLumenHas_AreKept()
    {
        var stores = CheapSharkClient.ParseStores(Fixtures.Read("deals/cheapshark_stores.json"));

        stores.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value))
            .ShouldBe([("1", "Steam"), ("13", "Ubisoft Connect"), ("25", "Epic Games Store"), ("7", "GOG")]);
    }

    [Fact]
    public void Deals_ReadPricesAndBuildTheRedirectLink_AndSkipBadEntries()
    {
        var stores = CheapSharkClient.ParseStores(Fixtures.Read("deals/cheapshark_stores.json"));

        var result = CheapSharkClient.ParseDeals(Fixtures.Read("deals/cheapshark_deals.json"), stores);

        result.Ok.ShouldBeTrue();
        // Humble Store is not one of Lumen's stores; the bad id, the bad price and the nameless entry are skipped.
        result.Deals.Select(d => (d.Title, d.Store)).ShouldBe(
        [
            ("Sample Portal Game", "Steam"),
            ("Sample Portal Game", "GOG"),
            ("Sample Portal Game", "Epic Games Store"),
            ("Portal Sequel Deluxe Edition", "Steam"),
            ("Another Portal", "Ubisoft Connect"),
        ]);

        var first = result.Deals[0];
        first.Sale.ShouldBe(4.99m);
        first.Normal.ShouldBe(19.99m);
        first.SavingsPercent.ShouldBe(75.037519, 0.0001);
        first.IsOnSale.ShouldBeTrue();
        first.Url.ShouldBe("https://www.cheapshark.com/redirect?dealID=AAAAAAAAAAbbbbbbbbbb%2B0123456789%3D");
        result.Deals.ShouldAllBe(d => DealUrls.IsSafeToOpen(d.Url));
        result.Deals[2].IsOnSale.ShouldBeFalse();
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("{}")]
    public void Deals_UnexpectedAnswers_AreAFailure(string body)
    {
        CheapSharkClient.ParseDeals(body, new Dictionary<string, string> { ["1"] = "Steam" }).Ok.ShouldBeFalse();
    }

    [Fact]
    public void SteamAppId_IsKeptOnlyWhenItIsPlainDigits()
    {
        var stores = new Dictionary<string, string> { ["1"] = "Steam", ["7"] = "GOG", ["25"] = "Epic" };
        var deals = CheapSharkClient.ParseDeals(Fixtures.Read("deals/cheapshark_deals.json"), stores).Deals;

        deals.Where(d => d.SteamAppId is not null).Select(d => d.SteamAppId).ShouldBe(["620"]);
    }

    [Fact]
    public async Task Best_AsksForTheStoresOfLumenOnly_AndReadsTheStoreListOnce()
    {
        ServeFixtures(Fixtures.Read("deals/cheapshark_deals.json"));
        var client = Client();

        var first = await client.BestAsync(CancellationToken.None);
        await client.BestAsync(CancellationToken.None);

        first.Ok.ShouldBeTrue();
        _http.Requests.Count(r => r.AbsolutePath.EndsWith("/stores", StringComparison.Ordinal)).ShouldBe(1);
        var deals = _http.Requests.First(r => r.AbsolutePath.EndsWith("/deals", StringComparison.Ordinal));
        deals.Host.ShouldBe("www.cheapshark.com");
        deals.Query.ShouldContain("storeID=1,13,25,7");
        deals.Query.ShouldContain("onSale=1");
        deals.Query.ShouldContain("pageSize=20");
    }

    [Fact]
    public async Task Search_SendsOnlyTheTypedText_Escaped()
    {
        ServeFixtures("[]");

        await Client().SearchAsync("half life & co?", CancellationToken.None);

        var deals = _http.Requests.Single(r => r.AbsolutePath.EndsWith("/deals", StringComparison.Ordinal));
        deals.Query.ShouldContain("title=half%20life%20%26%20co%3F");
        deals.Query.ShouldContain("storeID=1,13,25,7");
        deals.Query.ShouldNotContain("&co");
    }

    [Fact]
    public async Task HttpErrorsAndNetworkFailures_AreAFailure_NotAnException()
    {
        _http.RespondWith(HttpStatusCode.InternalServerError);
        (await Client().BestAsync(CancellationToken.None)).Ok.ShouldBeFalse();

        _http.Respond = _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");
        (await Client().SearchAsync("portal", CancellationToken.None)).Ok.ShouldBeFalse();
    }

    [Fact]
    public async Task AnEmptyStoreList_IsAFailure_AndIsAskedAgainNextTime()
    {
        _http.RespondWith(HttpStatusCode.OK, "[]");
        var client = Client();
        (await client.BestAsync(CancellationToken.None)).Ok.ShouldBeFalse();

        ServeFixtures(Fixtures.Read("deals/cheapshark_deals.json"));
        (await client.BestAsync(CancellationToken.None)).Ok.ShouldBeTrue();
    }
}
