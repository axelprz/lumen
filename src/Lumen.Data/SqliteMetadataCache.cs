// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Library;

namespace Lumen.Data;

public sealed class SqliteMetadataCache(ISqliteConnectionFactory connectionFactory) : IMetadataCache
{
    public async Task<IReadOnlyDictionary<string, string>> GetManyAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        var list = keys.Distinct(StringComparer.Ordinal).ToList();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (list.Count == 0)
        {
            return result;
        }

        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);

        // SQLite limits bound parameters per statement: query in chunks.
        foreach (var chunk in list.Chunk(500))
        {
            var rows = await connection.QueryAsync<(string Key, string Value)>(new CommandDefinition(
                "SELECT key, value FROM metadata_cache WHERE key IN @Keys;", new { Keys = chunk }, cancellationToken: ct)).ConfigureAwait(false);
            foreach (var row in rows)
            {
                result[row.Key] = row.Value;
            }
        }

        return result;
    }

    public async Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        if (values.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var (key, value) in values)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO metadata_cache (key, value, updated_at) VALUES (@Key, @Value, @Now)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at;
                """,
                new { Key = key, Value = value, Now = now },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
