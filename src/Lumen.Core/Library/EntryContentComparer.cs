// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>
/// Compares entries by content, including <see cref="LibraryEntry.Extra"/> (record equality compares that
/// dictionary by reference, so two scans of the same game would always look "different").
/// </summary>
public sealed class EntryContentComparer : IEqualityComparer<LibraryEntry>
{
    public static EntryContentComparer Instance { get; } = new();

    public bool Equals(LibraryEntry? x, LibraryEntry? y)
    {
        if (ReferenceEquals(x, y))
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        return x.Ref == y.Ref
            && x.Kind == y.Kind
            && x.State == y.State
            && string.Equals(x.Title, y.Title, StringComparison.Ordinal)
            && string.Equals(x.InstallDirectory, y.InstallDirectory, StringComparison.Ordinal)
            && x.Launch == y.Launch
            && x.Install == y.Install
            && x.Extra.Count == y.Extra.Count
            && x.Extra.All(kv => y.Extra.TryGetValue(kv.Key, out var value) && string.Equals(value, kv.Value, StringComparison.Ordinal));
    }

    public int GetHashCode(LibraryEntry obj) => HashCode.Combine(obj.Ref, obj.State, obj.Title);

    /// <summary>Same entries in the same order.</summary>
    public static bool SameList(IReadOnlyList<LibraryEntry> a, IReadOnlyList<LibraryEntry> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => Instance.Equals(pair.First, pair.Second));
}
