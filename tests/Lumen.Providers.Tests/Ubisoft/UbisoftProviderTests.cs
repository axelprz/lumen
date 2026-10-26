// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Providers.Tests.Fakes;
using Lumen.Providers.Ubisoft;

namespace Lumen.Providers.Tests.Ubisoft;

// Synthetic registry snapshot following the real layout (compared with a real install, 2026-10).
public sealed class UbisoftProviderTests
{
    private readonly FakeRegistry _registry = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task NotInstalled_IsNotAvailable()
    {
        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotAvailable);
    }

    [Fact]
    public async Task ListsInstalledGames_WithTitlesFromUninstallEntries()
    {
        Arrange();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);
        var odyssey = result.Entries.Single(e => e.Ref.ExternalId == "4001");

        result.Entries.Select(e => e.Ref.ToString()).Order().ShouldBe(["ubisoft:4001", "ubisoft:4002"]);
        odyssey.Title.ShouldBe("Sample Odyssey");
        odyssey.InstallDirectory.ShouldBe(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Sample Odyssey");
        odyssey.Launch.ShouldBe(new LaunchSpec(LaunchKind.Uri, "uplay://launch/4001/0"));
    }

    [Fact]
    public async Task WithoutUninstallEntry_TitleIsTheFolderName()
    {
        Arrange();

        var racer = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "4002");

        racer.Title.ShouldBe("Untitled Racer");
    }

    [Fact]
    public async Task MissingFolder_IsSkippedWithInfo()
    {
        Arrange();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Ok);
        result.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Info && d.Message.Contains("4003"));
    }

    [Fact]
    public async Task LauncherOnly_IsOkAndEmpty()
    {
        _registry.Set(Lumen.Core.Platform.RegistryHive.LocalMachine, UbisoftProvider.LauncherKey, "InstallDir", @"C:\Ubisoft\");

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Ok);
        result.Entries.ShouldBeEmpty();
    }

    private UbisoftProvider Provider() => new(_registry, _fs);

    private void Arrange()
    {
        _registry.LoadFixture("ubisoft/registry.json");
        _fs.AddDirectory(@"C:\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\Sample Odyssey");
        _fs.AddDirectory(@"D:\Ubisoft\Untitled Racer");
    }
}
