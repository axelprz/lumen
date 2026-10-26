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

    public string Title => Primary.Title;

    public ItemKind Kind => Primary.Kind;

    public bool IsInstalled => Entries.Any(e => e.IsInstalled);

    public bool IsMerged => Entries.Count > 1;
}

/// <summary>The user's decisions about merging. They always win over automatic merging.</summary>
/// <param name="Separated">Entries the user split off: never auto-merged with anything.</param>
/// <param name="Preferred">Entries the user chose as the store to use for their item.</param>
public sealed record MergeRules(IReadOnlySet<ItemRef> Separated, IReadOnlySet<ItemRef> Preferred)
{
    public static MergeRules None { get; } = new(new HashSet<ItemRef>(), new HashSet<ItemRef>());
}

/// <summary>Stores <see cref="MergeRules"/> (SQLite <c>merge_rules</c>, machine-local).</summary>
public interface IMergeRuleStore
{
    Task<MergeRules> LoadAsync(CancellationToken ct);

    /// <summary>Marks each entry as separate (splits a merged item).</summary>
    Task SeparateAsync(IEnumerable<ItemRef> refs, CancellationToken ct);

    /// <summary>Makes <paramref name="preferred"/> the store used for its item; clears the preference of the others.</summary>
    Task SetPreferredAsync(ItemRef preferred, IEnumerable<ItemRef> others, CancellationToken ct);

    /// <summary>Forgets every merge decision (back to automatic merging).</summary>
    Task ResetAsync(CancellationToken ct);
}
