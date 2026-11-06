// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Deals;

/// <summary>One price of one game in one store, in US dollars (what the price service reports).</summary>
/// <param name="DealId">The service's own id for this offer (letters, digits and %-escapes only).</param>
/// <param name="GameId">The service's id for the game, shared by every store's offer of it.</param>
/// <param name="Store">The store's name as Lumen calls it ("Steam", "Epic Games Store"…).</param>
/// <param name="Sale">What it costs now.</param>
/// <param name="Normal">What it costs without the discount.</param>
/// <param name="Url">Where to open the offer in the user's browser; always one that <see cref="DealUrls.IsSafeToOpen"/> accepts.</param>
public sealed record PriceDeal(string DealId, string GameId, string Title, string Store, decimal Sale, decimal Normal, double SavingsPercent, string Url, string? SteamAppId = null)
{
    public bool IsOnSale => SavingsPercent >= 1 && Sale < Normal;
}

/// <summary>What the price service answered; a service that fails returns <see cref="Failed"/> instead of throwing.</summary>
public sealed record PriceDealsResult(bool Ok, IReadOnlyList<PriceDeal> Deals)
{
    public static PriceDealsResult Failed { get; } = new(false, []);
}

/// <summary>One game with its price in each of Lumen's stores, cheapest first.</summary>
public sealed record PriceGame(string GameId, string Title, IReadOnlyList<PriceDeal> Stores)
{
    public PriceDeal Best => Stores[0];
}

/// <summary>
/// A price service (CheapShark): the best deals today and the price of a game by name, both only for the stores Lumen
/// supports. Read-only; nothing about the user goes with a request except, for a search, the text the user typed.
/// </summary>
public interface IPriceDealsSource
{
    Task<PriceDealsResult> BestAsync(CancellationToken ct);

    Task<PriceDealsResult> SearchAsync(string title, CancellationToken ct);
}
