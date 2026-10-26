// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Artwork;

/// <summary>A remembered lookup: <see cref="Id"/> null means "searched, not found".</summary>
public sealed record ArtworkLookup(long? Id, DateTimeOffset UpdatedAt);

/// <summary>
/// Remembers external ids found while looking for artwork (e.g. the SteamGridDB game id for "steam:570") so the
/// search runs once per item, not on every start. Machine-local (SQLite <c>artwork_lookups</c>).
/// </summary>
public interface IArtworkLookupCache
{
    Task<ArtworkLookup?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, long? id, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}
