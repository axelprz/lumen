// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text;

namespace Lumen.Core.Library;

/// <summary>
/// Turns a store title into a comparison key so the same game from different stores can be merged:
/// lowercase, no ™®©, no accents, "&amp;" → "and", no punctuation, roman numerals II–XX → digits, edition
/// suffixes removed (list is data), whitespace collapsed. Only used for matching; never shown.
/// </summary>
public sealed class TitleNormalizer
{
    private static readonly Dictionary<string, string> _roman = new(StringComparer.Ordinal)
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9",
        ["x"] = "10", ["xi"] = "11", ["xii"] = "12", ["xiii"] = "13", ["xiv"] = "14", ["xv"] = "15", ["xvi"] = "16",
        ["xvii"] = "17", ["xviii"] = "18", ["xix"] = "19", ["xx"] = "20",
    };

    // Longest first, so "game of the year edition" is removed before "edition"-less variants.
    private readonly IReadOnlyList<string[]> _suffixes;

    /// <param name="editionSuffixes">Already lowercase phrases such as "goty edition" (from Data/title-cleanup.json).</param>
    public TitleNormalizer(IEnumerable<string>? editionSuffixes = null)
    {
        _suffixes = (editionSuffixes ?? [])
            .Select(s => Tokenize(s).ToArray())
            .Where(t => t.Length > 0)
            .OrderByDescending(t => t.Length)
            .ToList();
    }

    public string Normalize(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var tokens = Tokenize(title).Select(t => _roman.GetValueOrDefault(t, t)).ToList();

        // Strip edition suffixes repeatedly ("… Deluxe Edition GOTY"), but never the whole title.
        var removed = true;
        while (removed)
        {
            removed = false;
            foreach (var suffix in _suffixes)
            {
                if (tokens.Count > suffix.Length && tokens.Skip(tokens.Count - suffix.Length).SequenceEqual(suffix))
                {
                    tokens.RemoveRange(tokens.Count - suffix.Length, suffix.Length);
                    removed = true;
                    break;
                }
            }
        }

        return string.Join(' ', tokens);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var decomposed = text.Replace("&", " and ", StringComparison.Ordinal).Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue; // accents
            }

            // Letters and digits stay; everything else (™®©, punctuation, dashes, apostrophes) separates words,
            // except apostrophes inside words ("Assassin's") which are dropped without splitting.
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c is not ('\'' or '’'))
            {
                sb.Append(' ');
            }
        }

        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
