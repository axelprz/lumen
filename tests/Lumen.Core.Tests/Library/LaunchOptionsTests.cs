// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Tests.Library;

public sealed class LaunchOptionsTests
{
    private static LibraryEntry Steam(string id = "570", string? target = null) =>
        new(new ItemRef("steam", id), ItemKind.Game, "Game", InstallState.Installed, null,
            new LaunchSpec(LaunchKind.Uri, target ?? $"steam://rungameid/{id}"), null, LibraryEntry.NoExtra);

    [Fact]
    public void Apply_SteamGame_PutsTheEscapedArgumentsInTheLink()
    {
        var entry = LaunchOptions.Apply(Steam(), "  -windowed -w 1280 ");

        entry.Launch!.Target.ShouldBe("steam://run/570//-windowed%20-w%201280/");
        entry.Launch.Kind.ShouldBe(LaunchKind.Uri);
    }

    [Fact]
    public void Apply_SlashesAndQuotesInTheArguments_CannotEscapeTheLink()
    {
        var entry = LaunchOptions.Apply(Steam(), "-x \"a/b\" ?y=1#z");

        var target = entry.Launch!.Target;
        target.ShouldStartWith("steam://run/570//");
        target["steam://run/570//".Length..].TrimEnd('/').ShouldNotContain("/");
        target.ShouldNotContain("?");
        target.ShouldNotContain("#");
        target.ShouldNotContain(" ");
    }

    [Fact]
    public void Apply_NothingToApply_ReturnsTheSameEntry()
    {
        var entry = Steam();

        LaunchOptions.Apply(entry, null).ShouldBeSameAs(entry);
        LaunchOptions.Apply(entry, "   ").ShouldBeSameAs(entry);
        LaunchOptions.Apply(entry, "bad\nline").ShouldBeSameAs(entry);
    }

    [Fact]
    public void Apply_OtherStores_AreNeverChanged()
    {
        var epic = TestEntries.Game("epic", "x", "Game");

        LaunchOptions.Supports(epic).ShouldBeFalse();
        LaunchOptions.Apply(epic, "-windowed").ShouldBeSameAs(epic);
    }

    [Fact]
    public void Supports_NeedsANumericSteamIdAndALaunch()
    {
        LaunchOptions.Supports(Steam()).ShouldBeTrue();
        LaunchOptions.Supports(Steam("12a")).ShouldBeFalse();
        LaunchOptions.Supports(Steam(target: "https://example.com")).ShouldBeFalse();
        LaunchOptions.Supports(Steam() with { Launch = null }).ShouldBeFalse();
    }

    [Fact]
    public void Clean_TrimsAndRejectsControlCharactersAndLongText()
    {
        LaunchOptions.Clean("  -a  ").ShouldBe("-a");
        LaunchOptions.Clean("").ShouldBeNull();
        LaunchOptions.Clean("a\tb").ShouldBeNull();
        LaunchOptions.Clean(new string('x', LaunchOptions.MaxLength)).ShouldNotBeNull();
        LaunchOptions.Clean(new string('x', LaunchOptions.MaxLength + 1)).ShouldBeNull();
    }

    [Fact]
    public void ApplyOverrides_PutsTheArgumentsInThePrimaryLaunch_AndTheItem()
    {
        var steam = Steam();
        var item = new LibraryItem([steam], steam);
        var overrides = new Dictionary<ItemRef, ItemOverride> { [steam.Ref] = new ItemOverride(null, false, "-novid") };

        var result = LibraryService.ApplyOverrides(item, overrides);

        result.LaunchArguments.ShouldBe("-novid");
        result.SupportsLaunchArguments.ShouldBeTrue();
        result.Primary.Launch!.Target.ShouldBe("steam://run/570//-novid/");
        result.Entries.ShouldHaveSingleItem().ShouldBe(result.Primary);
        LaunchService.PrimaryAction(result.Primary)!.Target.ShouldBe("steam://run/570//-novid/");
    }

    [Fact]
    public void ApplyOverrides_ArgumentsOnANonSteamEntry_AreIgnored()
    {
        var epic = TestEntries.Game("epic", "x", "Game");
        var item = new LibraryItem([epic], epic);
        var overrides = new Dictionary<ItemRef, ItemOverride> { [epic.Ref] = new ItemOverride(null, false, "-novid") };

        var result = LibraryService.ApplyOverrides(item, overrides);

        result.LaunchArguments.ShouldBeNull();
        result.Primary.Launch.ShouldBe(epic.Launch);
    }
}
