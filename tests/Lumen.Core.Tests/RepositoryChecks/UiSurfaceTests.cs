// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Tests.RepositoryChecks;

/// <summary>
/// Guards the Mica wiring (M10): the backdrop brushes the code swaps at run time must exist in the theme as opaque
/// defaults, and the main window must paint with them. Also guards the template look (brand chrome, glass cards,
/// ambient lights). Reads files as plain text so it runs on any OS.
/// </summary>
public sealed class UiSurfaceTests
{
    private static readonly string _appDir = Path.Combine(RepoRoot.Find(), "src", "Lumen.App");

    [Theory]
    [InlineData("Lumen.Brush.Backdrop")]
    [InlineData("Lumen.Brush.SidebarBackdrop")]
    public void Theme_DefinesTheBackdropBrushes(string key)
    {
        var theme = File.ReadAllText(Path.Combine(_appDir, "Themes", "Theme.Dark.xaml"));

        theme.ShouldContain($"x:Key=\"{key}\"");
    }

    [Fact]
    public void MainWindow_PaintsWindowAndSidebarWithTheBackdropBrushes()
    {
        var xaml = File.ReadAllText(Path.Combine(_appDir, "Views", "MainWindow.xaml"));

        xaml.ShouldContain("Background=\"{DynamicResource Lumen.Brush.Backdrop}\"");
        xaml.ShouldContain("Background=\"{DynamicResource Lumen.Brush.SidebarBackdrop}\"");
    }

    [Fact]
    public void MicaBackdrop_SwapsTheSameKeysTheThemeDefines()
    {
        var code = File.ReadAllText(Path.Combine(_appDir, "Services", "MicaBackdrop.cs"));

        code.ShouldContain("\"Lumen.Brush.Backdrop\"");
        code.ShouldContain("\"Lumen.Brush.SidebarBackdrop\"");
    }

    [Theory]
    [InlineData("Lumen.Brush.Brand")]
    [InlineData("Lumen.Brush.BrandGradient")]
    [InlineData("Lumen.Brush.Glass")]
    [InlineData("Lumen.Brush.GlassBorder")]
    [InlineData("Lumen.Brush.AmbientBlue")]
    [InlineData("Lumen.Brush.AmbientViolet")]
    public void Theme_DefinesTheBrandGlassAndAmbientBrushes(string key)
    {
        var theme = File.ReadAllText(Path.Combine(_appDir, "Themes", "Theme.Dark.xaml"));

        theme.ShouldContain($"x:Key=\"{key}\"");
    }

    [Theory]
    [InlineData("Buttons.xaml")]
    [InlineData("Sidebar.xaml")]
    [InlineData("Inputs.xaml")]
    [InlineData("ComboBox.xaml")]
    public void Chrome_UsesTheBrand_NotTheGameAccent(string file)
    {
        // Mixed accent (2026-10-30): the game color tints art and background only; controls keep the fixed brand.
        File.ReadAllText(Path.Combine(_appDir, "Themes", "Controls", file)).ShouldNotContain("Lumen.Brush.Accent");
    }

    [Fact]
    public void Cards_AreGlass()
    {
        var surfaces = File.ReadAllText(Path.Combine(_appDir, "Themes", "Controls", "Surfaces.xaml"));

        surfaces.ShouldContain("<Setter Property=\"Background\" Value=\"{DynamicResource Lumen.Brush.Glass}\" />");
    }

    [Fact]
    public void MainWindow_HasTheTopBarSearchAndTheToasts()
    {
        var xaml = File.ReadAllText(Path.Combine(_appDir, "Views", "MainWindow.xaml"));

        xaml.ShouldContain("x:Name=\"TopSearch\"");
        xaml.ShouldContain("Text=\"{Binding SearchText, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.ShouldContain("ItemsSource=\"{Binding Toasts.Items}\"");
        File.ReadAllText(Path.Combine(_appDir, "Views", "MainWindow.xaml.cs")).ShouldContain("TopSearch.Focus()");
    }

    [Fact]
    public void SidebarMonitor_OnlyRunsWhileTheWindowIsShown()
    {
        File.ReadAllText(Path.Combine(_appDir, "Views", "MainWindow.xaml.cs"))
            .ShouldContain("shell.Footer.SetActive(IsVisible && WindowState != WindowState.Minimized)");
    }

    [Fact]
    public void Home_HeroIsACardWithItsArtFadingIn_AndTilesShowStoreAndPlay()
    {
        var xaml = File.ReadAllText(Path.Combine(_appDir, "Views", "HomeView.xaml"));

        xaml.ShouldContain("x:Name=\"HeroArt\"");
        xaml.ShouldContain("Text=\"{Binding StoreName}\"");
        xaml.ShouldContain("Command=\"{Binding DataContext.PlayTileCommand");
        File.ReadAllText(Path.Combine(_appDir, "Views", "HomeView.xaml.cs")).ShouldContain("Motion.FadeIn(HeroArt)");
    }

    [Fact]
    public void LanguageSwitch_AlsoSetsTheFormatCulture()
    {
        File.ReadAllText(Path.Combine(_appDir, "Localization", "LocalizationService.cs"))
            .ShouldContain("CultureInfo.CurrentCulture = format;");
    }

    [Fact]
    public void MainWindow_AmbientLightsBreatheThroughMotion()
    {
        var xaml = File.ReadAllText(Path.Combine(_appDir, "Views", "MainWindow.xaml"));

        xaml.ShouldContain("Fill=\"{DynamicResource Lumen.Brush.AmbientBlue}\" services:Motion.Breathe=");
        xaml.ShouldContain("Fill=\"{DynamicResource Lumen.Brush.AmbientViolet}\" services:Motion.Breathe=");
    }
}
