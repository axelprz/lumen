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

    [GeneratedRegex(@"new\s+(Double|Color|Thickness)Animation\s*\(")]
    private static partial Regex Animation();
}
