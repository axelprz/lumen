// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;

namespace Lumen.Core.Library;

public enum KindFilter
{
    All,
    Games,
    Apps,
}

public enum InstallFilter
{
    All,
    Installed,
    NotInstalled,
}

public enum LibrarySort
{
    Name,

    /// <summary>Most recently played first; never-played last.</summary>
    LastPlayed,

    /// <summary>Most played first.</summary>
    Playtime,

    /// <summary>Newest in Lumen's library first.</summary>
    RecentlyAdded,
}

/// <summary>What the library page shows: free-text search plus filters and an order.</summary>
/// <param name="Store">A source id ("steam", "epic", …) or null for every store.</param>
/// <param name="Hidden">False: the library without hidden items. True: only the hidden ones (to find and unhide them).</param>
/// <param name="Collection">A collection id ("favorites", …) or null for everything.</param>
public sealed record LibraryQuery(
    string Search = "",
    KindFilter Kind = KindFilter.All,
    string? Store = null,
    InstallFilter Install = InstallFilter.All,
    LibrarySort Sort = LibrarySort.Name,
    bool Hidden = false,
    string? Collection = null)
{
    public static LibraryQuery Default { get; } = new();

    /// <summary>
    /// True when anything but the sort narrows the list (the UI shows "x of y" and "clear filters"). Switching to the
    /// hidden items is a different view, not a filter.
    /// </summary>
    public bool IsFiltering =>
        !string.IsNullOrWhiteSpace(Search) || Kind != KindFilter.All || Store is not null || Install != InstallFilter.All || Collection is not null;
}

/// <summary>Applies a <see cref="LibraryQuery"/> to items. Pure and fast enough to run on every keystroke.</summary>
public sealed class LibraryFilter(TitleNormalizer normalizer)
{
    private static readonly CompareInfo _compare = CultureInfo.InvariantCulture.CompareInfo;

    /// <param name="firstSeen">When each entry first appeared in Lumen (for "recently added"); missing = unknown, sorted last.</param>
    /// <param name="collections">The user's collections, for <see cref="LibraryQuery.Collection"/>; an unknown id matches nothing.</param>
    public IReadOnlyList<LibraryItem> Apply(
        IReadOnlyList<LibraryItem> items,
        LibraryQuery query,
        IReadOnlyDictionary<ItemRef, DateTimeOffset> firstSeen,
        IReadOnlyList<Collection>? collections = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(firstSeen);

        var search = query.Search.Trim();
        var normalizedSearch = search.Length == 0 ? string.Empty : normalizer.Normalize(search);
        var collection = query.Collection is null ? null : collections?.FirstOrDefault(c => c.Id == query.Collection);
        var inCollection = collection is null ? null : collection.Items.ToHashSet();

        var filtered = items.Where(item =>
            item.IsHidden == query.Hidden
            && MatchesKind(item, query.Kind)
            && (query.Store is null || item.Entries.Any(e => e.Ref.Source == query.Store))
            && MatchesInstall(item, query.Install)
            && (query.Collection is null || (inCollection is not null && item.Entries.Any(e => inCollection.Contains(e.Ref))))
            && (search.Length == 0 || MatchesSearch(item.Title, search, normalizedSearch)));

        var byName = StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true);
        return query.Sort switch
        {
            LibrarySort.LastPlayed => filtered
                .OrderBy(i => LastPlayed(i) is null)
                .ThenByDescending(LastPlayed)
                .ThenBy(i => i.Title, byName)
                .ToList(),
            LibrarySort.Playtime => filtered
                .OrderByDescending(PlaytimeMinutes)
                .ThenBy(i => i.Title, byName)
                .ToList(),
            LibrarySort.RecentlyAdded => filtered
                .Select(i => (Item: i, Added: Added(i, firstSeen)))
                .OrderBy(x => x.Added is null)
                .ThenByDescending(x => x.Added)
                .ThenBy(x => x.Item.Title, byName)
                .Select(x => x.Item)
                .ToList(),
            _ => filtered.OrderBy(i => i.Title, byName).ToList(),
        };
    }

    /// <summary>Most recent "last played" across the item's stores (unix seconds in <c>Extra["lastPlayed"]</c>).</summary>
    public static DateTimeOffset? LastPlayed(LibraryItem item) =>
        item.Entries
            .Select(e => e.Extra.TryGetValue("lastPlayed", out var raw) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : (DateTimeOffset?)null)
            .Max();

    /// <summary>Playtime known from stores (<c>Extra["playtimeMinutes"]</c>, summed); Lumen's own tracking arrives in M6.</summary>
    public static long PlaytimeMinutes(LibraryItem item) =>
        item.Entries.Sum(e => e.Extra.TryGetValue("playtimeMinutes", out var raw) && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ? minutes : 0);

    private static DateTimeOffset? Added(LibraryItem item, IReadOnlyDictionary<ItemRef, DateTimeOffset> firstSeen) =>
        item.Entries.Select(e => firstSeen.TryGetValue(e.Ref, out var when) ? when : (DateTimeOffset?)null).Min();

    private static bool MatchesKind(LibraryItem item, KindFilter kind) => kind switch
    {
        KindFilter.Games => item.Kind == ItemKind.Game,
        KindFilter.Apps => item.Kind == ItemKind.App,
        _ => true,
    };

    private static bool MatchesInstall(LibraryItem item, InstallFilter install) => install switch
    {
        InstallFilter.Installed => item.IsInstalled,
        InstallFilter.NotInstalled => !item.IsInstalled,
        _ => true,
    };

    // "witcher" finds "The Witcher® 3"; "pokemon" finds "Pokémon"; "witcher iii" finds "The Witcher 3".
    private bool MatchesSearch(string title, string search, string normalizedSearch) =>
        _compare.IndexOf(title, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0
        || (normalizedSearch.Length > 0 && normalizer.Normalize(title).Contains(normalizedSearch, StringComparison.Ordinal));
}
