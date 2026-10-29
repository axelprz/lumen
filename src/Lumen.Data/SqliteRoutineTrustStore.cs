// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Lumen.Core.Routines;

namespace Lumen.Data;

public sealed class SqliteRoutineTrustStore(ISqliteConnectionFactory connectionFactory, TimeProvider? time = null) : IRoutineTrustStore
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<(string Id, string Sha256)>(new CommandDefinition(
            "SELECT id, sha256 FROM routine_trust;", cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToDictionary(r => r.Id, r => r.Sha256, StringComparer.Ordinal);
    }

    public async Task ApproveAsync(string routineId, string hash, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routineId);
        if (hash is not { Length: 64 } || !hash.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("Expected a lowercase SHA-256 hex string.", nameof(hash));
        }

        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO routine_trust (id, sha256, approved_at) VALUES (@Id, @Hash, @At) " +
            "ON CONFLICT(id) DO UPDATE SET sha256 = excluded.sha256, approved_at = excluded.approved_at;",
            new { Id = routineId, Hash = hash, At = _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task ForgetAsync(string routineId, CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM routine_trust WHERE id = @Id;", new { Id = routineId }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
