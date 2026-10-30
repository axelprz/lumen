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

    [Theory]
    [InlineData("GameDetailView.xaml")]
    [InlineData("CollectionsView.xaml")]
    [InlineData("RoutinesView.xaml")]
    public void ReworkedViews_UseTheTypeScale(string file)
    {
        // The listed sizes have a token (Label 11 … Display 44); a literal one is a view that skipped the scale.
        var xaml = File.ReadAllText(Path.Combine(_appDir, "Views", file));

        LiteralScaleSize().Matches(xaml).Select(m => m.Value).ShouldBeEmpty();
    }

    [GeneratedRegex("FontSize=\"(11|12|13|14|15|16|18|20|24|32|44)\"")]
    private static partial Regex LiteralScaleSize();

    [GeneratedRegex(@"new\s+(Double|Color|Thickness)Animation\s*\(")]
    private static partial Regex Animation();
}
