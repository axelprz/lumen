// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Library;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class QuickSearchTests
{
    private readonly QuickSearch _search = new(new TitleNormalizer());

    [Fact]
    public void TitleStart_ThenWordStart_ThenAnywhere()
    {
        var anywhere = Item("1", "Stardew Valley"); // "dew"
        var wordStart = Item("2", "Mountain Dew Simulator");
        var titleStart = Item("3", "Dewdrop Dive");

        _search.Find([anywhere, wordStart, titleStart], "dew").ShouldBe([titleStart, wordStart, anywhere]);
    }

    [Fact]
    public void IgnoresCaseAccentsAndSymbols()
    {
        var pokemon = Item("1", "Pokémon Legends");
        var witcher = Item("2", "The Witcher® 3: Wild Hunt");

        _search.Find([pokemon, witcher], "POKEMON").ShouldBe([pokemon]);
        _search.Find([pokemon, witcher], "witcher iii").ShouldBe([witcher]);
    }

    [Fact]
    public void Ties_GoToInstalled_ThenMostRecentlyPlayed()
    {
        var notInstalled = Item("1", "Hollow Echo", installed: false);
        var playedLongAgo = Item("2", "Hollow Pines", lastPlayed: 1_700_000_000);
        var playedRecently = Item("3", "Hollow Zone", lastPlayed: 1_790_000_000);

        _search.Find([notInstalled, playedLongAgo, playedRecently], "hollow").ShouldBe([playedRecently, playedLongAgo, notInstalled]);
    }

    [Fact]
    public void HiddenItems_NeverShow()
    {
        var hidden = Item("1", "Secret Game") with { IsHidden = true };

        _search.Find([hidden], "secret").ShouldBeEmpty();
    }

    [Fact]
    public void RenamedItems_AreFoundByTheirNewName()
    {
        var renamed = Item("1", "Original Name") with { CustomTitle = "My Favorite" };

        _search.Find([renamed], "favorite").ShouldBe([renamed]);
    }

    [Fact]
    public void NoText_ListsRecentlyPlayed()
    {
        var never = Item("1", "Never Played");
        var old = Item("2", "Old", lastPlayed: 1_700_000_000);
        var recent = Item("3", "Recent", lastPlayed: 1_790_000_000);

        _search.Find([never, old, recent], "  ").ShouldBe([recent, old]);
    }

    [Fact]
    public void KeepsAtMostMax()
    {
        var items = Enumerable.Range(0, 20).Select(i => Item(i.ToString(CultureInfo.InvariantCulture), $"Game {i}")).ToList();

        _search.Find(items, "game").Count.ShouldBe(QuickSearch.MaxResults);
    }

    [Fact]
    public void NoMatch_IsEmpty() => _search.Find([Item("1", "Celeste")], "zelda").ShouldBeEmpty();

    private static LibraryItem Item(string id, string title, bool installed = true, long? lastPlayed = null)
    {
        var entry = Game("steam", id, title, installed);
        if (lastPlayed is { } unix)
        {
            entry = entry with { Extra = new Dictionary<string, string> { ["lastPlayed"] = unix.ToString(CultureInfo.InvariantCulture) } };
        }

        return new LibraryItem([entry], entry);
    }
}
