// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;

namespace Lumen.Core.Library;

/// <summary>
/// Results for the quick search box (PLAN-v1 §4.3): type, Enter launches the first one. Matches like the library's
/// search (accents, case, symbols and roman numerals don't matter), ranked so the likely pick comes first:
/// title starts with the text → a word starts with it → it appears anywhere. Ties go to installed items, then the most
/// recently played, then by name. Hidden items never show. With no text, the most recently played items.
/// </summary>
public sealed class QuickSearch(TitleNormalizer normalizer)
{
    /// <summary>Most results the box lists.</summary>
    public const int MaxResults = 8;

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public IReadOnlyList<LibraryItem> Find(IReadOnlyList<LibraryItem> items, string? text, int max = MaxResults)
    {
        ArgumentNullException.ThrowIfNull(items);
        var query = text?.Trim() ?? string.Empty;
        var visible = items.Where(i => !i.IsHidden);

        if (query.Length == 0)
        {
            return visible
                .Select(i => (Item: i, Last: LibraryFilter.LastPlayed(i)))
                .Where(x => x.Last is not null)
                .OrderByDescending(x => x.Last)
                .ThenBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Take(max)
                .Select(x => x.Item)
                .ToList();
        }

        var normalizedQuery = normalizer.Normalize(query);
        return visible
            .Select(i => (Item: i, Rank: Rank(i.Title, query, normalizedQuery)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.Item.IsInstalled)
            .ThenByDescending(x => LibraryFilter.LastPlayed(x.Item) ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Item.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(max)
            .Select(x => x.Item)
            .ToList();
    }

    // 0 = title starts with it, 1 = a word starts with it, 2 = anywhere, -1 = no match.
    private int Rank(string title, string query, string normalizedQuery)
    {
        var at = Compare.IndexOf(title, query, Loose);
        if (at == 0)
        {
            return 0;
        }

        if (at > 0)
        {
            return StartsAWord(title, query) ? 1 : 2;
        }

        if (normalizedQuery.Length == 0)
        {
            return -1;
        }

        var normalizedTitle = normalizer.Normalize(title);
        var n = normalizedTitle.IndexOf(normalizedQuery, StringComparison.Ordinal);
        return n switch
        {
            < 0 => -1,
            0 => 0,
            _ when normalizedTitle[n - 1] == ' ' => 1,
            _ => 2,
        };
    }

    private static bool StartsAWord(string title, string query)
    {
        for (var at = Compare.IndexOf(title, query, Loose); at >= 0; at = Compare.IndexOf(title, query, at + 1, Loose))
        {
            if (at == 0 || !char.IsLetterOrDigit(title[at - 1]))
            {
                return true;
            }

            if (at + 1 >= title.Length)
            {
                break;
            }
        }

        return false;
    }
}
