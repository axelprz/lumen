// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Library;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class HomeFeedTests
{
    private static readonly IReadOnlyDictionary<ItemRef, DateTimeOffset> _noDates = new Dictionary<ItemRef, DateTimeOffset>();

    [Fact]
    public void ContinuePlaying_IsMostRecentFirst_AndFeaturesTheLatest()
    {
        var old = Played("1", "Old", 1_700_000_000);
        var recent = Played("2", "Recent", 1_790_000_000);
        var never = Item(Game("steam", "3", "Never"));

        var feed = HomeFeed.Build([old, never, recent], _noDates);

        feed.ContinuePlaying.ShouldBe([recent, old]);
        feed.Featured.ShouldBe(recent);
    }

    [Fact]
    public void HiddenItemsAndApps_NeverAppear()
    {
        var hidden = Played("1", "Hidden", 1_790_000_000) with { IsHidden = true };
        var app = Item(Game("manual", "a", "Tool") with { Kind = ItemKind.App, Extra = Extra(1_795_000_000) });
        var visible = Played("2", "Visible", 1_700_000_000);

        var feed = HomeFeed.Build([hidden, app, visible], _noDates);

        feed.ContinuePlaying.ShouldBe([visible]);
        feed.Featured.ShouldBe(visible);
    }

    [Fact]
    public void WithoutHistory_FeaturesTheNewestInstalledGame()
    {
        var older = Item(Game("steam", "1", "Older"));
        var newer = Item(Game("epic", "x", "Newer"));
        var notInstalled = Item(Game("gog", "9", "Owned only", installed: false));
        var firstSeen = new Dictionary<ItemRef, DateTimeOffset>
        {
            [older.Key] = DateTimeOffset.UnixEpoch.AddDays(1),
            [newer.Key] = DateTimeOffset.UnixEpoch.AddDays(5),
            [notInstalled.Key] = DateTimeOffset.UnixEpoch.AddDays(9),
        };

        var feed = HomeFeed.Build([older, newer, notInstalled], firstSeen);

        feed.ContinuePlaying.ShouldBeEmpty();
        feed.Featured.ShouldBe(newer);
    }

    [Fact]
    public void ContinuePlaying_IsCapped()
    {
        var items = Enumerable.Range(1, 20).Select(i => Played(i.ToString(CultureInfo.InvariantCulture), $"Game {i}", 1_700_000_000 + i)).ToList();

        HomeFeed.Build(items, _noDates).ContinuePlaying.Count.ShouldBe(HomeFeed.MaxContinuePlaying);
    }

    [Fact]
    public void EmptyLibrary_HasNothingToFeature()
    {
        var feed = HomeFeed.Build([], _noDates);

        feed.Featured.ShouldBeNull();
        feed.ContinuePlaying.ShouldBeEmpty();
    }

    [Fact]
    public void Favorites_AreGamesInTheOrderMarked_HiddenOnesLeftOut()
    {
        var first = Item(Game("steam", "1", "Zeta"));
        var second = Item(Game("epic", "x", "Alpha"));
        var hidden = Item(Game("gog", "9", "Hidden")) with { IsHidden = true };
        var app = App("startmenu", "Editor.lnk", "Editor");
        var favorites = new Collection(Collection.FavoritesId, "Favorites", [first.Key, hidden.Key, app.Key, second.Key]);

        var feed = HomeFeed.Build([second, first, hidden, app], _noDates, favorites);

        feed.Favorites.ShouldBe([first, second]);
        feed.QuickApps.ShouldBe([app]);
    }

    [Fact]
    public void QuickApps_WithoutFavoriteApps_AreTheLibrarysApps_ByName_Capped()
    {
        var apps = Enumerable.Range(1, 20).Select(i => App("startmenu", $"{i}.lnk", $"App {i:00}")).ToList();

        var feed = HomeFeed.Build(apps, _noDates);

        feed.QuickApps.Count.ShouldBe(HomeFeed.MaxQuickApps);
        feed.QuickApps[0].Title.ShouldBe("App 01");
        feed.Favorites.ShouldBeEmpty();
    }

    private static LibraryItem Played(string id, string title, long unix) => Item(Game("steam", id, title) with { Extra = Extra(unix) });

    private static LibraryItem Item(LibraryEntry entry) => new([entry], entry);

    private static IReadOnlyDictionary<string, string> Extra(long lastPlayed) =>
        new Dictionary<string, string> { ["lastPlayed"] = lastPlayed.ToString(CultureInfo.InvariantCulture) };

    private static LibraryItem App(string source, string id, string title) => Item(Game(source, id, title) with { Kind = ItemKind.App });
}
