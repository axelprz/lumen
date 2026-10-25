// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Lumen.Data.Migrations;

/// <summary>
/// Brings the local database up to the latest schema. Each migration runs in its own transaction
/// and is recorded in <c>schema_version</c>. A database created by a newer Lumen is refused.
/// </summary>
public sealed partial class MigrationRunner
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IReadOnlyList<Migration> _migrations;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(
        ISqliteConnectionFactory connectionFactory,
        IReadOnlyList<Migration> migrations,
        ILogger<MigrationRunner> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int LatestVersion => _migrations.Count == 0 ? 0 : _migrations[^1].Version;

    /// <returns>The schema version after migrating.</returns>
    public async Task<int> MigrateAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                version    INTEGER NOT NULL PRIMARY KEY,
                name       TEXT    NOT NULL,
                applied_at TEXT    NOT NULL
            ) STRICT;
            """,
            cancellationToken: ct)).ConfigureAwait(false);

        var current = await GetCurrentVersionAsync(connection, ct).ConfigureAwait(false);
        if (current > LatestVersion)
        {
            throw new DatabaseVersionException(current, LatestVersion);
        }

        foreach (var migration in _migrations.Where(m => m.Version > current))
        {
            ct.ThrowIfCancellationRequested();
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(migration.Sql, transaction: transaction, cancellationToken: ct))
                .ConfigureAwait(false);
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_version (version, name, applied_at) VALUES (@Version, @Name, @AppliedAt);",
                new
                {
                    migration.Version,
                    migration.Name,
                    AppliedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                },
                transaction,
                cancellationToken: ct)).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            LogApplied(migration.Version, migration.Name);
            current = migration.Version;
        }

        return current;
    }

    public async Task<int> GetCurrentVersionAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.OpenAsync(ct).ConfigureAwait(false);
        return await GetCurrentVersionAsync(connection, ct).ConfigureAwait(false);
    }

    private static async Task<int> GetCurrentVersionAsync(System.Data.Common.DbConnection connection, CancellationToken ct)
    {
        var exists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';",
            cancellationToken: ct)).ConfigureAwait(false);
        if (exists == 0)
        {
            return 0;
        }

        var version = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT MAX(version) FROM schema_version;",
            cancellationToken: ct)).ConfigureAwait(false);
        return (int)(version ?? 0);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied database migration {Version} ({Name})")]
    private partial void LogApplied(int version, string name);
}
