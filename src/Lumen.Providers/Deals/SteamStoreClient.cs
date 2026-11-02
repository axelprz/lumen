// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumen.Core.Deals;

namespace Lumen.Providers.Deals;

/// <summary>
/// Steam's own store feed (<c>store.steampowered.com/api/featuredcategories</c>, no account, no key): the games that are
/// 100 % off right now (free to keep) and the top sellers. The feed is not documented by Valve and may change shape: one that
/// can't be read is a failed source, never an exception. The request carries a fixed country and language (United States,
/// English: prices in US dollars like the rest of Deals), nothing about the user. The answer is reused for a few minutes
/// so the free games and the top sellers share one request.
/// </summary>
public sealed class SteamStoreClient(HttpClient http, TimeProvider? time = null) : IFreeGamesSource, ITrendingSource
{
    // VERIFY: unofficial Steam store feed; shape taken from what the store returns today.
    public const string FeedUrl = "https://store.steampowered.com/api/featuredcategories?cc=us&l=english";

    public const string StoreName = "Steam";

    /// <summary>How many top sellers are kept.</summary>
    public const int MaxTopSellers = 12;

    private static readonly TimeSpan Reuse = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (Featured Data, DateTimeOffset At)? _cached;

    public string Id => "steam";

    public async Task<FreeGamesResult> GetAsync(CancellationToken ct) =>
        await FeaturedAsync(ct).ConfigureAwait(false) is { } featured ? new FreeGamesResult(true, featured.FreeGames) : FreeGamesResult.Failed;

    public async Task<TrendingResult> TopSellersAsync(CancellationToken ct) =>
        await FeaturedAsync(ct).ConfigureAwait(false) is { } featured ? new TrendingResult(true, featured.TopSellers) : TrendingResult.Failed;

    private async Task<Featured?> FeaturedAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is { } hit && _time.GetUtcNow() - hit.At < Reuse)
            {
                return hit.Data;
            }

            using var response = await http.GetAsync(FeedUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = Parse(body);
            if (parsed is not null)
            {
                _cached = (parsed, _time.GetUtcNow());
            }

            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The free-to-keep games (specials at 100 % off) and the top sellers of a feed; null when it has neither list.</summary>
    internal static Featured? Parse(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        var specials = root?["specials"]?["items"] as JsonArray;
        var topSellers = root?["top_sellers"]?["items"] as JsonArray;
        if (specials is null && topSellers is null)
        {
            return null;
        }

        var free = new List<FreeGame>();
        foreach (var item in (specials ?? []).OfType<JsonObject>())
        {
            if (Game(item) is { } game && game.Discount == 100 && game.Sale == 0)
            {
                var end = Number(item["discount_expiration"]) is { } seconds and > 0 && seconds < 4_102_444_800
                    ? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
                    : (DateTimeOffset?)null;
                free.Add(new FreeGame("steam", game.Title, StoreName, game.Url, null, end, game.Normal > 0 ? "$" + game.Normal.ToString("0.00", CultureInfo.InvariantCulture) : null));
            }
        }

        var top = (topSellers ?? []).OfType<JsonObject>()
            .Select(Game)
            .OfType<Parsed>()
            .DistinctBy(g => g.AppId)
            .Take(MaxTopSellers)
            .Select(g => new TrendingGame(g.AppId, g.Title, g.Sale, g.Normal, g.Discount, g.Url))
            .ToList();
        return new Featured(free, top);
    }

    private static Parsed? Game(JsonObject item)
    {
        var idNumber = Number(item["id"]);
        var finalCents = Number(item["final_price"]);
        var originalCents = Number(item["original_price"]);
        var title = item["name"] is JsonValue v && v.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;
        if (idNumber is not > 0 or >= 100_000_000_000 || title is null || finalCents is null or < 0 || originalCents is null or < 0)
        {
            return null;
        }

        var id = ((long)idNumber.Value).ToString(CultureInfo.InvariantCulture);
        var discount = Number(item["discount_percent"]) is { } d ? (int)Math.Clamp(d, 0, 100) : 0;
        return new Parsed(id, title, (decimal)finalCents.Value / 100m, (decimal)originalCents.Value / 100m, discount, $"https://store.steampowered.com/app/{id}/");
    }

    private static double? Number(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : null;

    private sealed record Parsed(string AppId, string Title, decimal Sale, decimal Normal, int Discount, string Url);

    internal sealed record Featured(IReadOnlyList<FreeGame> FreeGames, IReadOnlyList<TrendingGame> TopSellers);
}
