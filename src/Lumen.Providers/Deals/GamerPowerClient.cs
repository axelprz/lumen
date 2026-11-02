// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumen.Core.Deals;

namespace Lumen.Providers.Deals;

/// <summary>
/// GamerPower's list of free games and giveaways on PC stores (no account, no key; its terms ask for a visible link back
/// to gamerpower.com, which the Deals page shows). The request carries only the filters, nothing about the user.
/// Only giveaways of games on a store Lumen supports are kept (owner, 2026-11-02): other stores and DRM-free pages
/// (itch.io and the like) are left out, and so are other kinds (loot, beta access).
/// </summary>
public sealed class GamerPowerClient(HttpClient http) : IFreeGamesSource
{
    public const string SiteUrl = "https://www.gamerpower.com/";

    public const string FeedUrl = "https://www.gamerpower.com/api/giveaways?platform=pc&type=game&sort-by=popularity";

    /// <summary>
    /// The platform names GamerPower uses for the stores Lumen has (its <c>platforms</c> text is a comma-separated list such
    /// as "PC, Steam"), with the name shown to the user.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LumenStores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam"] = "Steam",
        ["Epic Games Store"] = "Epic Games Store",
        ["GOG"] = "GOG",
        ["Ubisoft"] = "Ubisoft Connect",
        ["Ubisoft Connect"] = "Ubisoft Connect",
        ["Origin"] = "EA app",
        ["EA"] = "EA app",
        ["EA app"] = "EA app",
        ["Battle.net"] = "Battle.net",
    };

    public string Id => "gamerpower";

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
            return Parse(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return FreeGamesResult.Failed;
        }
    }

    /// <summary>Active full-game giveaways. An entry without a safe link or a title is skipped.</summary>
    public static FreeGamesResult Parse(string json)
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

        // With no giveaways GamerPower answers an object with a status message instead of a list.
        if (root is JsonObject)
        {
            return new FreeGamesResult(true, []);
        }

        if (root is not JsonArray array)
        {
            return FreeGamesResult.Failed;
        }

        var games = new List<FreeGame>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var title = Text(item["title"]);
            var url = Text(item["open_giveaway_url"]);
            var type = Text(item["type"]);
            var status = Text(item["status"]);
            if (title is null || !DealUrls.IsSafeToOpen(url) || !string.Equals(type, "Game", StringComparison.OrdinalIgnoreCase)
                || (status is not null && !string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var stores = StoresOf(Text(item["platforms"]));
            if (stores.Count == 0)
            {
                continue; // not on a store Lumen supports
            }

            var worth = Text(item["worth"]);
            games.Add(new FreeGame(
                "gamerpower",
                title,
                string.Join(", ", stores),
                url!,
                null,
                End(Text(item["end_date"])),
                worth is null || worth.Equals("N/A", StringComparison.OrdinalIgnoreCase) ? null : worth));
        }

        return new FreeGamesResult(true, games);
    }

    // The stores of Lumen named in a platforms text ("PC, Steam, itch.io" -> Steam), in the order given, once each.
    private static List<string> StoresOf(string? platforms) =>
        (platforms ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(p => LumenStores.TryGetValue(p, out var name) ? name : null)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .ToList();

    // "2026-11-10 23:59:00" (UTC), or "N/A" when it has no end.
    private static DateTimeOffset? End(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
