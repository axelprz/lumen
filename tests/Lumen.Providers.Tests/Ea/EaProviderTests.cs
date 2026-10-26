// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Xml;
using Lumen.Core.Library;
using Lumen.Providers.Ea;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Ea;

// Synthetic uninstall entries and installerdata.xml following the real format (compared with a real install, 2026-10).
public sealed class EaProviderTests
{
    private const string EaApp = @"C:\Program Files\Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe";

    private readonly FakeRegistry _registry = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task NotInstalled_IsNotAvailable()
    {
        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotAvailable);
    }

    [Fact]
    public async Task ListsGamesWithInstallerData_UsingContentIdForLaunch()
    {
        Arrange();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);
        var fc = result.Entries.Single(e => e.Ref.ExternalId == "1000001");

        result.Entries.Select(e => e.Ref.ToString()).Order().ShouldBe(["ea:1000001", "ea:2000001"]);
        fc.Title.ShouldBe("Sample FC"); // uninstall DisplayName wins over the manifest title
        fc.InstallDirectory.ShouldBe(@"C:\Program Files\EA Games\Sample FC");
        fc.Launch.ShouldBe(new LaunchSpec(LaunchKind.Uri, "origin2://game/launch?offerIds=1000001"));
        fc.Extra["contentId"].ShouldBe("1000001");
    }

    [Fact]
    public async Task PublisherFallback_FindsOlderInstalls()
    {
        Arrange();

        var legacy = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "2000001");

        legacy.Title.ShouldBe("Sample Legacy");
        legacy.InstallDirectory.ShouldBe(@"D:\Origin Games\Sample Legacy");
    }

    [Fact]
    public async Task HostileManifest_IsRejected_AndReportedAsWarning()
    {
        Arrange();

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Partial);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("Hostile Manifest"));
        result.Entries.ShouldNotContain(e => e.Title == "Hostile Manifest");
    }

    [Fact]
    public void Parser_RefusesDtd_SoExternalEntitiesAreNeverRead()
    {
        Should.Throw<XmlException>(() => EaProvider.ParseInstallerData(Fixtures.Read("ea/installerdata_xxe.xml")));
    }

    [Fact]
    public void Parser_ReadsBothLayouts()
    {
        EaProvider.ParseInstallerData(Fixtures.Read("ea/installerdata_dip4.xml"))
            .ShouldBe(new EaProvider.InstallerData("1000001", "Sample FC (manifest title)"));
        EaProvider.ParseInstallerData(Fixtures.Read("ea/installerdata_legacy.xml"))
            .ShouldBe(new EaProvider.InstallerData("2000001", "Sample Legacy (manifest title)"));
    }

    [Fact]
    public async Task EaAppEntryAndUnrelatedPrograms_AreIgnored()
    {
        Arrange();

        var titles = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Select(e => e.Title).ToList();

        titles.ShouldNotContain("EA app");
        titles.ShouldNotContain("Unrelated Tool");
    }

    private EaProvider Provider() => new(_registry, _fs, new FakeKnownFolders());

    private void Arrange()
    {
        _registry.LoadFixture("ea/registry.json");
        _fs.AddFile(EaApp, "MZ");
        _fs.AddFixture(@"C:\Program Files\EA Games\Sample FC\__Installer\installerdata.xml", "ea/installerdata_dip4.xml");
        _fs.AddFixture(@"D:\Origin Games\Sample Legacy\__Installer\installerdata.xml", "ea/installerdata_legacy.xml");
        _fs.AddFixture(@"C:\Program Files\EA Games\Hostile\__Installer\installerdata.xml", "ea/installerdata_xxe.xml");
        _fs.AddDirectory(@"C:\Tools\Unrelated");
    }
}
