// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Deals;

namespace Lumen.Core.Tests.Deals;

public sealed class FreeGamesServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static FreeGame Game(string source, string title, int? endsInDays = 5, int? startsInDays = null) =>
        new(source, title, "Store", "https://www.gamerpower.com/open/x", startsInDays is { } s ? Now.AddDays(s) : null, endsInDays is { } e ? Now.AddDays(e) : null, null);

    private sealed class Source(string id, Func<FreeGamesResult> answer) : IFreeGamesSource
    {
        public int Calls { get; private set; }

        public string Id => id;

        public Task<FreeGamesResult> GetAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer());
        }
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Value;
    }

    [Fact]
    public void Merge_FreeNowFirstEndingSoonestFirst_ThenUpcoming()
    {
        var snapshot = FreeGamesService.Merge(
            [new FreeGamesResult(true, [Game("a", "Later", startsInDays: 3), Game("a", "Slow", endsInDays: 9), Game("a", "Fast", endsInDays: 2)])],
            Now);

        snapshot.Games.Select(g => g.Title).ShouldBe(["Fast", "Slow", "Later"]);
        snapshot.AnyFailed.ShouldBeFalse();
    }

    [Fact]
    public void Merge_TheSameGameFromTwoSources_IsListedOnce_KeepingTheFirstSource()
    {
        var snapshot = FreeGamesService.Merge(
            [new FreeGamesResult(true, [Game("epic", "Sample Quest")]), new FreeGamesResult(true, [Game("gamerpower", "SAMPLE  quest!")])],
            Now);

        snapshot.Games.ShouldHaveSingleItem().Source.ShouldBe("epic");
    }

    [Fact]
    public void Merge_FreeNowAndComingLaterOfTheSameTitle_AreBothKept()
    {
        var snapshot = FreeGamesService.Merge([new FreeGamesResult(true, [Game("epic", "Quest"), Game("epic", "Quest", startsInDays: 7)])], Now);

        snapshot.Games.Count.ShouldBe(2);
    }

    [Fact]
    public void Merge_OneSourceFailing_KeepsTheOthers_AndAllFailingSaysSo()
    {
        var partial = FreeGamesService.Merge([FreeGamesResult.Failed, new FreeGamesResult(true, [Game("a", "One")])], Now);
        partial.AnyFailed.ShouldBeTrue();
        partial.AllFailed.ShouldBeFalse();
        partial.Games.ShouldHaveSingleItem();

        var none = FreeGamesService.Merge([FreeGamesResult.Failed, FreeGamesResult.Failed], Now);
        none.AllFailed.ShouldBeTrue();
        none.Games.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReusesTheAnswerUntilItExpires_AndRefreshAsksAgain()
    {
        var clock = new Clock(Now);
        var source = new Source("a", () => new FreeGamesResult(true, [Game("a", "One")]));
        var service = new FreeGamesService([source], clock);

        await service.GetAsync(false, CancellationToken.None);
        await service.GetAsync(false, CancellationToken.None);
        source.Calls.ShouldBe(1);

        await service.GetAsync(true, CancellationToken.None);
        source.Calls.ShouldBe(2);

        clock.Value = Now + FreeGamesService.CacheLifetime + TimeSpan.FromMinutes(1);
        await service.GetAsync(false, CancellationToken.None);
        source.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task GetAsync_AFailedAnswerIsNotRemembered()
    {
        var ok = false;
        var source = new Source("a", () => ok ? new FreeGamesResult(true, [Game("a", "One")]) : FreeGamesResult.Failed);
        var service = new FreeGamesService([source], new Clock(Now));

        (await service.GetAsync(false, CancellationToken.None)).AllFailed.ShouldBeTrue();
        ok = true;
        (await service.GetAsync(false, CancellationToken.None)).Games.ShouldHaveSingleItem();
        source.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task GetAsync_ASourceThatThrows_IsAFailedSource()
    {
        var service = new FreeGamesService([new ThrowingSource(), new Source("b", () => new FreeGamesResult(true, [Game("b", "Two")]))]);

        var snapshot = await service.GetAsync(true, CancellationToken.None);

        snapshot.AnyFailed.ShouldBeTrue();
        snapshot.Games.ShouldHaveSingleItem().Title.ShouldBe("Two");
    }

    [Theory]
    [InlineData("https://store.epicgames.com/en-US/p/x", true)]
    [InlineData("https://www.gamerpower.com/open/x", true)]
    [InlineData("https://steamcito.com.ar/machete-del-ahorro-en-gaming-en-argentina", true)]
    [InlineData("http://store.epicgames.com/x", false)]
    [InlineData("https://evil.example.com/x", false)]
    [InlineData("https://store.epicgames.com.evil.example/x", false)]
    [InlineData("https://user:pass@store.epicgames.com/x", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSafeToOpen_OnlyHttpsOnTheKnownHosts(string? url, bool expected)
    {
        DealUrls.IsSafeToOpen(url).ShouldBe(expected);
    }

    private sealed class ThrowingSource : IFreeGamesSource
    {
        public string Id => "boom";

        public Task<FreeGamesResult> GetAsync(CancellationToken ct) => throw new InvalidOperationException("changed format");
    }
}
