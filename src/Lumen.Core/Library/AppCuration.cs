// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>What the user decided about a detected app.</summary>
public enum AppChoice
{
    /// <summary>In the library.</summary>
    Added,

    /// <summary>Never suggested again (until the user un-ignores it).</summary>
    Ignored,
}

/// <summary>
/// Stores <see cref="AppChoice"/>s per app (SQLite <c>app_choices</c>, machine-local until the sync folder (M8) moves
/// ignored apps to <c>config.json</c> → <c>ignoredApps</c>).
/// </summary>
public interface IAppChoiceStore
{
    Task<IReadOnlyDictionary<ItemRef, AppChoice>> LoadAsync(CancellationToken ct);

    /// <summary>Sets the choice for each app; null forgets it (the app becomes a suggestion again).</summary>
    Task SetAsync(IEnumerable<ItemRef> refs, AppChoice? choice, CancellationToken ct);
}

/// <summary>
/// PLAN-v1 §4.2: apps detected on the PC (Start menu, Store apps) are only suggestions; they reach the library when
/// the user adds them in "Discover apps". Pure.
/// </summary>
public static class AppCuration
{
    /// <summary>Sources whose entries need the user's OK before showing in the library.</summary>
    public static IReadOnlySet<string> CandidateSources { get; } = new HashSet<string>(StringComparer.Ordinal) { "startmenu", "appsfolder" };

    public static bool IsCandidateSource(string source) => CandidateSources.Contains(source);

    /// <summary>Everything from stores and manual items shows; detected apps only once added.</summary>
    public static bool IsShown(LibraryEntry entry, IReadOnlyDictionary<ItemRef, AppChoice> choices) =>
        !IsCandidateSource(entry.Ref.Source) || (choices.TryGetValue(entry.Ref, out var choice) && choice == AppChoice.Added);

    /// <summary>
    /// Detected apps with the given status: suggestions (<paramref name="choice"/> null: neither added nor ignored),
    /// or the ignored ones. Shortcuts to games Lumen already shows through their store are never suggested: same
    /// normalized title, or a target inside the game's install folder.
    /// </summary>
    public static IReadOnlyList<LibraryEntry> Detected(
        IReadOnlyList<LibraryEntry> entries,
        IReadOnlyDictionary<ItemRef, AppChoice> choices,
        TitleNormalizer normalizer,
        AppChoice? choice = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(normalizer);

        var games = entries.Where(e => e.Kind == ItemKind.Game && !IsCandidateSource(e.Ref.Source)).ToList();
        var gameTitles = games.Select(g => normalizer.Normalize(g.Title)).Where(t => t.Length > 0).ToHashSet(StringComparer.Ordinal);
        var gameFolders = games
            .Select(g => g.InstallDirectory)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d!.TrimEnd('\\', '/') + "\\")
            .ToList();

        return entries
            .Where(e => IsCandidateSource(e.Ref.Source))
            .Where(e => (choices.TryGetValue(e.Ref, out var c) ? c : (AppChoice?)null) == choice)
            .Where(e => !gameTitles.Contains(normalizer.Normalize(e.Title)))
            .Where(e => !(e.Extra.TryGetValue("target", out var target) && gameFolders.Any(f => target.StartsWith(f, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
