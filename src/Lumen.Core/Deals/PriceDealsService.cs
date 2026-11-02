// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Deals;

/// <summary>The best deals of today, or why there are none.</summary>
public sealed record BestDealsSnapshot(IReadOnlyList<PriceDeal> Deals, bool Failed, DateTimeOffset At);

/// <summary>The games that matched a search, grouped, or why there are none.</summary>
public sealed record PriceSearchSnapshot(string Query, IReadOnlyList<PriceGame> Games, bool Failed);

/// <summary>
/// Prices for the Deals page (PLAN-v1 M13), only used once the user turned Deals on. The best deals are kept in memory
/// for <see cref="CacheLifetime"/>; searches for a few minutes. A failing service never throws.
/// </summary>
public sealed class PriceDealsService(IPriceDealsSource source, TimeProvider? time = null)
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan SearchCacheLifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>The shortest and longest text sent as a search.</summary>
    public const int MinQueryLength = 2;

    public const int MaxQueryLength = 60;

    private const int MaxSearchesKept = 20;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, (PriceSearchSnapshot Snapshot, DateTimeOffset At)> _searches = new(StringComparer.OrdinalIgnoreCase);
    private BestDealsSnapshot? _best;

    /// <summary>
    /// What is sent to the service for a search: the user's text with control characters dropped and spaces collapsed;
    /// null when it is too short or too long (the user is asked to type something else instead of cutting it).
    /// </summary>
    public static string? CleanQuery(string? text)
    {
        var words = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var clean = string.Join(' ', words.Select(w => new string(w.Where(c => !char.IsControl(c)).ToArray())).Where(w => w.Length > 0));
        return clean.Length is >= MinQueryLength and <= MaxQueryLength ? clean : null;
    }

    public async Task<BestDealsSnapshot> BestAsync(bool refresh, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!refresh && _best is { Failed: false } hit && _time.GetUtcNow() - hit.At < CacheLifetime)
            {
                return hit;
            }

            var result = await AskAsync(source.BestAsync, ct).ConfigureAwait(false);
            var snapshot = new BestDealsSnapshot(result.Deals, !result.Ok, _time.GetUtcNow());
            if (!snapshot.Failed)
            {
                _best = snapshot;
            }

            return snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Prices of the games whose name contains <paramref name="query"/> (already cleaned with <see cref="CleanQuery"/>).</summary>
    public async Task<PriceSearchSnapshot> SearchAsync(string query, CancellationToken ct)
    {
        var clean = CleanQuery(query);
        if (clean is null)
        {
            return new PriceSearchSnapshot(query, [], false);
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_searches.TryGetValue(clean, out var hit) && _time.GetUtcNow() - hit.At < SearchCacheLifetime)
            {
                return hit.Snapshot;
            }

            var result = await AskAsync(t => source.SearchAsync(clean, t), ct).ConfigureAwait(false);
            var snapshot = new PriceSearchSnapshot(clean, result.Ok ? Group(result.Deals, clean) : [], !result.Ok);
            if (!snapshot.Failed)
            {
                if (_searches.Count >= MaxSearchesKept)
                {
                    _searches.Remove(_searches.OrderBy(kv => kv.Value.At).First().Key);
                }

                _searches[clean] = (snapshot, _time.GetUtcNow());
            }

            return snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// One entry per game, each with its prices cheapest first (one offer per store). Games whose name starts with the
    /// search come first, then the shorter names (the closest ones), then the cheaper.
    /// </summary>
    public static IReadOnlyList<PriceGame> Group(IEnumerable<PriceDeal> deals, string query)
    {
        ArgumentNullException.ThrowIfNull(deals);
        return deals
            .GroupBy(d => d.GameId, StringComparer.Ordinal)
            .Select(g =>
            {
                var perStore = g.GroupBy(d => d.Store, StringComparer.Ordinal).Select(s => s.OrderBy(d => d.Sale).First()).OrderBy(d => d.Sale).ThenBy(d => d.Store, StringComparer.Ordinal).ToList();
                return new PriceGame(g.Key, perStore[0].Title, perStore);
            })
            .OrderBy(g => g.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(g => g.Title.Length)
            .ThenBy(g => g.Best.Sale)
            .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static async Task<PriceDealsResult> AskAsync(Func<CancellationToken, Task<PriceDealsResult>> ask, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return await ask(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return PriceDealsResult.Failed; // a service that is down or changed its format never breaks the page
        }
    }
}
