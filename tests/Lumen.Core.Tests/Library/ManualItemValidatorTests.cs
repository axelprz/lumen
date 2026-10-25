// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Tests.Library;

public sealed class ManualItemValidatorTests
{
    [Theory]
    [InlineData(@"C:\Games\Sample\sample.exe", null)]
    [InlineData(@"C:\Games\Sample\sample.exe", "-windowed --fps 144")]
    [InlineData(@"D:/Games/thing.LNK", null)]
    [InlineData(@"C:\Users\Public\Desktop\Site.url", null)]
    [InlineData(@"""C:\Program Files\App\app.exe""", null)]
    [InlineData(@"\\nas\games\retro.exe", null)]
    [InlineData("https://example.com/play", null)]
    [InlineData("steam://rungameid/1000010", null)]
    [InlineData("discord://", null)]
    public void Valid(string target, string? args)
    {
        ManualItemValidator.Validate("Title", target, args).ShouldBe(ManualItemError.None);
    }

    [Theory]
    [InlineData(null, @"C:\a.exe", null, ManualItemError.TitleMissing)]
    [InlineData("  ", @"C:\a.exe", null, ManualItemError.TitleMissing)]
    [InlineData("T", null, null, ManualItemError.TargetMissing)]
    [InlineData("T", @"C:\script.bat", null, ManualItemError.UnsupportedFileType)]
    [InlineData("T", @"C:\tool.ps1", null, ManualItemError.UnsupportedFileType)]
    [InlineData("T", @"C:\noextension", null, ManualItemError.UnsupportedFileType)]
    [InlineData("T", @"games\a.exe", null, ManualItemError.RelativePath)]
    [InlineData("T", @"\\?\C:\a.exe", null, ManualItemError.RelativePath)]
    [InlineData("T", "file:///C:/a.exe", null, ManualItemError.UnsupportedUrl)]
    [InlineData("T", "javascript:alert(1)", null, ManualItemError.UnsupportedUrl)]
    [InlineData("T", "ms-msdt:/id PCWDiagnostic", null, ManualItemError.UnsupportedUrl)]
    [InlineData("T", "search-ms:query=x", null, ManualItemError.UnsupportedUrl)]
    [InlineData("T", "https://example.com", "--flag", ManualItemError.ArgumentsNotAllowed)]
    [InlineData("T", @"C:\a.lnk", "--flag", ManualItemError.ArgumentsNotAllowed)]
    public void Invalid(string? title, string? target, string? args, ManualItemError expected)
    {
        ManualItemValidator.Validate(title, target, args).ShouldBe(expected);
    }

    [Fact]
    public void Limits_MatchConfigSchema()
    {
        ManualItemValidator.Validate(new string('a', 121), @"C:\a.exe", null).ShouldBe(ManualItemError.TitleTooLong);
        ManualItemValidator.Validate("T", @"C:\a.exe", new string('a', 2001)).ShouldBe(ManualItemError.ArgumentsTooLong);
        ManualItemValidator.Validate(new string('a', 120), @"C:\a.exe", new string('a', 2000)).ShouldBe(ManualItemError.None);
    }

    [Fact]
    public void Create_SplitsPathAndUrl_AndTrims()
    {
        var id = Guid.NewGuid();

        var file = ManualItemValidator.Create(id, ItemKind.Game, "  Sample ", @" ""C:\Games\s.exe"" ", " -x ");
        var url = ManualItemValidator.Create(id, ItemKind.App, "Site", "https://example.com", null);

        file.ShouldBe(new ManualItem(id, ItemKind.Game, "Sample", @"C:\Games\s.exe", null, "-x"));
        url.ShouldBe(new ManualItem(id, ItemKind.App, "Site", null, "https://example.com", null));
        file.Ref.ToString().ShouldBe("manual:" + id.ToString("D"));
    }

    [Fact]
    public void Create_RejectsInvalid()
    {
        Should.Throw<ArgumentException>(() => ManualItemValidator.Create(Guid.NewGuid(), ItemKind.Game, "T", @"C:\x.bat", null));
    }

    [Theory]
    [InlineData(@"C:\Games\Sample Quest\SampleQuest.exe", "SampleQuest")]
    [InlineData(@"C:\Users\Public\Desktop\My Tool.lnk", "My Tool")]
    [InlineData("https://www.example.com/play?x=1", "example.com")]
    [InlineData("steam://rungameid/1000010", "rungameid")]
    public void SuggestTitle(string target, string expected)
    {
        ManualItemValidator.SuggestTitle(target).ShouldBe(expected);
    }

    [Theory]
    [InlineData("github.com/axelprz/lumen", "https://github.com/axelprz/lumen")]
    [InlineData("  www.example.com  ", "https://www.example.com")]
    [InlineData("example.com", "https://example.com")]
    [InlineData("store.example.co.uk/game?id=1", "https://store.example.co.uk/game?id=1")]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("steam://rungameid/1", "steam://rungameid/1")]
    [InlineData("notepad.exe", "notepad.exe")]
    [InlineData("game.lnk", "game.lnk")]
    [InlineData(@"C:\Games\a.exe", @"C:\Games\a.exe")]
    [InlineData(@"""C:\Games\a.exe""", @"C:\Games\a.exe")]
    [InlineData(@"games\a.exe", @"games\a.exe")]
    [InlineData(@"%USERPROFILE%\a.exe", @"%USERPROFILE%\a.exe")]
    [InlineData("just words", "just words")]
    public void NormalizeTarget(string input, string expected)
    {
        ManualItemValidator.NormalizeTarget(input).ShouldBe(expected);
    }

    [Fact]
    public void BareDomain_IsAcceptedAndSavedAsHttps()
    {
        ManualItemValidator.Validate("Lumen", "github.com/axelprz/lumen", null).ShouldBe(ManualItemError.None);

        var item = ManualItemValidator.Create(Guid.NewGuid(), ItemKind.App, "Lumen", "github.com/axelprz/lumen", null);

        item.Url.ShouldBe("https://github.com/axelprz/lumen");
        ManualItemValidator.SuggestTitle("github.com/axelprz/lumen").ShouldBe("github.com");
    }

    [Theory]
    [InlineData(@"C:\a.exe", false)]
    [InlineData(@"C:/a.exe", false)]
    [InlineData(@"\\server\share\a.exe", false)]
    [InlineData("https://example.com", true)]
    [InlineData("steam://run/1", true)]
    public void LooksLikeUrl(string target, bool expected)
    {
        ManualItemValidator.LooksLikeUrl(target).ShouldBe(expected);
    }
}
