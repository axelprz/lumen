// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class LibraryMergerTests
{
    private readonly LibraryMerger _merger = new(new TitleNormalizer(["goty edition"]));

    [Fact]
    public void SameGameOnTwoStores_BecomesOneItem()
    {
        var steam = Game("steam", "1", "Sample Quest™");
        var epic = Game("epic", "SampleQuestApp", "Sample Quest - GOTY Edition");

        var items = _merger.Merge([steam, epic], MergeRules.None);

        var item = items.ShouldHaveSingleItem();
        item.IsMerged.ShouldBeTrue();
        item.Entries.Select(e => e.Ref.Source).ShouldBe(["steam", "epic"]);
    }

    [Fact]
    public void DifferentGames_StaySeparate()
    {
        _merger.Merge([Game("steam", "1", "Alpha"), Game("epic", "b", "Beta")], MergeRules.None).Count.ShouldBe(2);
    }

    [Fact]
    public void SameTitleTwiceInOneStore_IsNotMerged()
    {
        var a = Game("steam", "1", "Sample");
        var b = Game("steam", "2", "Sample");
        var epic = Game("epic", "x", "Sample");

        var items = _merger.Merge([a, b, epic], MergeRules.None);

        items.Count.ShouldBe(2);
        items.ShouldContain(i => i.Entries.Count == 2); // one Steam copy pairs with Epic
        items.ShouldContain(i => i.Entries.Count == 1);
    }

    [Fact]
    public void Apps_AreNeverAutoMerged()
    {
        var a = Game("epic", "a", "Paint Tool") with { Kind = ItemKind.App };
        var b = Game("manual", "b", "Paint Tool") with { Kind = ItemKind.App };

        _merger.Merge([a, b], MergeRules.None).Count.ShouldBe(2);
    }

    [Fact]
    public void Primary_IsTheInstalledEntry()
    {
        var steamOwned = Game("steam", "1", "Sample", installed: false);
        var epicInstalled = Game("epic", "x", "Sample");

        var item = _merger.Merge([steamOwned, epicInstalled], MergeRules.None).Single();

        item.Primary.ShouldBe(epicInstalled);
        item.IsInstalled.ShouldBeTrue();
    }

    [Fact]
    public void Primary_FollowsStorePriority_WhenSeveralAreInstalled()
    {
        var gog = Game("gog", "9", "Sample");
        var steam = Game("steam", "1", "Sample");

        _merger.Merge([gog, steam], MergeRules.None).Single().Primary.ShouldBe(steam);
    }

    [Fact]
    public void Primary_IsThePreferredEntry_WhenItCanRun()
    {
        var steam = Game("steam", "1", "Sample");
        var gog = Game("gog", "9", "Sample");
        var rules = new MergeRules(new HashSet<ItemRef>(), new HashSet<ItemRef> { gog.Ref });

        _merger.Merge([steam, gog], rules).Single().Primary.ShouldBe(gog);
    }

    [Fact]
    public void PreferredButNotLaunchable_FallsBack()
    {
        var steam = Game("steam", "1", "Sample");
        var manualMissing = Game("manual", "m", "Sample") with { State = InstallState.NotInstalled, Launch = null, Install = null };
        var rules = new MergeRules(new HashSet<ItemRef>(), new HashSet<ItemRef> { manualMissing.Ref });

        _merger.Merge([steam, manualMissing], rules).Single().Primary.ShouldBe(steam);
    }

    [Fact]
    public void SeparatedEntries_StayAlone()
    {
        var steam = Game("steam", "1", "Sample");
        var epic = Game("epic", "x", "Sample");
        var rules = new MergeRules(new HashSet<ItemRef> { steam.Ref, epic.Ref }, new HashSet<ItemRef>());

        _merger.Merge([steam, epic], rules).Count.ShouldBe(2);
    }

    [Fact]
    public void EveryEntry_EndsUpInExactlyOneItem()
    {
        var entries = new[]
        {
            Game("steam", "1", "A"), Game("epic", "a", "A"), Game("gog", "g", "a"),
            Game("steam", "2", "B"), Game("xbox", "x", "C"), Game("manual", "m", "C"),
        };

        var items = _merger.Merge(entries, MergeRules.None);

        items.SelectMany(i => i.Entries).Select(e => e.Ref.ToString()).Order().ShouldBe(entries.Select(e => e.Ref.ToString()).Order());
        items.Count.ShouldBe(3);
    }

    [Fact]
    public void ManualLink_JoinsDifferentTitles()
    {
        var steam = Game("steam", "1", "Sample Quest");
        var epic = Game("epic", "x", "SQ: Remastered Collection");
        var rules = MergeRules.None with { Links = new Dictionary<ItemRef, ItemRef> { [epic.Ref] = steam.Ref } };

        var item = _merger.Merge([steam, epic], rules).ShouldHaveSingleItem();

        item.Entries.Select(e => e.Ref).ShouldBe([steam.Ref, epic.Ref]);
    }

    [Fact]
    public void ManualLink_WinsOverSeparated_AndIsTransitive()
    {
        var steam = Game("steam", "1", "Alpha");
        var epic = Game("epic", "x", "Beta");
        var gog = Game("gog", "9", "Gamma");
        var rules = new MergeRules(
            new HashSet<ItemRef> { steam.Ref, epic.Ref, gog.Ref },
            new HashSet<ItemRef>(),
            new Dictionary<ItemRef, ItemRef> { [epic.Ref] = steam.Ref, [gog.Ref] = epic.Ref });

        _merger.Merge([steam, epic, gog], rules).ShouldHaveSingleItem().Entries.Count.ShouldBe(3);
    }

    [Fact]
    public void ManualLink_ToAnEntryThatIsGone_IsIgnored()
    {
        var epic = Game("epic", "x", "Beta");
        var rules = MergeRules.None with { Links = new Dictionary<ItemRef, ItemRef> { [epic.Ref] = new ItemRef("steam", "404") } };

        _merger.Merge([epic, Game("gog", "9", "Gamma")], rules).Count.ShouldBe(2);
    }

    [Fact]
    public void ManualLink_JoinsWholeAutoMergedGroups()
    {
        var steam = Game("steam", "1", "Sample");
        var epic = Game("epic", "x", "Sample");
        var gog = Game("gog", "9", "Sample Deluxe Edition Plus");
        var rules = MergeRules.None with { Links = new Dictionary<ItemRef, ItemRef> { [gog.Ref] = epic.Ref } };

        _merger.Merge([steam, epic, gog], rules).ShouldHaveSingleItem().Entries.Count.ShouldBe(3);
    }
}
