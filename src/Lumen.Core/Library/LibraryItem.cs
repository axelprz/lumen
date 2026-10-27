// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>
/// What the UI shows: one game or app, made of one or more <see cref="LibraryEntry"/> (one per store when
/// the same game is owned on several). <see cref="Primary"/> is what Play/Install uses.
/// </summary>
public sealed record LibraryItem(IReadOnlyList<LibraryEntry> Entries, LibraryEntry Primary)
{
    /// <summary>Stable within a session: the primary entry's reference.</summary>
    public ItemRef Key => Primary.Ref;

    /// <summary>The user's name for this item, or null to use the store's.</summary>
    public string? CustomTitle { get; init; }

    /// <summary>The user hid this item: it only shows with the "Hidden" filter.</summary>
    public bool IsHidden { get; init; }

    public string Title => CustomTitle ?? Primary.Title;

    /// <summary>The name the store gives it (what "Restore original name" goes back to).</summary>
    public string OriginalTitle => Primary.Title;

    public ItemKind Kind => Primary.Kind;

    public bool IsInstalled => Entries.Any(e => e.IsInstalled);

    public bool IsMerged => Entries.Count > 1;
}

/// <summary>The user's decisions about merging. They always win over automatic merging.</summary>
/// <param name="Separated">Entries the user split off: never auto-merged with anything.</param>
/// <param name="Preferred">Entries the user chose as the store to use for their item.</param>
/// <param name="Links">Manual merges: each entry joins the item that contains the entry it points to.</param>
public sealed record MergeRules(IReadOnlySet<ItemRef> Separated, IReadOnlySet<ItemRef> Preferred, IReadOnlyDictionary<ItemRef, ItemRef> Links)
{
    public MergeRules(IReadOnlySet<ItemRef> separated, IReadOnlySet<ItemRef> preferred)
        : this(separated, preferred, new Dictionary<ItemRef, ItemRef>())
    {
    }

    public static MergeRules None { get; } = new(new HashSet<ItemRef>(), new HashSet<ItemRef>());
}

/// <summary>Stores <see cref="MergeRules"/> (SQLite <c>merge_rules</c>, machine-local).</summary>
public interface IMergeRuleStore
{
    Task<MergeRules> LoadAsync(CancellationToken ct);

    /// <summary>Marks each entry as separate (splits a merged item) and drops manual merges involving them.</summary>
    Task SeparateAsync(IEnumerable<ItemRef> refs, CancellationToken ct);

    /// <summary>Manual merge: every entry in <paramref name="refs"/> joins the item that contains <paramref name="target"/>.</summary>
    Task LinkAsync(IEnumerable<ItemRef> refs, ItemRef target, CancellationToken ct);

    /// <summary>Makes <paramref name="preferred"/> the store used for its item; clears the preference of the others.</summary>
    Task SetPreferredAsync(ItemRef preferred, IEnumerable<ItemRef> others, CancellationToken ct);

    /// <summary>Forgets every merge decision (back to automatic merging).</summary>
    Task ResetAsync(CancellationToken ct);
}

/// <summary>The user's per-entry choices: a custom title and whether it is hidden.</summary>
public sealed record ItemOverride(string? Title, bool Hidden)
{
    /// <summary>Longest custom title accepted (matches <c>config.schema.json</c>).</summary>
    public const int MaxTitleLength = 120;

    /// <summary>Trims the user's input; null when empty (empty means "use the store's name").</summary>
    public static string? CleanTitle(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..MaxTitleLength].TrimEnd();
    }
}

/// <summary>Stores <see cref="ItemOverride"/>s (SQLite <c>item_overrides</c>, machine-local until sync arrives in M8).</summary>
public interface IItemOverrideStore
{
    Task<IReadOnlyDictionary<ItemRef, ItemOverride>> LoadAsync(CancellationToken ct);

    /// <summary>Sets (or with null, clears) the custom title of every entry of an item.</summary>
    Task SetTitleAsync(IEnumerable<ItemRef> refs, string? title, CancellationToken ct);

    Task SetHiddenAsync(IEnumerable<ItemRef> refs, bool hidden, CancellationToken ct);
}
