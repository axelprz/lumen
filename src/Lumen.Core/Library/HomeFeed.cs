// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>What the Home page shows: the featured game and the "Continue playing" row.</summary>
public sealed record HomeFeed(LibraryItem? Featured, IReadOnlyList<LibraryItem> ContinuePlaying)
{
    /// <summary>Most games in the "Continue playing" row.</summary>
    public const int MaxContinuePlaying = 12;

    public static HomeFeed Empty { get; } = new(null, []);

    /// <summary>
    /// "Continue playing" is every visible game with a known last-played date, most recent first. The featured game
    /// is the most recently played one; with no play history yet, the most recently added installed game (then any
    /// installed game) so Home is never empty while the library isn't. Hidden items and apps never appear.
    /// </summary>
    public static HomeFeed Build(IReadOnlyList<LibraryItem> items, IReadOnlyDictionary<ItemRef, DateTimeOffset> firstSeen)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(firstSeen);

        var games = items.Where(i => i.Kind == ItemKind.Game && !i.IsHidden).ToList();
        var continuePlaying = games
            .Select(i => (Item: i, Played: LibraryFilter.LastPlayed(i)))
            .Where(x => x.Played is not null)
            .OrderByDescending(x => x.Played)
            .ThenBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxContinuePlaying)
            .Select(x => x.Item)
            .ToList();

        var featured = continuePlaying.FirstOrDefault()
            ?? games
                .Where(i => i.IsInstalled)
                .OrderByDescending(i => i.Entries.Select(e => firstSeen.TryGetValue(e.Ref, out var when) ? when : DateTimeOffset.MinValue).Max())
                .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();

        return new HomeFeed(featured, continuePlaying);
    }
}
