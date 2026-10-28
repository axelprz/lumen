// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Dapper;
using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteCollectionStoreTests : IAsyncLifetime
{
    private static readonly ItemRef _a = new("steam", "1");
    private static readonly ItemRef _b = new("epic", "x");
    private static readonly ItemRef _c = new("startmenu", "Sample/Editor.lnk");

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
    public async Task Favorites_ExistsFromTheStart_AndCanNotBeDeleted()
    {
        var store = new SqliteCollectionStore(_db.Factory);

        await store.DeleteAsync(Collection.FavoritesId, CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None)).ShouldHaveSingleItem().Id.ShouldBe(Collection.FavoritesId);
    }

    [Fact]
    public async Task Items_KeepTheOrderTheyWereAdded_AndAddingTwiceIsHarmless()
    {
        var store = new SqliteCollectionStore(_db.Factory);
        await store.CreateAsync("work", "Work", CancellationToken.None);

        await store.AddAsync("work", [_b, _a], CancellationToken.None);
        await store.AddAsync("work", [_a, _c], CancellationToken.None);
        await store.RemoveAsync("work", [_b], CancellationToken.None);

        var work = (await store.LoadAsync(CancellationToken.None)).Single(c => c.Id == "work");
        work.Items.ShouldBe([_a, _c]);
    }

    [Fact]
    public async Task Rename_AndDelete_RemoveItsItemsToo()
    {
        var store = new SqliteCollectionStore(_db.Factory);
        await store.CreateAsync("zeta", "Zeta", CancellationToken.None);
        await store.CreateAsync("work", "Work", CancellationToken.None);
        await store.AddAsync("work", [_a], CancellationToken.None);

        await store.RenameAsync("work", "Alpha work", CancellationToken.None);
        (await store.LoadAsync(CancellationToken.None)).Select(c => c.Name).ShouldBe(["Favorites", "Alpha work", "Zeta"]);

        await store.DeleteAsync("work", CancellationToken.None);
        await using var connection = await _db.Factory.OpenAsync(CancellationToken.None);
        (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM collection_items;")).ShouldBe(0);
    }
}
