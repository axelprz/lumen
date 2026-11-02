// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Deals;

namespace Lumen.Core.Tests.Deals;

public sealed class PriceDealsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static PriceDeal Deal(string game, string title, string store, decimal sale, decimal normal = 20m) =>
        new($"deal{game}{store}0123456789", game, title, store, sale, normal, normal == 0 ? 0 : (double)((normal - sale) / normal * 100), "https://www.cheapshark.com/redirect?dealID=x");

    private sealed class Source : IPriceDealsSource
    {
        public int BestCalls { get; private set; }

        public List<string> Searches { get; } = [];

        public PriceDealsResult Answer { get; set; } = new(true, []);

        public Task<PriceDealsResult> BestAsync(CancellationToken ct)
        {
            BestCalls++;
            return Task.FromResult(Answer);
        }

        public Task<PriceDealsResult> SearchAsync(string title, CancellationToken ct)
        {
            Searches.Add(title);
            return Task.FromResult(Answer);
        }
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Value;
    }

    [Theory]
    [InlineData("  half   life  ", "half life")]
    [InlineData("Portal", "Portal")]
    [InlineData("a\tb\nc", "a b c")]
    [InlineData("x", null)]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CleanQuery_CollapsesSpaces_AndRefusesTooShortText(string? text, string? expected)
    {
        PriceDealsService.CleanQuery(text).ShouldBe(expected);
    }

    [Fact]
    public void CleanQuery_RefusesTooLongText_InsteadOfCuttingIt()
    {
        PriceDealsService.CleanQuery(new string('a', PriceDealsService.MaxQueryLength)).ShouldNotBeNull();
        PriceDealsService.CleanQuery(new string('a', PriceDealsService.MaxQueryLength + 1)).ShouldBeNull();
    }

    [Fact]
    public void Group_OneEntryPerGame_CheapestStoreFirst_OneOfferPerStore()
    {
        var games = PriceDealsService.Group(
            [Deal("1", "Portal", "GOG", 5m), Deal("1", "Portal", "Steam", 3m), Deal("1", "Portal", "Steam", 4m), Deal("2", "Portal 2", "Steam", 10m)],
            "portal");

        games.Select(g => g.Title).ShouldBe(["Portal", "Portal 2"]);
        games[0].Stores.Select(d => (d.Store, d.Sale)).ShouldBe([("Steam", 3m), ("GOG", 5m)]);
        games[0].Best.Store.ShouldBe("Steam");
    }

    [Fact]
    public void Group_NamesStartingWithTheSearchComeFirst_ThenTheShorterOnes()
    {
        var games = PriceDealsService.Group(
            [Deal("1", "The Long Portal Sequel Edition", "Steam", 1m), Deal("2", "Portal Knights Deluxe", "Steam", 2m), Deal("3", "Portal", "Steam", 9m)],
            "portal");

        games.Select(g => g.Title).ShouldBe(["Portal", "Portal Knights Deluxe", "The Long Portal Sequel Edition"]);
    }

    [Fact]
    public async Task Best_IsReusedUntilItExpires_RefreshAsksAgain_AndAFailureIsNotRemembered()
    {
        var clock = new Clock(Now);
        var source = new Source { Answer = new PriceDealsResult(true, [Deal("1", "Portal", "Steam", 3m)]) };
        var service = new PriceDealsService(source, clock);

        await service.BestAsync(false, CancellationToken.None);
        await service.BestAsync(false, CancellationToken.None);
        source.BestCalls.ShouldBe(1);

        await service.BestAsync(true, CancellationToken.None);
        source.BestCalls.ShouldBe(2);

        clock.Value = Now + PriceDealsService.CacheLifetime + TimeSpan.FromMinutes(1);
        await service.BestAsync(false, CancellationToken.None);
        source.BestCalls.ShouldBe(3);

        var failing = new Source { Answer = PriceDealsResult.Failed };
        var second = new PriceDealsService(failing, clock);
        (await second.BestAsync(false, CancellationToken.None)).Failed.ShouldBeTrue();
        failing.Answer = new PriceDealsResult(true, [Deal("1", "Portal", "Steam", 3m)]);
        (await second.BestAsync(false, CancellationToken.None)).Deals.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Search_SendsTheCleanedText_AndRemembersTheAnswerForAWhile()
    {
        var clock = new Clock(Now);
        var source = new Source { Answer = new PriceDealsResult(true, [Deal("1", "Portal", "Steam", 3m)]) };
        var service = new PriceDealsService(source, clock);

        var first = await service.SearchAsync("  PORTAL ", CancellationToken.None);
        await service.SearchAsync("portal", CancellationToken.None);

        first.Games.ShouldHaveSingleItem();
        source.Searches.ShouldBe(["PORTAL"]);

        clock.Value = Now + PriceDealsService.SearchCacheLifetime + TimeSpan.FromMinutes(1);
        await service.SearchAsync("portal", CancellationToken.None);
        source.Searches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Search_TooShortText_NeverReachesTheService()
    {
        var source = new Source();

        var result = await new PriceDealsService(source).SearchAsync("a", CancellationToken.None);

        result.Games.ShouldBeEmpty();
        result.Failed.ShouldBeFalse();
        source.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task AServiceThatThrows_IsAFailedAnswer()
    {
        var service = new PriceDealsService(new ThrowingSource());

        (await service.BestAsync(true, CancellationToken.None)).Failed.ShouldBeTrue();
        (await service.SearchAsync("portal", CancellationToken.None)).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://www.cheapshark.com/redirect?dealID=AAAAAAAAAAbbbbbbbbbb%2B0123456789%3D", true)]
    [InlineData("https://www.cheapshark.com/", true)]
    [InlineData("https://www.cheapshark.com/redirect?dealID=x", false)]
    [InlineData("https://www.cheapshark.com/redirect?dealID=AAAAAAAAAAbbbbbbbbbb&evil=1", false)]
    [InlineData("https://www.cheapshark.com/api/1.0/deals", false)]
    [InlineData("http://www.cheapshark.com/redirect?dealID=AAAAAAAAAAbbbbbbbbbb", false)]
    [InlineData("https://cheapshark.com.evil.example/redirect?dealID=AAAAAAAAAAbbbbbbbbbb", false)]
    public void IsSafeToOpen_CheapSharkOnlyForItsRedirectAndHomePage(string url, bool expected)
    {
        DealUrls.IsSafeToOpen(url).ShouldBe(expected);
    }

    private sealed class ThrowingSource : IPriceDealsSource
    {
        public Task<PriceDealsResult> BestAsync(CancellationToken ct) => throw new InvalidOperationException("changed format");

        public Task<PriceDealsResult> SearchAsync(string title, CancellationToken ct) => throw new InvalidOperationException("changed format");
    }
}
