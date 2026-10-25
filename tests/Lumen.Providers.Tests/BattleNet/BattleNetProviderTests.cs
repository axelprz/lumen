// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Providers.BattleNet;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.BattleNet;

// VERIFY: synthetic uninstall entries following community documentation; not yet compared with a real install.
public sealed class BattleNetProviderTests
{
    private const string Launcher = @"C:\Program Files (x86)\Battle.net\Battle.net.exe";

    private readonly FakeRegistry _registry = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task NotInstalled_IsNotAvailable()
    {
        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotAvailable);
    }

    [Fact]
    public async Task ListsGames_SkipsTheAppItselfAndMissingFolders()
    {
        Arrange();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Entries.Select(e => e.Ref.ToString()).Order().ShouldBe(["battlenet:newthing", "battlenet:prometheus", "battlenet:wow_classic"]);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("Diablo IV"));
    }

    [Fact]
    public async Task KnownProduct_LaunchesWithItsCode()
    {
        Arrange();

        var overwatch = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "prometheus");

        overwatch.Title.ShouldBe("Overwatch");
        overwatch.InstallDirectory.ShouldBe(@"C:\Program Files (x86)\Overwatch");
        overwatch.Launch.ShouldBe(new LaunchSpec(LaunchKind.ProcessWithArgs, Launcher, "--exec=\"launch Pro\"", @"C:\Program Files (x86)\Battle.net"));
    }

    [Fact]
    public async Task VariantUid_UsesTheBaseProductCode()
    {
        Arrange();

        var classic = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "wow_classic");

        classic.Launch!.Arguments.ShouldBe("--exec=\"launch WoW\"");
    }

    [Fact]
    public async Task UnknownProduct_OpensBattleNet_WithInfo()
    {
        Arrange();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);
        var future = result.Entries.Single(e => e.Ref.ExternalId == "newthing");

        future.Launch.ShouldBe(new LaunchSpec(LaunchKind.Executable, Launcher));
        result.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Info && d.Message.Contains("newthing"));
        result.Status.ShouldBe(ProviderStatus.Ok);
    }

    [Theory]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS --uid=Prometheus", "prometheus")]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --uid=\"hs_beta\" --lang=esMX", "hs_beta")]
    [InlineData("\"C:\\Program Files\\Other\\uninstall.exe\" --uid=prometheus", null)]
    [InlineData("\"C:\\ProgramData\\Battle.net\\Agent\\Blizzard Uninstaller.exe\" --lang=enUS", null)]
    [InlineData(null, null)]
    public void ProductUid(string? uninstallString, string? expected)
    {
        BattleNetProvider.ProductUid(uninstallString).ShouldBe(expected);
    }

    [Fact]
    public void EmbeddedLaunchCodes_IncludeCommonGames()
    {
        BattleNetProvider.LaunchCodes["prometheus"].ShouldBe("Pro");
        BattleNetProvider.LaunchCodes["fenris"].ShouldBe("Fen");
        BattleNetProvider.LaunchCodes["hs_beta"].ShouldBe("WTCG");
    }

    private BattleNetProvider Provider() => new(_registry, _fs, new FakeKnownFolders());

    private void Arrange()
    {
        _registry.LoadFixture("battlenet/registry.json");
        _fs.AddFile(Launcher, "MZ");
        _fs.AddDirectory(@"C:\Program Files (x86)\Overwatch");
        _fs.AddDirectory(@"D:\Games\World of Warcraft");
        _fs.AddDirectory(@"D:\Games\Future");
    }
}
