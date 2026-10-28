// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Providers.Apps;

namespace Lumen.Providers.Tests.Apps;

public sealed class AppJunkFilterTests
{
    private readonly AppJunkFilter _junk = AppJunkFilter.Default;

    [Theory]
    [InlineData("Uninstall 7-Zip")]
    [InlineData("Desinstalar Sample")]
    [InlineData("Read Me")]
    [InlineData("LÉAME")]
    [InlineData("Sample Website")]
    [InlineData("Microsoft Visual C++ Redistributable")]
    [InlineData("Release Notes")]
    public void ClutterNames_AreJunk(string name) => _junk.IsJunkName(name).ShouldBeTrue();

    [Theory]
    [InlineData("Discord")]
    [InlineData("Helpdesk Pro")] // "help" is a whole word only
    [InlineData("Visual Studio Code")]
    [InlineData("Manualidades")]
    public void RealApps_AreNotJunk(string name) => _junk.IsJunkName(name).ShouldBeFalse();

    [Theory]
    [InlineData(@"C:\Program Files\Sample\unins000.exe", true)]
    [InlineData(@"C:\Program Files\Sample\readme.txt", true)]
    [InlineData(@"C:\Windows\explorer.exe", true)]
    [InlineData(@"C:\Program Files\Sample\Sample.exe", false)]
    public void Targets(string target, bool junk) => _junk.IsJunkTarget(target).ShouldBe(junk);

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("steam://rungameid/1", true)]
    [InlineData("com.epicgames.launcher://apps/x", true)]
    [InlineData("sampleapp://open", false)]
    [InlineData("not a url", true)]
    public void Urls(string url, bool skipped) => _junk.IsSkippedUrl(url).ShouldBe(skipped);
}
