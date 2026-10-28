// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class AppCurationTests
{
    private static readonly TitleNormalizer Normalizer = new();

    private readonly LibraryEntry _editor = App("startmenu", "Sample/Editor.lnk", "Sample Editor", @"C:\Program Files\Sample\editor.exe");
    private readonly LibraryEntry _notes = App("appsfolder", "Contoso.Notes_x!App", "Contoso Notes");
    private readonly LibraryEntry _game = Game("gog", "1", "Sample Quest") with { InstallDirectory = @"C:\Games\Sample Quest" };

    [Fact]
    public void Suggestions_AreDetectedApps_WithNoChoice()
    {
        var choices = new Dictionary<ItemRef, AppChoice> { [_notes.Ref] = AppChoice.Ignored };

        AppCuration.Detected([_editor, _notes, _game], choices, Normalizer).ShouldBe([_editor]);
        AppCuration.Detected([_editor, _notes, _game], choices, Normalizer, AppChoice.Ignored).ShouldBe([_notes]);
    }

    [Fact]
    public void ShortcutsToKnownGames_AreNeverSuggested()
    {
        var sameTitle = App("startmenu", "Sample Quest [GOG.com]/Sample Quest.lnk", "Sample Quest", @"C:\Program Files\GOG Galaxy\GalaxyClient.exe");
        var insideGame = App("startmenu", "Sample Quest/Launcher.lnk", "Quest Launcher", @"C:\Games\Sample Quest\launcher.exe");

        AppCuration.Detected([_game, sameTitle, insideGame, _editor], new Dictionary<ItemRef, AppChoice>(), Normalizer).ShouldBe([_editor]);
    }

    [Fact]
    public void OnlyAddedApps_AreShown_StoresAndManualItemsAlways()
    {
        var choices = new Dictionary<ItemRef, AppChoice> { [_editor.Ref] = AppChoice.Added, [_notes.Ref] = AppChoice.Ignored };

        AppCuration.IsShown(_editor, choices).ShouldBeTrue();
        AppCuration.IsShown(_notes, choices).ShouldBeFalse();
        AppCuration.IsShown(App("appsfolder", "Other!App", "Other"), choices).ShouldBeFalse();
        AppCuration.IsShown(_game, choices).ShouldBeTrue();
        AppCuration.IsShown(Game("manual", "m", "Mine") with { Kind = ItemKind.App }, choices).ShouldBeTrue();
    }

    private static LibraryEntry App(string source, string id, string title, string? target = null)
    {
        var extra = target is null ? LibraryEntry.NoExtra : new Dictionary<string, string> { ["target"] = target };
        return Game(source, id, title) with { Kind = ItemKind.App, Extra = extra };
    }
}
