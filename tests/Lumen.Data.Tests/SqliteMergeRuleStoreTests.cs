// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteMergeRuleStoreTests : IAsyncLifetime
{
    private static readonly ItemRef _steam = new("steam", "1");
    private static readonly ItemRef _epic = new("epic", "x");
    private static readonly ItemRef _gog = new("gog", "9");

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
        var rules = await new SqliteMergeRuleStore(_db.Factory).LoadAsync(CancellationToken.None);

        rules.Separated.ShouldBeEmpty();
        rules.Preferred.ShouldBeEmpty();
    }

    [Fact]
    public async Task Separate_IsStored()
    {
        var store = new SqliteMergeRuleStore(_db.Factory);

        await store.SeparateAsync([_steam, _epic], CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None)).Separated.ShouldBe([_steam, _epic], ignoreOrder: true);
    }

    [Fact]
    public async Task Preferred_IsExclusiveWithinTheItem()
    {
        var store = new SqliteMergeRuleStore(_db.Factory);

        await store.SetPreferredAsync(_steam, [_steam, _epic, _gog], CancellationToken.None);
        await store.SetPreferredAsync(_gog, [_steam, _epic, _gog], CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None)).Preferred.ShouldBe([_gog]);
    }

    [Fact]
    public async Task Separating_ClearsPreference()
    {
        var store = new SqliteMergeRuleStore(_db.Factory);
        await store.SetPreferredAsync(_steam, [_steam, _epic], CancellationToken.None);

        await store.SeparateAsync([_steam, _epic], CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None)).Preferred.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_ForgetsEverything()
    {
        var store = new SqliteMergeRuleStore(_db.Factory);
        await store.SeparateAsync([_steam], CancellationToken.None);
        await store.SetPreferredAsync(_gog, [_gog, _epic], CancellationToken.None);

        await store.ResetAsync(CancellationToken.None);

        var rules = await store.LoadAsync(CancellationToken.None);
        rules.Separated.ShouldBeEmpty();
        rules.Preferred.ShouldBeEmpty();
    }

    [Fact]
    public async Task Link_IsStored_AndSkipsTheTargetItself()
    {
        var store = new SqliteMergeRuleStore(_db.Factory);

        await store.LinkAsync([_epic, _gog, _steam], _steam, CancellationToken.None);

        var links = (await store.LoadAsync(CancellationToken.None)).Links;
        links.Count.ShouldBe(2);
        links[_epic].ShouldBe(_steam);
        links[_gog].ShouldBe(_steam);
    }

    [Fact]
    public async Task Separating_UndoesLinksInBothDirections()
    {
        var store = new SqliteMergeRuleStore(_db.Factory);
        await store.LinkAsync([_epic], _steam, CancellationToken.None);
        await store.LinkAsync([_gog], _epic, CancellationToken.None);

        await store.SeparateAsync([_epic], CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None)).Links.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_ForgetsLinks()
    {
        var store = new SqliteMergeRuleStore(_db.Factory);
        await store.LinkAsync([_epic], _steam, CancellationToken.None);

        await store.ResetAsync(CancellationToken.None);

        (await store.LoadAsync(CancellationToken.None)).Links.ShouldBeEmpty();
    }
}
