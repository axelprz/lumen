// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Data.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Data.Tests;

public sealed class SqliteAppChoiceStoreTests : IAsyncLifetime
{
    private static readonly ItemRef _editor = new("startmenu", "Sample/Editor.lnk");
    private static readonly ItemRef _notes = new("appsfolder", "Contoso.Notes_x!App");

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
    public async Task Choices_AreStored_ChangedAndForgotten()
    {
        var store = new SqliteAppChoiceStore(_db.Factory);
        (await store.LoadAsync(CancellationToken.None)).ShouldBeEmpty();

        await store.SetAsync([_editor, _notes], AppChoice.Added, CancellationToken.None);
        await store.SetAsync([_notes], AppChoice.Ignored, CancellationToken.None);
        var choices = await store.LoadAsync(CancellationToken.None);
        choices[_editor].ShouldBe(AppChoice.Added);
        choices[_notes].ShouldBe(AppChoice.Ignored);

        await store.SetAsync([_editor], null, CancellationToken.None);
        (await store.LoadAsync(CancellationToken.None)).Keys.ShouldBe([_notes]);
    }
}
