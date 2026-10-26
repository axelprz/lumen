// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>Local cache of provider output (SQLite <c>entries</c> table) so the library shows instantly on start.</summary>
public interface ILibraryCache
{
    Task<IReadOnlyList<LibraryEntry>> LoadAllAsync(CancellationToken ct);

    /// <summary>Atomically replaces every cached entry of <paramref name="source"/>; records when new entries first appeared.</summary>
    Task ReplaceSourceAsync(string source, IReadOnlyList<LibraryEntry> entries, CancellationToken ct);

    /// <summary>When each entry first appeared in Lumen (survives rescans and uninstalls).</summary>
    Task<IReadOnlyDictionary<ItemRef, DateTimeOffset>> LoadFirstSeenAsync(CancellationToken ct);
}
