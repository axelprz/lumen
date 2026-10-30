// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json.Nodes;
using Lumen.Core.Library;
using Lumen.Core.Sync;

namespace Lumen.Core.Tests.Sync;

public sealed class PortableJsonTests
{
    private const string Guid1 = "6f1c2a7e-0d4b-4c8e-9b1a-2e5f3c7d9a10";

    [Fact]
    public void Config_ReadsWhatLumenManages()
    {
        var parsed = PortableConfigJson.Parse($$"""
            {
              "schema": 1, "language": "es",
              "ui": { "minimizeToTray": false, "dynamicAccent": true },
              "quickSearch": { "hotkey": "Ctrl+Alt+K" },
              "manualItems": [ { "id": "manual:{{Guid1}}", "kind": "app", "title": "n8n", "url": "https://n8n.example.com" } ],
              "overrides": { "steam:570": { "title": "Dota", "hidden": true } },
              "ignoredApps": ["startmenu:Uninstall Something"]
            }
            """);

        parsed.IsValid.ShouldBeTrue();
        var config = parsed.Config!;
        config.Language.ShouldBe("es");
        config.MinimizeToTray.ShouldBe(false);
        config.Hotkey.ShouldBe("Ctrl+Alt+K");
        config.ManualItems.Single().Title.ShouldBe("n8n");
        config.Overrides[ItemRef.Parse("steam:570")].ShouldBe(new ItemOverride("Dota", true));
        config.IgnoredApps.ShouldBe([ItemRef.Parse("startmenu:Uninstall Something")]);
        parsed.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void Config_ManualItemsFollowTheDialogRules_BadOnesAreSkipped()
    {
        var parsed = PortableConfigJson.Parse($$"""
            {
              "schema": 1,
              "manualItems": [
                { "id": "manual:{{Guid1}}", "kind": "app", "title": "Evil", "url": "file:///C:/Windows/System32/calc.exe" },
                { "id": "manual:11111111-1111-1111-1111-111111111111", "kind": "app", "title": "Relative", "path": "tools\\run.exe" },
                { "id": "manual:22222222-2222-2222-2222-222222222222", "kind": "app", "title": "Script", "path": "C:\\tools\\run.cmd" },
                { "id": "manual:33333333-3333-3333-3333-333333333333", "kind": "game", "title": "Fine", "path": "C:\\Games\\fine.exe", "args": "-windowed" }
              ]
            }
            """);

        parsed.Config!.ManualItems.Select(i => i.Title).ShouldBe(["Fine"]);
        parsed.Problems.Count.ShouldBe(3);
    }

    [Fact]
    public void Config_NewerSchema_IsRefused()
    {
        var parsed = PortableConfigJson.Parse("""{ "schema": 2 }""");

        parsed.IsValid.ShouldBeFalse();
        parsed.NewerSchema.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "language": "es" }""")]
    public void Config_Garbage_IsNotValid(string text) => PortableConfigJson.Parse(text).IsValid.ShouldBeFalse();

    [Fact]
    public void Config_BadValues_AreIgnoredNotFatal()
    {
        var parsed = PortableConfigJson.Parse("""{ "schema": 1, "language": "fr", "quickSearch": { "hotkey": "Shift+A" }, "ignoredApps": ["steam:570"] }""");

        parsed.IsValid.ShouldBeTrue();
        parsed.Config!.Language.ShouldBeNull();
        parsed.Config.Hotkey.ShouldBeNull();
        parsed.Config.IgnoredApps.ShouldBeEmpty();
        parsed.Problems.Count.ShouldBe(3);
    }

    [Fact]
    public void Config_Write_KeepsWhatItDoesNotManage()
    {
        const string existing = """
            { "schema": 1, "stores": { "battlenet": { "enabled": false } }, "ui": { "dynamicAccent": false },
              "overrides": { "steam:570": { "title": "Old", "artwork": { "cover": "sgdb:1" } } }, "updates": { "channel": "beta" } }
            """;
        var config = new PortableConfig
        {
            MinimizeToTray = true,
            Overrides = new Dictionary<ItemRef, ItemOverride> { [ItemRef.Parse("steam:570")] = new("Dota", false) },
        };

        var root = JsonNode.Parse(PortableConfigJson.Write(config, existing))!.AsObject();

        root["stores"]!["battlenet"]!["enabled"]!.GetValue<bool>().ShouldBeFalse();
        root["ui"]!["dynamicAccent"]!.GetValue<bool>().ShouldBeFalse();
        root["ui"]!["minimizeToTray"]!.GetValue<bool>().ShouldBeTrue();
        root["updates"]!["channel"]!.GetValue<string>().ShouldBe("beta");
        root["overrides"]!["steam:570"]!["title"]!.GetValue<string>().ShouldBe("Dota");
        root["overrides"]!["steam:570"]!["artwork"]!["cover"]!.GetValue<string>().ShouldBe("sgdb:1");
    }

    [Fact]
    public void Config_Write_DropsOverridesThatNoLongerExist_ButKeepsTheirArtwork()
    {
        const string existing = """{ "schema": 1, "overrides": { "steam:1": { "title": "A" }, "steam:2": { "title": "B", "artwork": { "cover": "x" } } } }""";

        var root = JsonNode.Parse(PortableConfigJson.Write(new PortableConfig(), existing))!.AsObject();

        root["overrides"]!.AsObject().ContainsKey("steam:1").ShouldBeFalse();
        root["overrides"]!["steam:2"]!.AsObject().ContainsKey("title").ShouldBeFalse();
        root["overrides"]!["steam:2"]!["artwork"]!["cover"]!.GetValue<string>().ShouldBe("x");
    }

    [Fact]
    public void Config_Write_IsDeterministic_AndHashIgnoresFormatting()
    {
        var config = PortableConfigJson.Parse("""{ "schema": 1, "ignoredApps": ["appsfolder:b", "startmenu:a"], "language": "en" }""").Config!;
        var reordered = PortableConfigJson.Parse("""{"ignoredApps":["startmenu:a","appsfolder:b"],"schema":1,"language":"en"}""").Config!;

        PortableConfigJson.Write(config).ShouldBe(PortableConfigJson.Write(reordered));
        PortableConfigJson.Hash(config).ShouldBe(PortableConfigJson.Hash(reordered));
        PortableConfigJson.Write(config, PortableConfigJson.Write(config)).ShouldBe(PortableConfigJson.Write(config));
    }

    [Fact]
    public void Config_Write_KeepsAccentsReadable() =>
        PortableConfigJson.Write(new PortableConfig { Overrides = new Dictionary<ItemRef, ItemOverride> { [ItemRef.Parse("steam:1")] = new("Pokémon ñandú", false) } })
            .ShouldContain("Pokémon ñandú");

    [Fact]
    public void Collection_ReadsAndNeedsTheIdToMatchTheFileName()
    {
        const string json = """{ "schema": 1, "id": "friends", "name": { "es": "Amigos", "en": "Friends" }, "items": ["steam:570", "steam:570", "gog:1", "bad"] }""";

        var parsed = PortableCollectionJson.Parse(json, "friends");

        parsed.Collection!.Name.ShouldBe("Friends");
        parsed.Collection.Items.ShouldBe([ItemRef.Parse("steam:570"), ItemRef.Parse("gog:1")]);
        parsed.Problems.Count.ShouldBe(1);
        PortableCollectionJson.Parse(json, "other").IsValid.ShouldBeFalse();
        PortableCollectionJson.Parse(json.Replace("\"friends\"", "\"Bad Id\"", StringComparison.Ordinal), "Bad Id").IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Collection_Write_KeepsIconSortAndPerLanguageNames()
    {
        const string existing = """{ "schema": 1, "id": "friends", "name": { "es": "Amigos", "en": "Friends" }, "icon": "people", "sort": "name", "items": [] }""";

        var same = JsonNode.Parse(PortableCollectionJson.Write(new Collection("friends", "Friends", [ItemRef.Parse("gog:1")]), existing))!.AsObject();
        var renamed = JsonNode.Parse(PortableCollectionJson.Write(new Collection("friends", "Buddies", []), existing))!.AsObject();

        same["name"]!["es"]!.GetValue<string>().ShouldBe("Amigos");
        same["icon"]!.GetValue<string>().ShouldBe("people");
        same["sort"]!.GetValue<string>().ShouldBe("name");
        same["items"]![0]!.GetValue<string>().ShouldBe("gog:1");
        renamed["name"]!.GetValue<string>().ShouldBe("Buddies");
    }

    [Fact]
    public void Collection_FavoritesHashIgnoresTheName()
    {
        var a = new Collection(Collection.FavoritesId, "Favorites", [ItemRef.Parse("steam:1")]);
        var b = a with { Name = "Favoritos" };

        PortableCollectionJson.Hash(a).ShouldBe(PortableCollectionJson.Hash(b));
        PortableCollectionJson.Hash(new Collection("x", "A", [])).ShouldNotBe(PortableCollectionJson.Hash(new Collection("x", "B", [])));
    }

    [Fact]
    public void Merge_JoinsEverything_AndTheFolderWinsOnTheSameThing()
    {
        var folder = new PortableConfig
        {
            Language = "es",
            IgnoredApps = [ItemRef.Parse("startmenu:a")],
            Overrides = new Dictionary<ItemRef, ItemOverride> { [ItemRef.Parse("steam:1")] = new("Folder", false) },
        };
        var local = new PortableConfig
        {
            Language = "en",
            Hotkey = "Ctrl+Alt+K",
            IgnoredApps = [ItemRef.Parse("startmenu:b")],
            Overrides = new Dictionary<ItemRef, ItemOverride> { [ItemRef.Parse("steam:1")] = new("Local", false), [ItemRef.Parse("steam:2")] = new(null, true) },
        };

        var merged = SyncMerge.MergeConfig(folder, local);

        merged.Language.ShouldBe("es");
        merged.Hotkey.ShouldBe("Ctrl+Alt+K");
        merged.IgnoredApps.Count.ShouldBe(2);
        merged.Overrides[ItemRef.Parse("steam:1")].Title.ShouldBe("Folder");
        merged.Overrides.ContainsKey(ItemRef.Parse("steam:2")).ShouldBeTrue();

        var collection = SyncMerge.MergeCollection(
            new Collection("c", "Folder name", [ItemRef.Parse("a:1"), ItemRef.Parse("a:2")]),
            new Collection("c", "Local name", [ItemRef.Parse("a:2"), ItemRef.Parse("a:3")]));
        collection.Name.ShouldBe("Folder name");
        collection.Items.Select(i => i.ToString()).ShouldBe(["a:1", "a:2", "a:3"]);
    }
}
