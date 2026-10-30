// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.RegularExpressions;

namespace Lumen.Core.Tests.RepositoryChecks;

/// <summary>
/// Everything Lumen ships must have its license in THIRD-PARTY-NOTICES.txt, and both that file and the GPL text travel
/// next to Lumen.exe (MIT and Apache-2.0 require the license with binary copies). Reads files as text; runs on any OS.
/// </summary>
public sealed partial class LicenseNoticeTests
{
    private static readonly string _root = RepoRoot.Find();

    [Fact]
    public void EveryPackageTheAppShips_IsInTheNotices()
    {
        var notices = File.ReadAllText(Path.Combine(_root, "THIRD-PARTY-NOTICES.txt"));
        var packages = Directory.EnumerateFiles(Path.Combine(_root, "src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(file => PackageReference().Matches(File.ReadAllText(file)).Select(m => m.Groups["id"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        packages.ShouldNotBeEmpty();
        packages.Where(id => !notices.Contains(id, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty("add the package and its license to THIRD-PARTY-NOTICES.txt and to AboutViewModel");
    }

    [Theory]
    [InlineData("MIT LICENSE")]
    [InlineData("Apache License")]
    [InlineData("SIL OPEN FONT LICENSE")]
    [InlineData("MICROSOFT WEBVIEW2 SDK LICENSE")]
    public void Notices_CarryTheFullLicenseTexts(string heading)
    {
        File.ReadAllText(Path.Combine(_root, "THIRD-PARTY-NOTICES.txt")).ShouldContain(heading);
    }

    [Fact]
    public void LicenseFiles_ShipNextToTheApp()
    {
        var project = File.ReadAllText(Path.Combine(_root, "src", "Lumen.App", "Lumen.App.csproj"));

        project.ShouldContain("Link=\"LICENSE.txt\" CopyToOutputDirectory=\"PreserveNewest\"");
        project.ShouldContain("Link=\"THIRD-PARTY-NOTICES.txt\" CopyToOutputDirectory=\"PreserveNewest\"");
    }

    [GeneratedRegex("<PackageReference Include=\"(?<id>[^\"]+)\"")]
    private static partial Regex PackageReference();
}
