// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>What the Home page shows: the featured game and its rows.</summary>
/// <param name="Favorites">Favorite games, in the order they were marked.</param>
/// <param name="QuickApps">Favorite apps; with none marked, the apps in the library (so the dock is useful from day one).</param>
public sealed record HomeFeed(
    LibraryItem? Featured,
    IReadOnlyList<LibraryItem> ContinuePlaying,
    IReadOnlyList<LibraryItem> Favorites,
    IReadOnlyList<LibraryItem> QuickApps)
{
    /// <summary>Most games in the "Continue playing" row.</summary>
    public const int MaxContinuePlaying = 12;

    /// <summary>Most games in the "Favorites" row.</summary>
    public const int MaxFavorites = 24;

    /// <summary>Most apps in the "Quick apps" dock.</summary>
    public const int MaxQuickApps = 12;

    public static HomeFeed Empty { get; } = new(null, [], [], []);

    /// <summary>
    /// "Continue playing" is every visible game with a known last-played date, most recent first. The featured game
    /// is the most recently played one; with no play history yet, the most recently added installed game (then any
    /// installed game) so Home is never empty while the library isn't. Hidden items never appear anywhere.
    /// </summary>
    /// <param name="favorites">The Favorites collection (null: no favorites yet).</param>
    public static HomeFeed Build(
        IReadOnlyList<LibraryItem> items,
        IReadOnlyDictionary<ItemRef, DateTimeOffset> firstSeen,
        Collection? favorites = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(firstSeen);

        var visible = items.Where(i => !i.IsHidden).ToList();
        var games = visible.Where(i => i.Kind == ItemKind.Game).ToList();
        var apps = visible.Where(i => i.Kind == ItemKind.App).ToList();

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

        var favoriteGames = InCollectionOrder(games, favorites).Take(MaxFavorites).ToList();
        var favoriteApps = InCollectionOrder(apps, favorites).ToList();
        var quickApps = (favoriteApps.Count > 0
                ? favoriteApps
                : apps.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).AsEnumerable())
            .Take(MaxQuickApps)
            .ToList();

        return new HomeFeed(featured, continuePlaying, favoriteGames, quickApps);
    }

    // Items of the collection, ordered by when each was added (its earliest ref in the collection).
    private static IEnumerable<LibraryItem> InCollectionOrder(IReadOnlyList<LibraryItem> items, Collection? collection)
    {
        if (collection is null || collection.Items.Count == 0)
        {
            return [];
        }

        var position = collection.Items.Select((r, i) => (r, i)).ToDictionary(x => x.r, x => x.i);
        return items
            .Select(i => (Item: i, At: i.Entries.Select(e => position.TryGetValue(e.Ref, out var at) ? at : int.MaxValue).Min()))
            .Where(x => x.At != int.MaxValue)
            .OrderBy(x => x.At)
            .Select(x => x.Item);
    }
}
