// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lumen.Providers.Apps;

/// <summary>
/// Tells real apps from Start menu clutter (uninstallers, readmes, help, websites, runtimes, store game shortcuts)
/// using <c>Data/app-junk.json</c>. Whatever slips through, the user can still ignore in "Discover apps".
/// </summary>
public sealed class AppJunkFilter
{
    private const string ResourceName = "Lumen.Providers.Data.app-junk.json";
    private static readonly Lazy<AppJunkFilter> _default = new(Load);

    private readonly IReadOnlyList<string[]> _nameWords;
    private readonly IReadOnlySet<string> _targetFileNames;
    private readonly IReadOnlySet<string> _targetExtensions;
    private readonly IReadOnlySet<string> _skippedUrlSchemes;
    private readonly IReadOnlySet<string> _skippedFolders;

    public AppJunkFilter(
        IEnumerable<string> nameWords,
        IEnumerable<string> targetFileNames,
        IEnumerable<string> targetExtensions,
        IEnumerable<string> skippedUrlSchemes,
        IEnumerable<string> skippedFolders)
    {
        _nameWords = nameWords.Select(Words).Where(w => w.Length > 0).ToList();
        _targetFileNames = targetFileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _targetExtensions = targetExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _skippedUrlSchemes = skippedUrlSchemes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _skippedFolders = skippedFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The filter built from the embedded data file.</summary>
    public static AppJunkFilter Default => _default.Value;

    /// <summary>A shortcut or app name like "Uninstall 7-Zip" or "Read Me".</summary>
    public bool IsJunkName(string name)
    {
        var words = Words(name);
        return _nameWords.Any(phrase => ContainsPhrase(words, phrase));
    }

    /// <summary>A shortcut target that is an uninstaller, a setup program or a document.</summary>
    public bool IsJunkTarget(string targetPath)
    {
        var fileName = Path.GetFileName(targetPath.Replace('/', '\\').Split('\\')[^1]);
        return _targetFileNames.Contains(fileName) || _targetExtensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>Websites and store game links (Lumen already shows those games through their store).</summary>
    public bool IsSkippedUrl(string url)
    {
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        return colon <= 0 || _skippedUrlSchemes.Contains(url[..colon]);
    }

    /// <summary>A Start menu folder that holds no launchable apps for Lumen (autostart copies, admin tools).</summary>
    public bool IsSkippedFolder(string folderName) => _skippedFolders.Contains(folderName);

    private static bool ContainsPhrase(string[] words, string[] phrase)
    {
        for (var i = 0; i + phrase.Length <= words.Length; i++)
        {
            if (words.AsSpan(i, phrase.Length).SequenceEqual(phrase))
            {
                return true;
            }
        }

        return false;
    }

    // Lowercase, accents removed, split on anything that isn't a letter, digit or apostrophe.
    private static string[] Words(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) || c == '\'' ? char.ToLowerInvariant(c) : ' ');
        }

        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static AppJunkFilter Load()
    {
        using var stream = typeof(AppJunkFilter).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        IEnumerable<string> List(string name) => root.GetProperty(name).EnumerateArray().Select(e => e.GetString()!).ToList();
        return new AppJunkFilter(List("nameWords"), List("targetFileNames"), List("targetExtensions"), List("skippedUrlSchemes"), List("skippedFolders"));
    }
}
