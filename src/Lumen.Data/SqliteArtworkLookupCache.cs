// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Artwork;

namespace Lumen.Data;

public sealed class SqliteArtworkLookupCache(ISqliteConnectionFactory connectionFactory, TimeProvider? time = null) : IArtworkLookupCache
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<ArtworkLookup?> GetAsync(string key, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT value AS Value, updated_at AS UpdatedAt FROM artwork_lookups WHERE key = @key;", new { key }, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(row.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated)
            ? new ArtworkLookup(row.Value, updated)
            : null;
    }

    private sealed class Row
    {
        public long? Value { get; init; }

        public string UpdatedAt { get; init; } = string.Empty;
    }

    public async Task SetAsync(string key, long? id, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO artwork_lookups (key, value, updated_at) VALUES (@key, @id, @now)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
            """,
            new { key, id, now = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM artwork_lookups;", cancellationToken: ct)).ConfigureAwait(false);
    }
}
