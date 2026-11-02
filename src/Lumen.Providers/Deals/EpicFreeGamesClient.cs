// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lumen.Core.Deals;

namespace Lumen.Providers.Deals;

/// <summary>
/// The Epic Games Store's weekly free games: the public promotions feed the store's own web page uses (no account, no
/// key). The feed is not documented by Epic and may change shape: a feed that can't be read is a failed source, never an
/// exception. The request carries a fixed locale and country, nothing about the user.
/// </summary>
public sealed partial class EpicFreeGamesClient(HttpClient http) : IFreeGamesSource
{
    // VERIFY: unofficial public feed (store-site-backend-static.ak.epicgames.com); shape taken from community clients.
    public const string FeedUrl = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale=en-US&country=US&allowCountries=US";

    public const string StoreName = "Epic Games Store";

    private const string StoreBase = "https://store.epicgames.com/en-US/";

    public string Id => "epic";

    public async Task<FreeGamesResult> GetAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(FeedUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return FreeGamesResult.Failed;
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Parse(body, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return FreeGamesResult.Failed;
        }
    }

    /// <summary>Games whose price is 0% of normal right now, and the ones that will be. Anything unreadable is skipped.</summary>
    public static FreeGamesResult Parse(string json, DateTimeOffset now)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return FreeGamesResult.Failed;
        }

        if (root?["data"]?["Catalog"]?["searchStore"]?["elements"] is not JsonArray elements)
        {
            return FreeGamesResult.Failed;
        }

        var games = new List<FreeGame>();
        foreach (var element in elements.OfType<JsonObject>())
        {
            var title = Text(element["title"]);
            if (title is null)
            {
                continue;
            }

            var url = PageUrl(element);
            var worth = Text(element["price"]?["totalPrice"]?["fmtPrice"]?["originalPrice"]);
            var current = FreeWindows(element["promotions"]?["promotionalOffers"]).FirstOrDefault(w => w.Start <= now && now < w.End);
            if (current is not null)
            {
                games.Add(new FreeGame("epic", title, StoreName, url, null, current.End, worth));
                continue;
            }

            var next = FreeWindows(element["promotions"]?["upcomingPromotionalOffers"]).Where(w => w.Start > now).OrderBy(w => w.Start).FirstOrDefault();
            if (next is not null)
            {
                games.Add(new FreeGame("epic", title, StoreName, url, next.Start, next.End, worth));
            }
        }

        return new FreeGamesResult(true, games);
    }

    // Promotions whose discount is 0 (the game costs nothing), with their start and end.
    private static IEnumerable<Window> FreeWindows(JsonNode? groups)
    {
        if (groups is not JsonArray array)
        {
            yield break;
        }

        foreach (var offer in array.OfType<JsonObject>().SelectMany(g => g["promotionalOffers"] is JsonArray inner ? inner.OfType<JsonObject>() : []))
        {
            var percentage = offer["discountSetting"]?["discountPercentage"];
            var isFree = percentage is JsonValue v && v.TryGetValue<double>(out var p) && p == 0;
            if (isFree && Date(offer["startDate"]) is { } start && Date(offer["endDate"]) is { } end)
            {
                yield return new Window(start, end);
            }
        }
    }

    private sealed record Window(DateTimeOffset Start, DateTimeOffset End);

    private static DateTimeOffset? Date(JsonNode? node) =>
        DateTimeOffset.TryParse(Text(node), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    // The store page of the game, built from a slug that only holds letters, digits and dashes, optionally followed by "/home" (the feed is untrusted).
    private static string PageUrl(JsonObject element)
    {
        var slug = Text(element["productSlug"])
            ?? Text(element["offerMappings"]?[0]?["pageSlug"])
            ?? Text(element["catalogNs"]?["mappings"]?[0]?["pageSlug"])
            ?? Text(element["urlSlug"]);
        return slug is not null && SlugPattern().IsMatch(slug) ? $"{StoreBase}p/{slug.Split('/')[0]}" : StoreBase + "free-games";
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,120}(/home)?$")]
    private static partial Regex SlugPattern();
}
