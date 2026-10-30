// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.RegularExpressions;

namespace Lumen.Core.Tests.RepositoryChecks;

/// <summary>
/// Guards the motion rules (PLAN-v1 §4.9): interactions last 250 ms at most, and animations go through
/// <c>Motion</c> so Windows' "Animation effects" switch can turn them off. Reads source as plain text so it runs on any OS.
/// </summary>
public sealed partial class UiMotionTests
{
    private static readonly string _appDir = Path.Combine(RepoRoot.Find(), "src", "Lumen.App");

    [Theory]
    [InlineData("Fast")]
    [InlineData("Normal")]
    public void InteractionDurations_AreAtMost250Ms(string name)
    {
        var code = File.ReadAllText(Path.Combine(_appDir, "Services", "Motion.cs"));
        var match = Regex.Match(code, @"\b" + name + @" = new\(TimeSpan\.FromMilliseconds\((?<ms>\d+)\)\)");

        match.Success.ShouldBeTrue($"Motion.{name} not found");
        int.Parse(match.Groups["ms"].Value).ShouldBeLessThanOrEqualTo(250);
    }

    [Fact]
    public void AmbientBreathing_IsSlowCappedAndChecksTheSetting()
    {
        // Ambience is the one long-running animation: a few frames a second, off with "Animation effects" or a hidden window.
        var code = File.ReadAllText(Path.Combine(_appDir, "Services", "Motion.cs"));

        code.ShouldContain("Timeline.SetDesiredFrameRate(breath, AmbientFrameRate)");
        Regex.Match(code, @"AmbientFrameRate = (?<fps>\d+);").Groups["fps"].Value.ShouldNotBeEmpty();
        int.Parse(Regex.Match(code, @"AmbientFrameRate = (?<fps>\d+);").Groups["fps"].Value).ShouldBeLessThanOrEqualTo(30);
        code.ShouldContain("var run = offset >= 0 && Enabled && element.IsVisible && !minimized;");
    }

    [Fact]
    public void AnimationsOutsideMotion_AreOnlyTheOnesThatCheckTheSetting()
    {
        // Allowed: Motion itself, and AccentService (it checks Motion.Enabled before animating).
        var allowed = new[] { "Motion.cs", "AccentService.cs" };
        var offenders = Directory.EnumerateFiles(_appDir, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !allowed.Contains(Path.GetFileName(path)))
            .Where(path => Animation().IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty("use Motion.To / Motion.PageIn so \"Animation effects\" off is respected");
    }

    [Fact]
    public void AccentService_ChecksTheSettingBeforeFading()
    {
        File.ReadAllText(Path.Combine(_appDir, "Services", "AccentService.cs")).ShouldContain("Motion.Enabled");
    }

    [Theory]
    [InlineData("HomeView.xaml.cs", "Motion.Stagger(Sections)")]
    [InlineData("LibraryView.xaml.cs", "Motion.Stagger(PageBlocks)")]
    [InlineData("SettingsView.xaml.cs", "Motion.Stagger(PageBlocks)")]
    [InlineData("CollectionsView.xaml.cs", "Motion.Stagger(PageBlocks)")]
    [InlineData("RoutinesView.xaml.cs", "Motion.Stagger(PageBlocks)")]
    public void HomeAndLibrary_StaggerTheirBlocksOnLoad(string file, string call)
    {
        File.ReadAllText(Path.Combine(_appDir, "Views", file)).ShouldContain(call);
    }

    [Theory]
    [InlineData("GameDetailView.xaml")]
    [InlineData("RoutinesView.xaml")]
    [InlineData("LibraryView.xaml")]
    public void CoveringPanels_ComeInWithMotion(string file)
    {
        File.ReadAllText(Path.Combine(_appDir, "Views", file)).ShouldContain("Motion.ShowIn=\"True\"");
    }

    [Fact]
    public void EveryView_UsesTheTypeScale()
    {
        // The scale sizes (Label 11 … Display 44) have tokens; a literal one is a view that skipped the scale.
        var offenders = Directory.EnumerateFiles(Path.Combine(_appDir, "Views"), "*.xaml")
            .Where(path => LiteralScaleSize().IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void FirstRun_AndQuickSearch_ComeInWithMotion()
    {
        File.ReadAllText(Path.Combine(_appDir, "Views", "MainWindow.xaml")).ShouldContain("Motion.ShowIn=\"True\"");
        File.ReadAllText(Path.Combine(_appDir, "Views", "QuickSearchWindow.xaml.cs")).ShouldContain("Motion.PanelIn(");
    }

    [GeneratedRegex("FontSize=\"(11|12|13|14|15|16|18|20|24|32|44)\"")]
    private static partial Regex LiteralScaleSize();

    [GeneratedRegex(@"new\s+(Double|Color|Thickness)Animation\s*\(")]
    private static partial Regex Animation();
}
