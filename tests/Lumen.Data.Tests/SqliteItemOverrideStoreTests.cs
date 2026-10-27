// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Dapper;
using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteItemOverrideStoreTests : IAsyncLifetime
{
    private static readonly ItemRef _steam = new("steam", "1");
    private static readonly ItemRef _epic = new("epic", "x");

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
    public async Task Empty_ByDefault()
    {
        (await new SqliteItemOverrideStore(_db.Factory).LoadAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TitleAndHidden_AreStoredPerEntry_Independently()
    {
        var store = new SqliteItemOverrideStore(_db.Factory);

        await store.SetTitleAsync([_steam, _epic], "  My name ", CancellationToken.None);
        await store.SetHiddenAsync([_steam], true, CancellationToken.None);

        var all = await store.LoadAsync(CancellationToken.None);
        all[_steam].ShouldBe(new ItemOverride("My name", Hidden: true));
        all[_epic].ShouldBe(new ItemOverride("My name", Hidden: false));
    }

    [Fact]
    public async Task ClearingEverything_RemovesTheRow()
    {
        var store = new SqliteItemOverrideStore(_db.Factory);
        await store.SetTitleAsync([_steam], "Name", CancellationToken.None);
        await store.SetHiddenAsync([_steam], true, CancellationToken.None);

        await store.SetTitleAsync([_steam], "   ", CancellationToken.None);
        (await store.LoadAsync(CancellationToken.None))[_steam].ShouldBe(new ItemOverride(null, Hidden: true));

        await store.SetHiddenAsync([_steam], false, CancellationToken.None);
        await using var connection = await _db.Factory.OpenAsync(CancellationToken.None);
        (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM item_overrides;")).ShouldBe(0);
    }

    [Fact]
    public async Task LongTitles_AreCapped()
    {
        var store = new SqliteItemOverrideStore(_db.Factory);

        await store.SetTitleAsync([_steam], new string('a', 500), CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None))[_steam].Title!.Length.ShouldBe(ItemOverride.MaxTitleLength);
    }
}
