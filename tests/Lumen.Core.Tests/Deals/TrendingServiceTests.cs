// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Deals;

namespace Lumen.Core.Tests.Deals;

public sealed class TrendingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static TrendingGame Game(string id) => new(id, "Game " + id, 10m, 20m, 50, $"https://store.steampowered.com/app/{id}/");

    private sealed class Source : ITrendingSource
    {
        public int Calls { get; private set; }

        public TrendingResult Answer { get; set; } = new(true, [Game("1")]);

        public Task<TrendingResult> TopSellersAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Answer);
        }
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Value;
    }

    [Fact]
    public async Task Answer_IsReusedUntilItExpires_AndRefreshAsksAgain()
    {
        var clock = new Clock(Now);
        var source = new Source();
        var service = new TrendingService(source, clock);

        await service.GetAsync(false, CancellationToken.None);
        await service.GetAsync(false, CancellationToken.None);
        source.Calls.ShouldBe(1);

        await service.GetAsync(true, CancellationToken.None);
        source.Calls.ShouldBe(2);

        clock.Value = Now + TrendingService.CacheLifetime + TimeSpan.FromMinutes(1);
        await service.GetAsync(false, CancellationToken.None);
        source.Calls.ShouldBe(3);
    }

    [Fact]
    public async Task AFailedAnswer_IsNotRemembered_AndAThrowingSourceIsAFailure()
    {
        var source = new Source { Answer = TrendingResult.Failed };
        var service = new TrendingService(source, new Clock(Now));

        (await service.GetAsync(false, CancellationToken.None)).Failed.ShouldBeTrue();
        source.Answer = new TrendingResult(true, [Game("1")]);
        (await service.GetAsync(false, CancellationToken.None)).Games.ShouldHaveSingleItem();

        (await new TrendingService(new ThrowingSource()).GetAsync(true, CancellationToken.None)).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://store.steampowered.com/app/1000010/", true)]
    [InlineData("https://store.steampowered.com/app/1000010", true)]
    [InlineData("https://store.steampowered.com/app/10/?utm=x", false)]
    [InlineData("https://store.steampowered.com/app/abc/", false)]
    [InlineData("https://store.steampowered.com/login/", false)]
    [InlineData("https://store.steampowered.com/", false)]
    [InlineData("https://steamcommunity.com/app/10/", false)]
    [InlineData("http://store.steampowered.com/app/10/", false)]
    [InlineData("https://store.steampowered.com.evil.example/app/10/", false)]
    public void IsSafeToOpen_SteamOnlyForAGamesStorePage(string url, bool expected)
    {
        DealUrls.IsSafeToOpen(url).ShouldBe(expected);
    }

    [Theory]
    [InlineData("123", "https://cdn.cloudflare.steamstatic.com/steam/apps/123/header.jpg")]
    [InlineData("../x", null)]
    [InlineData("12a", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SteamPosterUrl_OnlyForAPlainAppId(string? id, string? expected)
    {
        DealUrls.SteamPosterUrl(id).ShouldBe(expected);
    }

    [Theory]
    [InlineData("https://store.steampowered.com/app/620/", "620")]
    [InlineData("https://store.steampowered.com/app/620", "620")]
    [InlineData("https://store.steampowered.com/login/", null)]
    [InlineData("https://store.epicgames.com/app/620/", null)]
    [InlineData("not a url", null)]
    public void SteamAppIdOf_ReadsOnlyAGamesStorePage(string url, string? expected)
    {
        DealUrls.SteamAppIdOf(url).ShouldBe(expected);
    }

    private sealed class ThrowingSource : ITrendingSource
    {
        public Task<TrendingResult> TopSellersAsync(CancellationToken ct) => throw new InvalidOperationException("changed format");
    }
}
