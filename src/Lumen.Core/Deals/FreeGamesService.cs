// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text;

namespace Lumen.Core.Deals;

/// <summary>The merged list of every source, with whether some of them did not answer.</summary>
/// <param name="AnyFailed">At least one source failed (the list may be incomplete).</param>
/// <param name="AllFailed">Every source failed and there is nothing to show.</param>
public sealed record FreeGamesSnapshot(IReadOnlyList<FreeGame> Games, bool AnyFailed, bool AllFailed, DateTimeOffset At);

/// <summary>
/// Free games from every source, merged (PLAN-v1 M13). Only called once the user turned Deals on. Sources run in
/// parallel, each with a timeout; one failing never hides the others, and nothing throws. The answer is kept in memory
/// for <see cref="CacheLifetime"/> so opening the page again does not ask the services again ("Refresh" does).
/// </summary>
public sealed class FreeGamesService(IEnumerable<IFreeGamesSource> sources, TimeProvider? time = null)
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(20);

    private readonly IReadOnlyList<IFreeGamesSource> _sources = sources.ToList();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FreeGamesSnapshot? _cached;

    public async Task<FreeGamesSnapshot> GetAsync(bool refresh, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!refresh && _cached is { AllFailed: false } hit && _time.GetUtcNow() - hit.At < CacheLifetime)
            {
                return hit;
            }

            var results = await Task.WhenAll(_sources.Select(s => AskAsync(s, ct))).ConfigureAwait(false);
            var snapshot = Merge(results, _time.GetUtcNow());
            if (!snapshot.AllFailed)
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

    /// <summary>
    /// Free now first (the one ending soonest first), then what comes later. The same game from two services is listed
    /// once, keeping the first source's entry (sources are given in order of trust: the store's own feed first).
    /// </summary>
    public static FreeGamesSnapshot Merge(IReadOnlyList<FreeGamesResult> results, DateTimeOffset now)
    {
        var all = results.Where(r => r.Ok).SelectMany(r => r.Games).ToList();
        // The Epic feed is the authority for the Epic store: a giveaway another service lists for that store is the same game under another name.
        var epicFeed = all.Any(g => g.Source == "epic");
        var games = new List<FreeGame>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var game in all)
        {
            if (epicFeed && game.Source != "epic" && game.Store == "Epic Games Store")
            {
                continue;
            }

            if (seen.Add(Key(game)))
            {
                games.Add(game);
            }
        }

        var ordered = games
            .OrderBy(g => g.IsUpcoming ? 1 : 0)
            .ThenBy(g => g.IsUpcoming ? g.StartsAt : g.EndsAt ?? DateTimeOffset.MaxValue)
            .ThenBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var failed = results.Count(r => !r.Ok);
        return new FreeGamesSnapshot(ordered, failed > 0, results.Count > 0 && failed == results.Count, now);
    }

    // Same title (letters and digits only) and same moment in the sequence: free now vs coming later.
    private static string Key(FreeGame game)
    {
        var text = new StringBuilder();
        foreach (var c in game.Title.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                text.Append(char.ToLowerInvariant(c));
            }
        }

        return (game.IsUpcoming ? "later:" : "now:") + text;
    }

    private static async Task<FreeGamesResult> AskAsync(IFreeGamesSource source, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SourceTimeout);
        try
        {
            return await source.GetAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return FreeGamesResult.Failed; // a service that is down or changed its format never breaks the page
        }
    }
}
