// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

public sealed class VdfParserTests
{
    [Fact]
    public void Parses_SyntaxFeatures()
    {
        var root = VdfParser.Parse(Fixtures.Read("steam/syntax.vdf")).GetObject("root")!;

        root.GetString("quoted").ShouldBe("value with \"quotes\" and \\backslash\\");
        root.GetString("newline").ShouldBe("a\nb");
        root.GetString("unquoted").ShouldBe("token");
        root.GetString("conditional").ShouldBe("win");
        root.GetObject("nested")!.GetString("KEY").ShouldBe("v");
    }

    [Fact]
    public void Lookups_IgnoreCase_AndKeepOrder()
    {
        var root = VdfParser.Parse("\"A\" { \"b\" \"1\" \"c\" \"2\" }").GetObject("a")!;

        root.Properties.Select(p => p.Key).ShouldBe(["b", "c"]);
        root.GetString("B").ShouldBe("1");
        root.GetString("missing").ShouldBeNull();
        root.GetObject("b").ShouldBeNull();
    }

    [Fact]
    public void Parses_WindowsPathsWithEscapedBackslashes()
    {
        var root = VdfParser.Parse(Fixtures.Read("steam/libraryfolders.vdf"));

        root.GetObject("libraryfolders")!.GetObject("1")!.GetString("path").ShouldBe(@"D:\SteamLibrary");
    }

    [Theory]
    [InlineData("\"a\" { \"b\" \"c\"")]
    [InlineData("\"a\" \"unterminated")]
    [InlineData("}")]
    [InlineData("\"a\" }")]
    public void Malformed_Throws(string text)
    {
        Should.Throw<VdfParseException>(() => VdfParser.Parse(text));
    }

    [Fact]
    public void Empty_IsEmptyObject()
    {
        VdfParser.Parse("  // nothing\n").Properties.ShouldBeEmpty();
    }
}
