// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lumen.Core.Deals;

namespace Lumen.Providers.Deals;

/// <summary>
/// CheapShark's price comparison (no account, no key; prices in US dollars, taxes not included). Only offers of the
/// stores Lumen supports are asked for and kept (owner, 2026-11-02): the store list is read once and matched by name, so a
/// store CheapShark does not run any more (Origin, Blizzard Shop today) simply does not show. A search sends only the text
/// the user typed. A service that fails or changes format is a failed answer, never an exception.
/// </summary>
public sealed partial class CheapSharkClient(HttpClient http) : IPriceDealsSource
{
    public const string BaseUrl = "https://www.cheapshark.com/api/1.0/";

    public const string SiteUrl = "https://www.cheapshark.com/";

    /// <summary>CheapShark's store names for the stores Lumen has, with the name Lumen shows.</summary>
    public static readonly IReadOnlyDictionary<string, string> LumenStores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam"] = "Steam",
        ["Epic Games Store"] = "Epic Games Store",
        ["GOG"] = "GOG",
        ["Uplay"] = "Ubisoft Connect",
        ["Origin"] = "EA app",
        ["Blizzard Shop"] = "Battle.net",
    };

    private readonly SemaphoreSlim _storesLock = new(1, 1);
    private IReadOnlyDictionary<string, string>? _stores;

    public Task<PriceDealsResult> BestAsync(CancellationToken ct) =>
        AskAsync(ids => $"deals?storeID={ids}&onSale=1&sortBy=Deal%20Rating&pageSize=20", ct);

    public Task<PriceDealsResult> SearchAsync(string title, CancellationToken ct) =>
        AskAsync(ids => $"deals?storeID={ids}&title={Uri.EscapeDataString(title)}&sortBy=Price&pageSize=40", ct);

    private async Task<PriceDealsResult> AskAsync(Func<string, string> request, CancellationToken ct)
    {
        try
        {
            var stores = await StoresAsync(ct).ConfigureAwait(false);
            if (stores.Count == 0)
            {
                return PriceDealsResult.Failed;
            }

            var body = await GetAsync(request(string.Join(',', stores.Keys.Order(StringComparer.Ordinal))), ct).ConfigureAwait(false);
            return body is null ? PriceDealsResult.Failed : ParseDeals(body, stores);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return PriceDealsResult.Failed;
        }
    }

    // The ids of the stores Lumen supports, read once from CheapShark's own list (only the ones it still runs).
    private async Task<IReadOnlyDictionary<string, string>> StoresAsync(CancellationToken ct)
    {
        if (_stores is { } known)
        {
            return known;
        }

        await _storesLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_stores is null)
            {
                var body = await GetAsync("stores", ct).ConfigureAwait(false);
                var stores = body is null ? new Dictionary<string, string>() : ParseStores(body);
                if (stores.Count == 0)
                {
                    return stores; // not remembered: try again next time
                }

                _stores = stores;
            }

            return _stores;
        }
        finally
        {
            _storesLock.Release();
        }
    }

    private async Task<string?> GetAsync(string relative, CancellationToken ct)
    {
        using var response = await http.GetAsync(BaseUrl + relative, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
    }

    /// <summary>Store id → the name Lumen shows, for the active stores of <see cref="LumenStores"/>.</summary>
    public static IReadOnlyDictionary<string, string> ParseStores(string json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Parse(json) is not JsonArray array)
        {
            return result;
        }

        foreach (var store in array.OfType<JsonObject>())
        {
            var id = Text(store["storeID"]);
            var name = Text(store["storeName"]);
            var active = Text(store["isActive"]) == "1";
            if (active && id is not null && IdPattern().IsMatch(id) && name is not null && LumenStores.TryGetValue(name, out var shown))
            {
                result[id] = shown;
            }
        }

        return result;
    }

    /// <summary>Offers of the given stores. An entry without a valid id, a price or a known store is skipped.</summary>
    public static PriceDealsResult ParseDeals(string json, IReadOnlyDictionary<string, string> stores)
    {
        if (Parse(json) is not JsonArray array)
        {
            return PriceDealsResult.Failed;
        }

        var deals = new List<PriceDeal>();
        foreach (var item in array.OfType<JsonObject>())
        {
            var dealId = Text(item["dealID"]);
            var gameId = Text(item["gameID"]);
            var title = Text(item["title"]);
            var storeId = Text(item["storeID"]);
            if (dealId is null || !DealIdPattern().IsMatch(dealId) || gameId is null || !IdPattern().IsMatch(gameId)
                || title is null || storeId is null || !stores.TryGetValue(storeId, out var store)
                || !Money(item["salePrice"], out var sale) || !Money(item["normalPrice"], out var normal))
            {
                continue;
            }

            var savings = double.TryParse(Text(item["savings"]), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 0, 100) : 0;
            var appId = Text(item["steamAppID"]);
            deals.Add(new PriceDeal(dealId, gameId, title, store, sale, normal, savings, RedirectUrl(dealId), appId is not null && DealUrls.IsSteamAppId(appId) ? appId : null));
        }

        return new PriceDealsResult(true, deals);
    }

    /// <summary>CheapShark's own link that opens the offer in the store. The id must be letters, digits and %-escapes.</summary>
    public static string RedirectUrl(string dealId) => "https://www.cheapshark.com/redirect?dealID=" + dealId;

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Prices come as text ("4.99") or numbers; never negative or absurd.
    private static bool Money(JsonNode? node, out decimal value) =>
        decimal.TryParse(Text(node), NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value is >= 0 and < 100000;

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) => text.Trim(),
        JsonValue v when v.TryGetValue<decimal>(out var number) => number.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    [GeneratedRegex("^[0-9]{1,10}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9%]{10,120}$")]
    private static partial Regex DealIdPattern();
}
