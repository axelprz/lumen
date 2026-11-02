// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Dapper;
using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteLaunchArgsTests : IAsyncLifetime
{
    private static readonly ItemRef _steam = new("steam", "570");

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
    public async Task Arguments_AreStoredCleaned_AndSurviveTitleChanges()
    {
        var store = new SqliteItemOverrideStore(_db.Factory);

        await store.SetArgsAsync([_steam], "  -windowed ", CancellationToken.None);
        await store.SetTitleAsync([_steam], "Dota", CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None))[_steam].ShouldBe(new ItemOverride("Dota", false, "-windowed"));
    }

    [Fact]
    public async Task ClearingTheArguments_RemovesTheRowWhenNothingElseIsSet()
    {
        var store = new SqliteItemOverrideStore(_db.Factory);
        await store.SetArgsAsync([_steam], "-windowed", CancellationToken.None);

        await store.SetArgsAsync([_steam], null, CancellationToken.None);

        await using var connection = await _db.Factory.OpenAsync(CancellationToken.None);
        (await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM item_overrides;")).ShouldBe(0);
    }

    [Fact]
    public async Task TextThatIsNotValid_IsNotStored()
    {
        var store = new SqliteItemOverrideStore(_db.Factory);

        await store.SetArgsAsync([_steam], "a\nb", CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None)).ShouldBeEmpty();
    }
}
