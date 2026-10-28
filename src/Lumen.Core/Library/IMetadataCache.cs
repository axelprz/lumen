// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>
/// Small, public metadata providers look up once and reuse (e.g. a store catalog's title for an item id), so a scan
/// doesn't repeat hundreds of requests. Never holds secrets. SQLite <c>metadata_cache</c>, machine-local.
/// </summary>
public interface IMetadataCache
{
    Task<IReadOnlyDictionary<string, string>> GetManyAsync(IEnumerable<string> keys, CancellationToken ct);

    Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct);
}
