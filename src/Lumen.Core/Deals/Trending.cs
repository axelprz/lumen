// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Deals;

/// <summary>One of Steam's top sellers, with its price in US dollars.</summary>
/// <param name="Url">Its Steam store page; always one that <see cref="DealUrls.IsSafeToOpen"/> accepts.</param>
public sealed record TrendingGame(string AppId, string Title, decimal Sale, decimal Normal, int DiscountPercent, string Url);

public sealed record TrendingResult(bool Ok, IReadOnlyList<TrendingGame> Games)
{
    public static TrendingResult Failed { get; } = new(false, []);
}

/// <summary>What is popular right now: the store's own top sellers, never paid placement or ads.</summary>
public interface ITrendingSource
{
    Task<TrendingResult> TopSellersAsync(CancellationToken ct);
}

public sealed record TrendingSnapshot(IReadOnlyList<TrendingGame> Games, bool Failed, DateTimeOffset At);

/// <summary>Top sellers for the Deals page: kept in memory for <see cref="CacheLifetime"/>; a failing source never throws.</summary>
public sealed class TrendingService(ITrendingSource source, TimeProvider? time = null)
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TrendingSnapshot? _cached;

    public async Task<TrendingSnapshot> GetAsync(bool refresh, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!refresh && _cached is { Failed: false } hit && _time.GetUtcNow() - hit.At < CacheLifetime)
            {
                return hit;
            }

            var result = await AskAsync(ct).ConfigureAwait(false);
            var snapshot = new TrendingSnapshot(result.Games, !result.Ok, _time.GetUtcNow());
            if (!snapshot.Failed)
            {
                _cached = snapshot;
            }

            return snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<TrendingResult> AskAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return await source.TopSellersAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return TrendingResult.Failed;
        }
    }
}
