// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Providers.Steam;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Steam;

public sealed class SteamProviderTests
{
    private const string SteamDir = @"C:\Program Files (x86)\Steam";
    private const string ExtraLibrary = @"D:\SteamLibrary";

    private readonly FakeRegistry _registry = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task SteamMissing_IsNotAvailable()
    {
        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.NotAvailable);
        result.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task RegistryPointsToMissingFolder_IsNotAvailable()
    {
        _registry.Set(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", "c:/nowhere/steam");

        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotAvailable);
    }

    [Fact]
    public async Task FullLibrary_ListsFullyInstalledGamesFromEveryLibrary()
    {
        ArrangeFullLibrary();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Entries.Select(e => e.Ref.ToString()).Order().ShouldBe(["steam:1000010", "steam:1000011", "steam:1000020"]);
        result.Entries.ShouldAllBe(e => e.State == InstallState.Installed && e.Kind == ItemKind.Game);
    }

    [Fact]
    public async Task Entry_HasTitleDirectoryAndSteamUris()
    {
        ArrangeFullLibrary();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);
        var quest = result.Entries.Single(e => e.Ref.ExternalId == "1000010");
        var racer = result.Entries.Single(e => e.Ref.ExternalId == "1000020");

        quest.Title.ShouldBe("Sample Quest");
        quest.InstallDirectory.ShouldBe(@"C:\Program Files (x86)\Steam\steamapps\common\Sample Quest");
        quest.Launch.ShouldBe(new LaunchSpec(LaunchKind.Uri, "steam://rungameid/1000010"));
        quest.Install.ShouldBe(new LaunchSpec(LaunchKind.Uri, "steam://install/1000010"));
        quest.Extra["lastPlayed"].ShouldBe("1790001000");
        racer.Title.ShouldBe("Example Racer™");
        racer.InstallDirectory.ShouldBe(@"D:\SteamLibrary\steamapps\common\example_racer");
        racer.Extra.ContainsKey("lastPlayed").ShouldBeFalse();
    }

    [Fact]
    public async Task PartiallyDownloadedAndRedistributables_AreSkipped()
    {
        ArrangeFullLibrary();

        var ids = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Select(e => e.Ref.ExternalId).ToList();

        ids.ShouldNotContain("1000012");
        ids.ShouldNotContain("228980");
    }

    [Fact]
    public async Task BrokenManifest_IsReportedAndTheRestStillLoads()
    {
        ArrangeFullLibrary();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Partial);
        result.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("appmanifest_1000013.acf"));
        result.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task LockedManifest_IsReportedAndTheRestStillLoads()
    {
        ArrangeMainLibraryOnly();
        _fs.MakeUnreadable(SteamDir + @"\steamapps\appmanifest_1000010.acf");

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Partial);
        result.Entries.Select(e => e.Ref.ExternalId).ShouldBe(["1000011"]);
    }

    [Fact]
    public async Task MissingLibraryFoldersFile_ScansMainLibraryWithWarning()
    {
        ArrangeMainLibraryOnly();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Partial);
        result.Entries.Select(e => e.Ref.ExternalId).Order().ShouldBe(["1000010", "1000011"]);
    }

    [Fact]
    public async Task LibraryOnDisconnectedDrive_IsAWarningNotAFailure()
    {
        ArrangeMainLibraryOnly();
        _fs.AddFixture(SteamDir + @"\steamapps\libraryfolders.vdf", "steam/libraryfolders.vdf");

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Partial);
        result.Diagnostics.ShouldContain(d => d.Message.Contains(ExtraLibrary));
        result.Entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LegacyLibraryFoldersFormat_IsSupported()
    {
        ArrangeMainLibraryOnly();
        _fs.AddFixture(SteamDir + @"\steamapps\libraryfolders.vdf", "steam/libraryfolders_legacy.vdf");
        var diagnostics = new List<ProviderDiagnostic>();

        var libraries = await Provider().FindLibrariesAsync(SteamDir, diagnostics, CancellationToken.None);

        libraries.ShouldBe([SteamDir, @"E:\Games\Steam"]);
    }

    [Fact]
    public async Task NeverThrows_EvenIfTheRegistryDoes()
    {
        var provider = new SteamProvider(new ThrowingRegistry(), _fs);

        var result = await provider.ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Failed);
    }

    [Fact]
    public async Task OwnedScan_IsNotConfiguredYet()
    {
        (await Provider().ScanOwnedAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotConfigured);
    }

    [Fact]
    public void EmbeddedExclusions_ContainRedistributables()
    {
        SteamExclusions.Embedded.ShouldContain("228980");
    }

    private SteamProvider Provider() => new(_registry, _fs);

    private void ArrangeMainLibraryOnly()
    {
        // Real registry value format: lowercase, forward slashes.
        _registry.Set(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", "c:/program files (x86)/steam");
        foreach (var id in new[] { "1000010", "1000011", "1000012", "228980" })
        {
            _fs.AddFixture($@"{SteamDir}\steamapps\appmanifest_{id}.acf", $"steam/main/appmanifest_{id}.acf");
        }
    }

    private void ArrangeFullLibrary()
    {
        ArrangeMainLibraryOnly();
        _fs.AddFixture(SteamDir + @"\steamapps\libraryfolders.vdf", "steam/libraryfolders.vdf");
        _fs.AddFixture(SteamDir + @"\steamapps\appmanifest_1000013.acf", "steam/main/appmanifest_1000013.acf");
        _fs.AddFixture(ExtraLibrary + @"\steamapps\appmanifest_1000020.acf", "steam/extra/appmanifest_1000020.acf");
    }

    private sealed class ThrowingRegistry : IRegistryReader
    {
        public string? GetString(RegistryHive hive, string keyPath, string valueName) => throw new UnauthorizedAccessException();

        public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath) => throw new UnauthorizedAccessException();
    }
}
