// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteManualItemStoreTests : IAsyncLifetime
{
    private readonly TempDatabase _db = new();

    public async Task InitializeAsync() =>
        await new MigrationRunner(_db.Factory, MigrationLoader.LoadEmbedded(), NullLogger<MigrationRunner>.Instance)
            .MigrateAsync(CancellationToken.None);

    public Task DisposeAsync()
    {
        _db.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Save_Load_Update_Remove()
    {
        var store = new SqliteManualItemStore(_db.Factory);
        var game = new ManualItem(Guid.NewGuid(), ItemKind.Game, "Sample", @"%USERPROFILE%\Games\s.exe", null, "-x");
        var site = new ManualItem(Guid.NewGuid(), ItemKind.App, "Site", null, "https://example.com", null);

        await store.SaveAsync(game, CancellationToken.None);
        await store.SaveAsync(site, CancellationToken.None);
        (await store.GetAllAsync(CancellationToken.None)).ShouldBe([game, site]);

        var renamed = game with { Title = "Renamed", Arguments = null };
        await store.SaveAsync(renamed, CancellationToken.None);
        (await store.GetAllAsync(CancellationToken.None)).ShouldBe([renamed, site]);

        await store.RemoveAsync(game.Id, CancellationToken.None);
        (await store.GetAllAsync(CancellationToken.None)).ShouldBe([site]);
    }

    [Fact]
    public async Task PathAndUrlTogether_IsRejected()
    {
        var store = new SqliteManualItemStore(_db.Factory);

        await Should.ThrowAsync<ArgumentException>(() =>
            store.SaveAsync(new ManualItem(Guid.NewGuid(), ItemKind.Game, "X", @"C:\a.exe", "https://a", null), CancellationToken.None));
    }

    [Fact]
    public async Task Database_EnforcesExactlyOneTarget()
    {
        await using var connection = await _db.Factory.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO manual_items (id, kind, title, created_at, updated_at) VALUES ('x', 'Game', 'T', 'n', 'n');";

        await Should.ThrowAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }
}
