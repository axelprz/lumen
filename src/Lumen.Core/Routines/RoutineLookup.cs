// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;

namespace Lumen.Core.Routines;

/// <summary>
/// Which routines the tray menu, the quick search and Home offer (PLAN-v1 §4.5). A routine file with errors is never
/// offered; one that needs review is, because choosing it opens the preview instead of running.
/// </summary>
public static class RoutineLookup
{
    /// <summary>Most routines in the tray submenu.</summary>
    public const int MaxInMenu = 12;

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    /// <summary>Valid routines (ready or needing review), by name.</summary>
    public static IReadOnlyList<RoutineEntry> Usable(IEnumerable<RoutineEntry> entries, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(culture);
        return entries
            .Where(e => e.Routine is not null && e.State != RoutineTrustState.Invalid)
            .OrderBy(e => NameOf(e, culture), StringComparer.Create(culture, ignoreCase: true))
            .ToList();
    }

    /// <summary>Usable routines marked "pinned", for Home.</summary>
    public static IReadOnlyList<RoutineEntry> Pinned(IEnumerable<RoutineEntry> entries, CultureInfo culture) =>
        Usable(entries, culture).Where(e => e.Routine!.Pinned).ToList();

    /// <summary>Usable routines whose name contains the text (names starting with it first). Empty text finds nothing.</summary>
    public static IReadOnlyList<RoutineEntry> Find(IEnumerable<RoutineEntry> entries, string? text, CultureInfo culture, int max = 3)
    {
        var query = text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            return [];
        }

        return Usable(entries, culture)
            .Select(e => (Entry: e, Name: NameOf(e, culture)))
            .Select(x => (x.Entry, At: Compare.IndexOf(x.Name, query, Loose)))
            .Where(x => x.At >= 0)
            .OrderBy(x => x.At == 0 ? 0 : 1)
            .Take(max)
            .Select(x => x.Entry)
            .ToList();
    }

    /// <summary>The routine's name in the given language (the file id when the file is broken).</summary>
    public static string NameOf(RoutineEntry entry, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Routine?.Name.For(culture) ?? entry.FileId;
    }
}
