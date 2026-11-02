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
/// Giveaways of other kinds (loot, beta access) are left out.
/// </summary>
public sealed class GamerPowerClient(HttpClient http) : IFreeGamesSource
{
    public const string SiteUrl = "https://www.gamerpower.com/";

    public const string FeedUrl = "https://www.gamerpower.com/api/giveaways?platform=pc&type=game&sort-by=popularity";

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

            var worth = Text(item["worth"]);
            games.Add(new FreeGame(
                "gamerpower",
                title,
                Text(item["platforms"]) ?? "PC",
                url!,
                null,
                End(Text(item["end_date"])),
                worth is null || worth.Equals("N/A", StringComparison.OrdinalIgnoreCase) ? null : worth));
        }

        return new FreeGamesResult(true, games);
    }

    // "2026-11-10 23:59:00" (UTC), or "N/A" when it has no end.
    private static DateTimeOffset? End(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
