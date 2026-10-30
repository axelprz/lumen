// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Sync;

/// <summary>
/// "Combine" for the first sync, when the folder and this PC both already have data (PLAN-v1 §4.5). Nothing is
/// dropped: lists are joined, and when both sides have the same thing the folder's version wins.
/// </summary>
public static class SyncMerge
{
    public static PortableConfig MergeConfig(PortableConfig folder, PortableConfig local)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(local);
        return new PortableConfig
        {
            Language = folder.Language ?? local.Language,
            MinimizeToTray = folder.MinimizeToTray ?? local.MinimizeToTray,
            Hotkey = folder.Hotkey ?? local.Hotkey,
            ManualItems = folder.ManualItems.Concat(local.ManualItems.Where(l => folder.ManualItems.All(f => f.Id != l.Id))).ToList(),
            Overrides = local.Overrides.Concat(folder.Overrides).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value),
            IgnoredApps = folder.IgnoredApps.Union(local.IgnoredApps).ToList(),
        };
    }

    /// <summary>The folder's name and order first, then what only this PC had.</summary>
    public static Collection MergeCollection(Collection folder, Collection local)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(local);
        return folder with { Items = folder.Items.Concat(local.Items.Where(i => !folder.Items.Contains(i))).ToList() };
    }

    public static IReadOnlyList<Collection> MergeCollections(IReadOnlyList<Collection> folder, IReadOnlyList<Collection> local)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(local);
        var merged = folder.Select(f => local.FirstOrDefault(l => l.Id == f.Id) is { } l ? MergeCollection(f, l) : f).ToList();
        merged.AddRange(local.Where(l => folder.All(f => f.Id != l.Id)));
        return merged;
    }
}
