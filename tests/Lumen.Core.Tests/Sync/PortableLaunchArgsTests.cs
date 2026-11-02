// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json.Nodes;
using Lumen.Core.Library;
using Lumen.Core.Sync;

namespace Lumen.Core.Tests.Sync;

public sealed class PortableLaunchArgsTests
{
    [Fact]
    public void Overrides_SteamArguments_RoundTrip()
    {
        var steam = ItemRef.Parse("steam:570");
        var written = PortableConfigJson.Write(
            new PortableConfig { Overrides = new Dictionary<ItemRef, ItemOverride> { [steam] = new ItemOverride(null, false, "-windowed") } });

        JsonNode.Parse(written)!["overrides"]!["steam:570"]!["args"]!.GetValue<string>().ShouldBe("-windowed");
        PortableConfigJson.Parse(written).Config!.Overrides[steam].Args.ShouldBe("-windowed");
    }

    [Fact]
    public void Overrides_ArgumentsFromTheFolder_MustPassTheSameCheckAsTypedOnes()
    {
        var parsed = PortableConfigJson.Parse("""
            { "schema": 1, "overrides": {
                "steam:1": { "args": "-ok" },
                "steam:2": { "args": "bad\nline" },
                "epic:x": { "args": "-ignored" } } }
            """);

        parsed.Config!.Overrides[ItemRef.Parse("steam:1")].Args.ShouldBe("-ok");
        parsed.Config.Overrides.ContainsKey(ItemRef.Parse("steam:2")).ShouldBeFalse();
        parsed.Config.Overrides.ContainsKey(ItemRef.Parse("epic:x")).ShouldBeFalse();
        parsed.Problems.ShouldHaveSingleItem();
    }

    [Fact]
    public void Write_KeepsAnotherStoresArgsFromTheExistingFile()
    {
        var existing = """{ "schema": 1, "overrides": { "epic:x": { "args": "-keep" } } }""";

        var written = PortableConfigJson.Write(new PortableConfig(), existing);

        JsonNode.Parse(written)!["overrides"]!["epic:x"]!["args"]!.GetValue<string>().ShouldBe("-keep");
    }
}
