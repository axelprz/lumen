// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteArtworkLookupCacheTests : IAsyncLifetime
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
    public async Task Found_NotFound_Overwrite_Clear()
    {
        var cache = new SqliteArtworkLookupCache(_db.Factory);

        (await cache.GetAsync("sgdb:steam:1", CancellationToken.None)).ShouldBeNull();

        await cache.SetAsync("sgdb:steam:1", 900001, CancellationToken.None);
        await cache.SetAsync("sgdb:gog:2", null, CancellationToken.None);
        (await cache.GetAsync("sgdb:steam:1", CancellationToken.None))!.Id.ShouldBe(900001);
        var notFound = await cache.GetAsync("sgdb:gog:2", CancellationToken.None);
        notFound.ShouldNotBeNull();
        notFound.Id.ShouldBeNull();

        await cache.SetAsync("sgdb:gog:2", 42, CancellationToken.None);
        (await cache.GetAsync("sgdb:gog:2", CancellationToken.None))!.Id.ShouldBe(42);

        await cache.ClearAsync(CancellationToken.None);
        (await cache.GetAsync("sgdb:steam:1", CancellationToken.None)).ShouldBeNull();
    }
}
