// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqlitePlaytimeStoreTests : IAsyncLifetime
{
    private static readonly ItemRef _game = new("epic", "Sample");
    private static readonly DateTimeOffset _t0 = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);

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
    public async Task Sessions_AddUp_ShortOnesAreDropped()
    {
        var store = new SqlitePlaytimeStore(_db.Factory);

        var first = await store.StartAsync(_game, _t0, CancellationToken.None);
        await store.ExtendAsync(first, _t0.AddMinutes(30), CancellationToken.None);
        await store.FinishAsync(first, _t0.AddMinutes(45), TimeSpan.FromMinutes(1), CancellationToken.None);
        var crash = await store.StartAsync(_game, _t0.AddHours(2), CancellationToken.None);
        await store.FinishAsync(crash, _t0.AddHours(2).AddSeconds(20), TimeSpan.FromMinutes(1), CancellationToken.None);

        var total = (await store.LoadTotalsAsync(CancellationToken.None))[_game];
        total.ShouldBe(new PlaytimeTotal(45, _t0.AddMinutes(45)));
    }

    [Fact]
    public async Task SessionsLeftOpen_AreClosedAtTheirLastSavedTime()
    {
        var store = new SqlitePlaytimeStore(_db.Factory);
        var open = await store.StartAsync(_game, _t0, CancellationToken.None);
        await store.ExtendAsync(open, _t0.AddMinutes(20), CancellationToken.None); // then Lumen was closed

        await store.CloseLeftoversAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
        await store.ExtendAsync(open, _t0.AddHours(5), CancellationToken.None); // closed sessions can't be extended

        (await store.LoadTotalsAsync(CancellationToken.None))[_game].ShouldBe(new PlaytimeTotal(20, _t0.AddMinutes(20)));
    }
}
