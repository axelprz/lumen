// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Providers.Epic;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Epic;

// Fixtures are synthetic but follow the real manifest format (compared with a real install, 2026-10).
public sealed class EpicProviderTests
{
    private const string DefaultManifests = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

    private readonly FakeRegistry _registry = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task NoLauncher_IsNotAvailable()
    {
        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotAvailable);
    }

    [Fact]
    public async Task ListsGamesAndApps_SkipsDlcEnginesAndIncomplete()
    {
        ArrangeAll(DefaultManifests);

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Entries.Select(e => e.Ref.ToString()).Order().ShouldBe(["epic:Old Game With Spaces", "epic:PaintToolApp", "epic:SampleQuestApp"]);
        result.Entries.Single(e => e.Ref.ExternalId == "PaintToolApp").Kind.ShouldBe(ItemKind.App);
        result.Entries.Single(e => e.Ref.ExternalId == "SampleQuestApp").Kind.ShouldBe(ItemKind.Game);
    }

    [Fact]
    public async Task Game_HasLauncherUriAndInstallLocation()
    {
        ArrangeAll(DefaultManifests);

        var game = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "SampleQuestApp");

        game.Title.ShouldBe("Sample Quest");
        game.State.ShouldBe(InstallState.Installed);
        game.InstallDirectory.ShouldBe(@"D:\Epic Games\SampleQuest");
        game.Launch.ShouldBe(new LaunchSpec(LaunchKind.Uri,
            "com.epicgames.launcher://apps/samplens%3A0000000000000000000000000000aaaa%3ASampleQuestApp?action=launch&silent=true"));
        game.Extra["namespace"].ShouldBe("samplens");
        game.Extra["launchExecutable"].ShouldBe("Binaries/Win64/SampleQuest.exe");
    }

    [Fact]
    public async Task MissingCatalogIds_FallBackToAppName_Escaped()
    {
        ArrangeAll(DefaultManifests);

        var old = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "Old Game With Spaces");

        old.Launch!.Target.ShouldBe("com.epicgames.launcher://apps/Old%20Game%20With%20Spaces?action=launch&silent=true");
    }

    [Fact]
    public async Task ForwardSlashPaths_AreNormalized()
    {
        ArrangeAll(DefaultManifests);

        var tool = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "PaintToolApp");

        tool.InstallDirectory.ShouldBe(@"C:\Program Files\Epic Games\PaintTool");
    }

    [Fact]
    public async Task BrokenManifest_IsPartial_RestStillLoads()
    {
        ArrangeAll(DefaultManifests);

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Partial);
        result.Diagnostics.ShouldContain(d => d.Message.Contains("broken.item"));
        result.Entries.Count.ShouldBe(3);
    }

    [Fact]
    public async Task CustomDataPath_FromRegistry_IsUsed()
    {
        _registry.Set(RegistryHive.LocalMachine, EpicProvider.RegistryKey, EpicProvider.RegistryValue, @"E:\EpicData\");
        _fs.AddFixture(@"E:\EpicData\Manifests\game.item", "epic/game.item");

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Entries.Single().Ref.ExternalId.ShouldBe("SampleQuestApp");
    }

    [Fact]
    public async Task OwnedScan_NeedsSignIn()
    {
        (await Provider().ScanOwnedAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotConfigured);
    }

    private EpicProvider Provider() => new(_registry, _fs, new FakeKnownFolders());

    private void ArrangeAll(string manifests)
    {
        foreach (var name in new[] { "game", "software", "dlc", "engine", "incomplete", "nocatalog", "broken" })
        {
            _fs.AddFixture($@"{manifests}\{name}.item", $"epic/{name}.item");
        }
    }
}
