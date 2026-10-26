// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Dapper;
using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteLibraryCacheTests : IAsyncLifetime
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
    public async Task RoundTrips_EveryField()
    {
        var cache = new SqliteLibraryCache(_db.Factory);
        var entry = new LibraryEntry(
            new ItemRef("steam", "1000010"),
            ItemKind.Game,
            "Sample Quest™",
            InstallState.Installed,
            @"C:\Games\Sample Quest",
            new LaunchSpec(LaunchKind.Uri, "steam://rungameid/1000010"),
            new LaunchSpec(LaunchKind.ProcessWithArgs, @"C:\Launcher.exe", "--id 1", @"C:\"),
            new Dictionary<string, string> { ["appid"] = "1000010", ["lastPlayed"] = "1790001000" });

        await cache.ReplaceSourceAsync("steam", [entry], CancellationToken.None);
        var loaded = (await cache.LoadAllAsync(CancellationToken.None)).Single();

        loaded.Ref.ShouldBe(entry.Ref);
        loaded.Kind.ShouldBe(entry.Kind);
        loaded.Title.ShouldBe(entry.Title);
        loaded.State.ShouldBe(entry.State);
        loaded.InstallDirectory.ShouldBe(entry.InstallDirectory);
        loaded.Launch.ShouldBe(entry.Launch);
        loaded.Install.ShouldBe(entry.Install);
        loaded.Extra.ShouldBe(entry.Extra);
    }

    [Fact]
    public async Task NullSpecsAndDirectory_RoundTrip()
    {
        var cache = new SqliteLibraryCache(_db.Factory);
        var entry = new LibraryEntry(new ItemRef("steam", "2"), ItemKind.App, "Tool", InstallState.NotInstalled, null, null, null, LibraryEntry.NoExtra);

        await cache.ReplaceSourceAsync("steam", [entry], CancellationToken.None);
        var loaded = (await cache.LoadAllAsync(CancellationToken.None)).Single();

        loaded.InstallDirectory.ShouldBeNull();
        loaded.Launch.ShouldBeNull();
        loaded.Install.ShouldBeNull();
        loaded.Extra.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replace_OnlyTouchesItsSource()
    {
        var cache = new SqliteLibraryCache(_db.Factory);
        await cache.ReplaceSourceAsync("steam", [Entry("steam", "1"), Entry("steam", "2")], CancellationToken.None);
        await cache.ReplaceSourceAsync("epic", [Entry("epic", "a")], CancellationToken.None);

        await cache.ReplaceSourceAsync("steam", [Entry("steam", "3")], CancellationToken.None);

        (await cache.LoadAllAsync(CancellationToken.None)).Select(e => e.Ref.ToString()).Order().ShouldBe(["epic:a", "steam:3"]);
    }

    [Fact]
    public async Task FirstSeen_IsRecordedOnce_AndSurvivesRemoval()
    {
        var cache = new SqliteLibraryCache(_db.Factory);
        await cache.ReplaceSourceAsync("steam", [Entry("steam", "1")], CancellationToken.None);
        var first = (await cache.LoadFirstSeenAsync(CancellationToken.None))[new ItemRef("steam", "1")];

        await Task.Delay(20);
        await cache.ReplaceSourceAsync("steam", [Entry("steam", "1"), Entry("steam", "2")], CancellationToken.None);
        await cache.ReplaceSourceAsync("steam", [Entry("steam", "2")], CancellationToken.None); // "1" uninstalled/removed

        var seen = await cache.LoadFirstSeenAsync(CancellationToken.None);
        seen[new ItemRef("steam", "1")].ShouldBe(first);
        seen[new ItemRef("steam", "2")].ShouldBeGreaterThan(first);
    }

    [Fact]
    public async Task Replace_RejectsEntriesFromAnotherSource()
    {
        var cache = new SqliteLibraryCache(_db.Factory);

        await Should.ThrowAsync<ArgumentException>(() => cache.ReplaceSourceAsync("steam", [Entry("epic", "a")], CancellationToken.None));
    }

    [Fact]
    public async Task RowsWithUnknownValues_AreSkipped()
    {
        var cache = new SqliteLibraryCache(_db.Factory);
        await cache.ReplaceSourceAsync("steam", [Entry("steam", "1")], CancellationToken.None);
        await using (var connection = await _db.Factory.OpenAsync(CancellationToken.None))
        {
            await connection.ExecuteAsync(
                "INSERT INTO entries (source, external_id, kind, title, state, extra_json, updated_at) VALUES ('steam', '2', 'Spaceship', 'X', 'Installed', '{}', 'now');");
        }

        (await cache.LoadAllAsync(CancellationToken.None)).Single().Ref.ExternalId.ShouldBe("1");
    }

    private static LibraryEntry Entry(string source, string id) =>
        new(new ItemRef(source, id), ItemKind.Game, $"Game {id}", InstallState.Installed, null, null, null, LibraryEntry.NoExtra);
}
