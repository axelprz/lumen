// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Tests.Library;

public sealed class ItemRefTests
{
    [Theory]
    [InlineData("steam:570", "steam", "570")]
    [InlineData("xbox:Pkg_8wekyb3d8bbwe!App", "xbox", "Pkg_8wekyb3d8bbwe!App")]
    [InlineData("startmenu:Programs/Tools/a:b.lnk", "startmenu", "Programs/Tools/a:b.lnk")]
    public void Parse_SplitsOnFirstColon(string text, string source, string id)
    {
        var parsed = ItemRef.Parse(text);

        parsed.Source.ShouldBe(source);
        parsed.ExternalId.ShouldBe(id);
        parsed.ToString().ShouldBe(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("steam")]
    [InlineData(":570")]
    [InlineData("steam:")]
    [InlineData("Steam:570")]
    [InlineData("st eam:570")]
    [InlineData("steam:   ")]
    public void TryParse_RejectsInvalid(string? text)
    {
        ItemRef.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        new ItemRef("steam", "570").ShouldBe(ItemRef.Parse("steam:570"));
    }

    [Fact]
    public void Constructor_RejectsBadSource()
    {
        Should.Throw<ArgumentException>(() => new ItemRef("Steam", "570"));
    }
}
