// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Tests.RepositoryChecks;

/// <summary>
/// Guards the Mica wiring (M10): the backdrop brushes the code swaps at run time must exist in the theme as opaque
/// defaults, and the main window must paint with them. Reads files as plain text so it runs on any OS.
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
}
