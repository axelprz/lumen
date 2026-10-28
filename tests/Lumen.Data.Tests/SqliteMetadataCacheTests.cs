// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteMetadataCacheTests : IAsyncLifetime
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
    public async Task Values_AreStored_Replaced_AndReadInBulk()
    {
        var cache = new SqliteMetadataCache(_db.Factory);

        await cache.SetManyAsync(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, CancellationToken.None);
        await cache.SetManyAsync(new Dictionary<string, string> { ["b"] = "3" }, CancellationToken.None);

        (await cache.GetManyAsync(["a", "b", "missing"], CancellationToken.None))
            .ShouldBe(new Dictionary<string, string> { ["a"] = "1", ["b"] = "3" }, ignoreOrder: true);
        (await cache.GetManyAsync(Enumerable.Range(0, 1200).Select(i => "k" + i), CancellationToken.None)).ShouldBeEmpty();
    }
}
