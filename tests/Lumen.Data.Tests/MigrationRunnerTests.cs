// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Dapper;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class MigrationRunnerTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FreshDatabase_IsMigratedToLatest()
    {
        var runner = CreateRunner(MigrationLoader.LoadEmbedded());

        var version = await runner.MigrateAsync(CancellationToken.None);

        version.ShouldBe(runner.LatestVersion);
        version.ShouldBeGreaterThan(0);
        File.Exists(_db.Path).ShouldBeTrue();
        (await TableExistsAsync("settings_local")).ShouldBeTrue();
        (await runner.GetCurrentVersionAsync(CancellationToken.None)).ShouldBe(version);
    }

    [Fact]
    public async Task RunningTwice_IsANoOp()
    {
        var runner = CreateRunner(MigrationLoader.LoadEmbedded());

        await runner.MigrateAsync(CancellationToken.None);
        var second = await runner.MigrateAsync(CancellationToken.None);

        second.ShouldBe(runner.LatestVersion);
        await using var connection = await _db.Factory.OpenAsync(CancellationToken.None);
        var rows = await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM schema_version;");
        rows.ShouldBe(runner.LatestVersion);
    }

    [Fact]
    public async Task OnlyPendingMigrations_AreApplied()
    {
        var v1 = new Migration(1, "one", "CREATE TABLE t1 (id INTEGER PRIMARY KEY);");
        var v2 = new Migration(2, "two", "CREATE TABLE t2 (id INTEGER PRIMARY KEY);");

        (await CreateRunner([v1]).MigrateAsync(CancellationToken.None)).ShouldBe(1);
        (await CreateRunner([v1, v2]).MigrateAsync(CancellationToken.None)).ShouldBe(2);

        (await TableExistsAsync("t1")).ShouldBeTrue();
        (await TableExistsAsync("t2")).ShouldBeTrue();
    }

    [Fact]
    public async Task FailingMigration_IsRolledBack()
    {
        var good = new Migration(1, "good", "CREATE TABLE ok (id INTEGER PRIMARY KEY);");
        var bad = new Migration(2, "bad", "CREATE TABLE half (id INTEGER PRIMARY KEY); THIS IS NOT SQL;");
        var runner = CreateRunner([good, bad]);

        await Should.ThrowAsync<Microsoft.Data.Sqlite.SqliteException>(() => runner.MigrateAsync(CancellationToken.None));

        (await runner.GetCurrentVersionAsync(CancellationToken.None)).ShouldBe(1);
        (await TableExistsAsync("half")).ShouldBeFalse();
    }

    [Fact]
    public async Task NewerDatabase_IsRefused()
    {
        var v1 = new Migration(1, "one", "SELECT 1;");
        var v2 = new Migration(2, "two", "SELECT 1;");
        await CreateRunner([v1, v2]).MigrateAsync(CancellationToken.None);

        var ex = await Should.ThrowAsync<DatabaseVersionException>(
            () => CreateRunner([v1]).MigrateAsync(CancellationToken.None));

        ex.DatabaseVersion.ShouldBe(2);
        ex.SupportedVersion.ShouldBe(1);
    }

    private MigrationRunner CreateRunner(IReadOnlyList<Migration> migrations) =>
        new(_db.Factory, migrations, NullLogger<MigrationRunner>.Instance);

    private async Task<bool> TableExistsAsync(string table)
    {
        await using var connection = await _db.Factory.OpenAsync(CancellationToken.None);
        var count = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table;",
            new { table });
        return count == 1;
    }
}
