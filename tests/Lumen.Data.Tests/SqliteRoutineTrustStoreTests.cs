// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteRoutineTrustStoreTests : IAsyncLifetime
{
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);

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
    public async Task Approve_Replace_Forget()
    {
        var store = new SqliteRoutineTrustStore(_db.Factory);

        await store.ApproveAsync("game-night", HashA, CancellationToken.None);
        (await store.LoadAsync(CancellationToken.None)).ShouldBe(new Dictionary<string, string> { ["game-night"] = HashA });

        await store.ApproveAsync("game-night", HashB, CancellationToken.None);
        (await store.LoadAsync(CancellationToken.None))["game-night"].ShouldBe(HashB);

        await store.ForgetAsync("game-night", CancellationToken.None);
        (await store.LoadAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task OnlyLowercaseSha256_IsStored(string hash) =>
        await Should.ThrowAsync<ArgumentException>(() => new SqliteRoutineTrustStore(_db.Factory).ApproveAsync("x", hash, CancellationToken.None));
}
