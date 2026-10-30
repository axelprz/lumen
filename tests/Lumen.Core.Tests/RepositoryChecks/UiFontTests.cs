// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Tests.RepositoryChecks;

/// <summary>
/// Guards the UI font (Exo 2, M10): the four weights and the licence must ship with the app, and the
/// theme must point at the family the files declare. Reads files as plain text so it runs on any OS.
/// </summary>
public sealed class UiFontTests
{
    private static readonly string _appDir = Path.Combine(RepoRoot.Find(), "src", "Lumen.App");

    [Theory]
    [InlineData("Exo2-Regular.ttf")]
    [InlineData("Exo2-Medium.ttf")]
    [InlineData("Exo2-SemiBold.ttf")]
    [InlineData("Exo2-Bold.ttf")]
    [InlineData("OFL.txt")]
    public void FontFilesAndLicence_AreInTheRepository(string file)
    {
        File.Exists(Path.Combine(_appDir, "Assets", "Fonts", file)).ShouldBeTrue();
    }

    [Fact]
    public void FontFiles_AreEmbeddedAsResources()
    {
        var project = File.ReadAllText(Path.Combine(_appDir, "Lumen.App.csproj"));

        project.ShouldContain("Assets\\Fonts\\*.ttf");
    }

    [Fact]
    public void Theme_UsesTheBundledFamily()
    {
        var theme = File.ReadAllText(Path.Combine(_appDir, "Themes", "Theme.Dark.xaml"));

        theme.ShouldContain("/Lumen;component/Assets/Fonts/#Exo 2");
    }
}
