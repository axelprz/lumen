// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Core.Sync;

namespace Lumen.Core.Tests.Sync;

public sealed class SyncFolderServiceTests
{
    private static readonly Guid Manual1 = Guid.Parse("6f1c2a7e-0d4b-4c8e-9b1a-2e5f3c7d9a10");

    private readonly MemoryFolder _cloud = new();

    private FakePc Pc() => new(_cloud);

    private static async Task SeedAsync(FakePc pc)
    {
        await pc.Settings.SetAsync(LocalSettingKeys.Language, "es", CancellationToken.None);
        await pc.Manual.SaveAsync(new ManualItem(Manual1, ItemKind.App, "n8n", null, "https://n8n.example.com", null), CancellationToken.None);
        await pc.Collections.CreateAsync("friends", "Friends", CancellationToken.None);
        await pc.AddToCollectionAsync("friends", "steam:570", "gog:1");
        await pc.AddToCollectionAsync(Collection.FavoritesId, "steam:730");
        await pc.Choices.SetAsync([ItemRef.Parse("startmenu:Junk")], AppChoice.Ignored, CancellationToken.None);
        await pc.Overrides.SetTitleAsync([ItemRef.Parse("steam:570")], "Dota", CancellationToken.None);
    }

    [Fact]
    public async Task FirstPc_WritesItsDataToTheFolder_AndNothingElse()
    {
        var a = Pc();
        await SeedAsync(a);

        await a.AttachAsync();

        _cloud.Text.Keys.Order().ShouldBe(["/sync/collections/favorites.json", "/sync/collections/friends.json", "/sync/config.json"]);
        _cloud["config.json"].ShouldContain("\"language\": \"es\"");
        _cloud["config.json"].ShouldContain("n8n");
        a.Service.Status.ShouldBe(SyncStatus.Active);
        a.Settings.Values[LocalSettingKeys.SyncFolder].ShouldBe("/sync");
    }

    [Fact]
    public async Task SecondPc_ReceivesEverything_AndSaysWhichManualItemsCame()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();

        var b = Pc();
        await b.AttachAsync();

        (await b.Settings.GetAsync(LocalSettingKeys.Language, CancellationToken.None)).ShouldBe("es");
        b.Manual.Items.Values.Single().Title.ShouldBe("n8n");
        (await b.CollectionAsync("friends"))!.Items.Select(i => i.ToString()).ShouldBe(["steam:570", "gog:1"]);
        (await b.CollectionAsync(Collection.FavoritesId))!.Items.Select(i => i.ToString()).ShouldBe(["steam:730"]);
        b.Choices.Choices.ShouldContainKey(ItemRef.Parse("startmenu:Junk"));
        b.Overrides.Overrides[ItemRef.Parse("steam:570")].Title.ShouldBe("Dota");
        b.AppliedEvents.Single().ManualItemsReceived.ShouldBe(["n8n"]);
    }

    [Fact]
    public async Task Changes_TravelBothWays()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        var b = Pc();
        await b.AttachAsync();

        await a.AddToCollectionAsync("friends", "epic:fortnite");
        await a.SyncAsync();
        await b.SyncAsync();
        (await b.CollectionAsync("friends"))!.Items.Count.ShouldBe(3);

        await b.Collections.CreateAsync("coop", "Co-op", CancellationToken.None);
        await b.AddToCollectionAsync("coop", "steam:1");
        await b.SyncAsync();
        await a.SyncAsync();
        (await a.CollectionAsync("coop"))!.Name.ShouldBe("Co-op");

        await b.Collections.DeleteAsync("coop", CancellationToken.None);
        await b.SyncAsync();
        await a.SyncAsync();
        (await a.CollectionAsync("coop")).ShouldBeNull();
        _cloud.Has("collections/coop.json").ShouldBeFalse();
    }

    [Fact]
    public async Task Settled_NextPassDoesNothing()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        var b = Pc();
        await b.AttachAsync();
        await a.SyncAsync();
        await b.SyncAsync();
        var writes = _cloud.Writes;
        a.AppliedEvents.Clear();
        b.AppliedEvents.Clear();

        await a.SyncAsync();
        await b.SyncAsync();

        _cloud.Writes.ShouldBe(writes);
        a.AppliedEvents.ShouldBeEmpty();
        b.AppliedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task BothChangedTheSameCollection_TheFolderWins()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        var b = Pc();
        await b.AttachAsync();

        await a.AddToCollectionAsync("friends", "a:1");
        await a.SyncAsync(); // A reached the folder first
        await b.AddToCollectionAsync("friends", "b:1");
        await b.SyncAsync();

        (await b.CollectionAsync("friends"))!.Items.Select(i => i.ToString()).ShouldBe(["steam:570", "gog:1", "a:1"]);
    }

    [Fact]
    public async Task EditedHereButDeletedThere_TheEditIsKept()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        var b = Pc();
        await b.AttachAsync();

        await a.Collections.DeleteAsync("friends", CancellationToken.None);
        await a.SyncAsync();
        await b.AddToCollectionAsync("friends", "b:1");
        await b.SyncAsync();

        (await b.CollectionAsync("friends"))!.Items.Select(i => i.ToString()).ShouldContain("b:1");
        _cloud.Has("collections/friends.json").ShouldBeTrue();
    }

    [Fact]
    public async Task FolderThatLooksWiped_NeverDeletesHere_AndIsRestored()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        _cloud.Text.Clear();

        await a.SyncAsync();

        (await a.CollectionAsync("friends"))!.Items.Count.ShouldBe(2);
        a.Manual.Items.Count.ShouldBe(1);
        _cloud.Has("collections/friends.json").ShouldBeTrue();
        _cloud.Has("config.json").ShouldBeTrue();
    }

    [Fact]
    public async Task FolderThatIsAway_TouchesNothing()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        _cloud.Exists = false;

        await a.SyncAsync();

        a.Service.Status.ShouldBe(SyncStatus.FolderUnavailable);
        (await a.CollectionAsync("friends"))!.Items.Count.ShouldBe(2);

        _cloud.Exists = true;
        await a.SyncAsync();
        a.Service.Status.ShouldBe(SyncStatus.Active);
    }

    [Fact]
    public async Task UnreadableAndNewerFiles_AreLeftAlone_AndReported()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        _cloud["collections/friends.json"] = "{ half written";
        _cloud["config.json"] = """{ "schema": 2, "future": true }""";
        await a.AddToCollectionAsync("friends", "x:1");

        await a.SyncAsync();

        _cloud["collections/friends.json"].ShouldBe("{ half written");
        _cloud["config.json"].ShouldBe("""{ "schema": 2, "future": true }""");
        a.Service.Problems.Count.ShouldBe(2);
        (await a.CollectionAsync("friends"))!.Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ADangerousManualItem_FromTheFolder_IsNotImported()
    {
        _cloud["config.json"] = """
            { "schema": 1, "manualItems": [
              { "id": "manual:6f1c2a7e-0d4b-4c8e-9b1a-2e5f3c7d9a10", "kind": "app", "title": "Evil", "url": "javascript:alert(1)" },
              { "id": "manual:22222222-2222-2222-2222-222222222222", "kind": "app", "title": "Fine", "url": "https://example.org" } ] }
            """;
        var b = Pc();

        await b.AttachAsync();

        b.Manual.Items.Values.Select(i => i.Title).ShouldBe(["Fine"]);
        b.Service.Problems.ShouldContain(p => p.Contains("Evil", StringComparison.Ordinal) || p.Contains("manualItems[0]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnotherVersionsKeys_SurviveWhenThisPcRewritesTheConfig()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        _cloud["config.json"] = _cloud["config.json"].Replace("\"schema\": 1,", "\"schema\": 1,\n  \"stores\": { \"battlenet\": { \"enabled\": false } },", StringComparison.Ordinal);

        await a.Settings.SetAsync(LocalSettingKeys.CloseToTray, "0", CancellationToken.None);
        await a.SyncAsync();

        _cloud["config.json"].ShouldContain("battlenet");
        _cloud["config.json"].ShouldContain("\"minimizeToTray\": false");
    }

    [Fact]
    public async Task BothSidesHaveData_AChoiceIsRequired()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        var b = Pc();
        await b.Collections.CreateAsync("mine", "Mine", CancellationToken.None);
        await b.AddToCollectionAsync("mine", "steam:1");

        var pairing = await b.Service.InspectAsync(FakePc.FolderPath, CancellationToken.None);

        pairing.NeedsChoice.ShouldBeTrue();
        pairing.Folder.ManualItems.ShouldBe(1);
        pairing.ThisPc.Collections.ShouldBe(1);
        await Should.ThrowAsync<InvalidOperationException>(() => b.AttachAsync());
        b.Service.Folder.ShouldBeNull();
    }

    private async Task<FakePc> TwoSidedAsync()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        var b = Pc();
        await b.Collections.CreateAsync("mine", "Mine", CancellationToken.None);
        await b.AddToCollectionAsync("mine", "steam:1");
        await b.Collections.CreateAsync("friends", "Friends (mine)", CancellationToken.None);
        await b.AddToCollectionAsync("friends", "local:only");
        return b;
    }

    [Fact]
    public async Task Merge_KeepsBoth()
    {
        var b = await TwoSidedAsync();

        await b.AttachAsync(SyncStart.Merge);

        (await b.CollectionAsync("mine"))!.Items.Count.ShouldBe(1);
        (await b.CollectionAsync("friends"))!.Name.ShouldBe("Friends");
        (await b.CollectionAsync("friends"))!.Items.Select(i => i.ToString()).ShouldBe(["steam:570", "gog:1", "local:only"]);
        b.Manual.Items.Count.ShouldBe(1);
        _cloud.Has("collections/mine.json").ShouldBeTrue();
        _cloud["collections/friends.json"].ShouldContain("local:only");
    }

    [Fact]
    public async Task UseFolder_DropsWhatOnlyThisPcHad()
    {
        var b = await TwoSidedAsync();

        await b.AttachAsync(SyncStart.UseFolder);

        (await b.CollectionAsync("mine")).ShouldBeNull();
        (await b.CollectionAsync("friends"))!.Items.Select(i => i.ToString()).ShouldBe(["steam:570", "gog:1"]);
        b.Manual.Items.Count.ShouldBe(1);
        _cloud.Has("collections/mine.json").ShouldBeFalse();
    }

    [Fact]
    public async Task UseThisPc_ReplacesTheFolder()
    {
        var b = await TwoSidedAsync();

        await b.AttachAsync(SyncStart.UseThisPc);

        _cloud.Has("collections/mine.json").ShouldBeTrue();
        _cloud["collections/friends.json"].ShouldContain("local:only");
        _cloud["collections/friends.json"].ShouldNotContain("steam:570");
        _cloud.Has("collections/favorites.json").ShouldBeFalse();
        _cloud["config.json"].ShouldNotContain("n8n");
        (await b.CollectionAsync("friends"))!.Name.ShouldBe("Friends (mine)");
    }

    [Fact]
    public async Task Detach_ForgetsTheFolder_ButKeepsEverything()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();

        await a.Service.DetachAsync(CancellationToken.None);

        a.Service.Status.ShouldBe(SyncStatus.Off);
        a.Settings.Values.ShouldNotContainKey(LocalSettingKeys.SyncFolder);
        a.Settings.Values.ShouldNotContainKey(LocalSettingKeys.SyncBaseline);
        _cloud.Text.Count.ShouldBe(3);
        (await a.CollectionAsync("friends"))!.Items.Count.ShouldBe(2);
        await a.SyncAsync(); // does nothing
    }

    [Fact]
    public async Task Resume_PicksTheFolderUpAgain()
    {
        var a = Pc();
        await SeedAsync(a);
        await a.AttachAsync();
        _cloud["collections/friends.json"] = PortableCollectionJson.Write(new Collection("friends", "Friends", [ItemRef.Parse("new:1")]));

        var again = new FakePc(_cloud);
        foreach (var (key, value) in a.Settings.Values)
        {
            await again.Settings.SetAsync(key, value, CancellationToken.None);
        }

        await again.Service.ResumeAsync(CancellationToken.None);

        again.Service.Folder.ShouldBe("/sync");
        again.Service.Status.ShouldBe(SyncStatus.Active);
    }

    [Fact]
    public async Task NothingToSync_WritesNothing()
    {
        var a = Pc();

        await a.AttachAsync();

        _cloud.Writes.ShouldBe(0);
        a.Service.Status.ShouldBe(SyncStatus.Active);
    }

    [Fact]
    public async Task ApplyingFavorites_DoesNotRenameThem_AndEmptyingWorks()
    {
        var a = Pc();
        await a.Settings.SetAsync(LocalSettingKeys.Language, "en", CancellationToken.None); // a folder with only one file would look wiped
        await a.AddToCollectionAsync(Collection.FavoritesId, "steam:1");
        await a.AttachAsync();
        var b = Pc();
        await b.AttachAsync();
        (await b.CollectionAsync(Collection.FavoritesId))!.Name.ShouldBe("Favorites");

        await a.Collections.RemoveAsync(Collection.FavoritesId, [ItemRef.Parse("steam:1")], CancellationToken.None);
        await a.SyncAsync();
        await b.SyncAsync();

        (await b.CollectionAsync(Collection.FavoritesId))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReorderedItems_AreAppliedInOrder()
    {
        var a = Pc();
        await a.Collections.CreateAsync("c", "C", CancellationToken.None);
        await a.AddToCollectionAsync("c", "x:1", "x:2", "x:3");
        await a.AttachAsync();
        var b = Pc();
        await b.AttachAsync();

        _cloud["collections/c.json"] = PortableCollectionJson.Write(new Collection("c", "C", new[] { "x:3", "x:1" }.Select(ItemRef.Parse).ToList()));
        await b.SyncAsync();

        (await b.CollectionAsync("c"))!.Items.Select(i => i.ToString()).ShouldBe(["x:3", "x:1"]);
    }
}
