// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class LibraryFilterTests
{
    private static readonly IReadOnlyDictionary<ItemRef, DateTimeOffset> _noDates = new Dictionary<ItemRef, DateTimeOffset>();

    private readonly LibraryFilter _filter = new(new TitleNormalizer());

    private readonly LibraryItem _witcher = Item(Game("steam", "1", "The Witcher® 3: Wild Hunt") with { Extra = Extra(lastPlayed: 1_790_000_000, minutes: 6000) });
    private readonly LibraryItem _pokemon = Item(Game("epic", "p", "Pokémon Sample", installed: false));
    private readonly LibraryItem _alpha = Item(Game("gog", "a", "alpha Quest") with { Extra = Extra(lastPlayed: 1_795_000_000, minutes: 30) });
    private readonly LibraryItem _tool = Item(Game("manual", "t", "Paint Tool") with { Kind = ItemKind.App });

    private IReadOnlyList<LibraryItem> All => [_witcher, _pokemon, _alpha, _tool];

    [Fact]
    public void Default_IsEverythingByName_CaseInsensitive()
    {
        Titles(LibraryQuery.Default).ShouldBe(["alpha Quest", "Paint Tool", "Pokémon Sample", "The Witcher® 3: Wild Hunt"]);
        LibraryQuery.Default.IsFiltering.ShouldBeFalse();
    }

    [Theory]
    [InlineData("witcher", "The Witcher® 3: Wild Hunt")]
    [InlineData("WITCHER III", "The Witcher® 3: Wild Hunt")]
    [InlineData("wild hunt", "The Witcher® 3: Wild Hunt")]
    [InlineData("pokemon", "Pokémon Sample")]
    [InlineData("  quest ", "alpha Quest")]
    public void Search_IsCaseAccentAndSymbolInsensitive(string search, string expected)
    {
        Titles(new LibraryQuery(Search: search)).ShouldBe([expected]);
    }

    [Fact]
    public void Search_WithNoMatch_IsEmpty()
    {
        Titles(new LibraryQuery(Search: "zzz")).ShouldBeEmpty();
    }

    [Fact]
    public void KindStoreAndInstall_Filter()
    {
        Titles(new LibraryQuery(Kind: KindFilter.Apps)).ShouldBe(["Paint Tool"]);
        Titles(new LibraryQuery(Kind: KindFilter.Games)).Count.ShouldBe(3);
        Titles(new LibraryQuery(Store: "epic")).ShouldBe(["Pokémon Sample"]);
        Titles(new LibraryQuery(Install: InstallFilter.NotInstalled)).ShouldBe(["Pokémon Sample"]);
        Titles(new LibraryQuery(Install: InstallFilter.Installed)).Count.ShouldBe(3);
        new LibraryQuery(Store: "epic").IsFiltering.ShouldBeTrue();
    }

    [Fact]
    public void StoreFilter_MatchesAnyStoreOfAMergedItem()
    {
        var merged = new LibraryItem([Game("steam", "9", "Merged"), Game("gog", "9", "Merged")], Game("steam", "9", "Merged"));

        _filter.Apply([merged], new LibraryQuery(Store: "gog"), _noDates).ShouldHaveSingleItem();
    }

    [Fact]
    public void SortByLastPlayed_NewestFirst_NeverPlayedLast()
    {
        Titles(new LibraryQuery(Sort: LibrarySort.LastPlayed)).ShouldBe(["alpha Quest", "The Witcher® 3: Wild Hunt", "Paint Tool", "Pokémon Sample"]);
    }

    [Fact]
    public void SortByPlaytime_MostPlayedFirst()
    {
        Titles(new LibraryQuery(Sort: LibrarySort.Playtime))[..2].ShouldBe(["The Witcher® 3: Wild Hunt", "alpha Quest"]);
    }

    [Fact]
    public void SortByRecentlyAdded_UsesFirstSeen_UnknownLast()
    {
        var seen = new Dictionary<ItemRef, DateTimeOffset>
        {
            [_tool.Key] = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            [_pokemon.Key] = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero),
        };

        _filter.Apply(All, new LibraryQuery(Sort: LibrarySort.RecentlyAdded), seen).Select(i => i.Title)
            .ShouldBe(["Pokémon Sample", "Paint Tool", "alpha Quest", "The Witcher® 3: Wild Hunt"]);
    }

    [Fact]
    public void FiltersCombine()
    {
        // "t" is in "alpha Quest", "The Witcher…" and "Paint Tool" (an app, excluded); Pokémon isn't installed.
        Titles(new LibraryQuery(Search: "t", Kind: KindFilter.Games, Install: InstallFilter.Installed, Sort: LibrarySort.LastPlayed))
            .ShouldBe(["alpha Quest", "The Witcher® 3: Wild Hunt"]);
    }

    private List<string> Titles(LibraryQuery query) => _filter.Apply(All, query, _noDates).Select(i => i.Title).ToList();

    private static LibraryItem Item(LibraryEntry entry) => new([entry], entry);

    private static IReadOnlyDictionary<string, string> Extra(long lastPlayed, long minutes) =>
        new Dictionary<string, string> { ["lastPlayed"] = lastPlayed.ToString(System.Globalization.CultureInfo.InvariantCulture), ["playtimeMinutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    [Fact]
    public void CollectionFilter_ShowsOnlyItsItems_AndUnknownIdsShowNothing()
    {
        var collections = new[] { new Collection("work", "Work", [_alpha.Key, _tool.Key]) };

        _filter.Apply(All, new LibraryQuery(Collection: "work"), _noDates, collections).ShouldBe([_alpha, _tool]);
        _filter.Apply(All, new LibraryQuery(Collection: "gone"), _noDates, collections).ShouldBeEmpty();
        new LibraryQuery(Collection: "work").IsFiltering.ShouldBeTrue();
    }

    [Fact]
    public void Playtime_StoreNumberWins_LumenCountsWhereTheStoreHasNone_AndLastPlayedIsTheLatest()
    {
        static IReadOnlyDictionary<string, string> X(params (string K, string V)[] kv) => kv.ToDictionary(p => p.K, p => p.V);
        var steam = Game("steam", "1", "Sample") with { Extra = X(("playtimeMinutes", "600"), ("lumenMinutes", "45"), ("lastPlayed", "1790000000")) };
        var epic = Game("epic", "x", "Sample") with { Extra = X(("lumenMinutes", "90"), ("lumenLastPlayed", "1795000000")) };

        LibraryFilter.PlaytimeMinutes(Item(steam)).ShouldBe(600); // Steam's number; Lumen's 45 min are informational
        LibraryFilter.PlaytimeMinutes(Item(epic)).ShouldBe(90);
        LibraryFilter.PlaytimeMinutes(new LibraryItem([steam, epic], steam)).ShouldBe(690);
        LibraryFilter.LastPlayed(new LibraryItem([steam, epic], steam)).ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1795000000));

        LibraryFilter.SourceOfPlaytime(Item(steam)).ShouldBe(PlaytimeSource.Store);
        LibraryFilter.SourceOfPlaytime(Item(epic)).ShouldBe(PlaytimeSource.Lumen);
        LibraryFilter.SourceOfPlaytime(new LibraryItem([steam, epic], steam)).ShouldBe(PlaytimeSource.StoreAndLumen);
        LibraryFilter.SourceOfPlaytime(_tool).ShouldBe(PlaytimeSource.None);
    }

    [Fact]
    public void HiddenItems_OnlyShowInTheHiddenView()
    {
        var hidden = _alpha with { IsHidden = true };
        IReadOnlyList<LibraryItem> items = [_witcher, hidden];

        _filter.Apply(items, LibraryQuery.Default, _noDates).ShouldBe([_witcher]);
        _filter.Apply(items, new LibraryQuery(Hidden: true), _noDates).ShouldBe([hidden]);
        new LibraryQuery(Hidden: true).IsFiltering.ShouldBeFalse();
    }

    [Fact]
    public void Search_UsesTheCustomTitle()
    {
        var renamed = _alpha with { CustomTitle = "Zeta Renamed" };

        _filter.Apply([_witcher, renamed], new LibraryQuery(Search: "zeta"), _noDates).ShouldBe([renamed]);
        _filter.Apply([renamed, _witcher], LibraryQuery.Default, _noDates).Select(i => i.Title).ShouldBe(["The Witcher® 3: Wild Hunt", "Zeta Renamed"]);
    }
}
