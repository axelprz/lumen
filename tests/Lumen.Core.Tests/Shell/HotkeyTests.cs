// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Shell;

namespace Lumen.Core.Tests.Shell;

public sealed class HotkeyTests
{
    [Fact]
    public void Default_IsAltShiftSpace() => Hotkey.Default.ToString().ShouldBe("Alt+Shift+Space");

    [Theory]
    [InlineData("Alt+Shift+Space", "Alt+Shift+Space")]
    [InlineData("shift + alt + space", "Alt+Shift+Space")]
    [InlineData("Control+Alt+k", "Ctrl+Alt+K")]
    [InlineData("Win+Shift+7", "Shift+Win+7")]
    [InlineData("F9", "F9")]
    [InlineData("Shift+F24", "Shift+F24")]
    public void Parses_ToCanonicalText(string text, string expected)
    {
        Hotkey.TryParse(text, out var hotkey).ShouldBeTrue();
        hotkey!.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Alt+Shift")] // no key
    [InlineData("Shift+A")] // would swallow typing
    [InlineData("A")]
    [InlineData("Alt+A+B")] // two keys
    [InlineData("Alt+Escape")] // not offered
    [InlineData("Alt+F25")]
    [InlineData("Hyper+A")]
    public void Rejects(string? text) => Hotkey.TryParse(text, out _).ShouldBeFalse();

    [Fact]
    public void RoundTrips()
    {
        Hotkey.TryParse("Ctrl+Alt+Shift+Win+F1", out var hotkey).ShouldBeTrue();
        Hotkey.TryParse(hotkey!.ToString(), out var again).ShouldBeTrue();
        again.ShouldBe(hotkey);
    }
}
