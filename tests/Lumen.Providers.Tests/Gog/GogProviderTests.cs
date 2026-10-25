// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Providers.Gog;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Gog;

// VERIFY: the registry snapshot follows the layout documented for GOG installers/Galaxy; not yet compared
// with a real GOG install.
public sealed class GogProviderTests
{
    private const string Galaxy = @"C:\Program Files (x86)\GOG Galaxy\GalaxyClient.exe";

    private readonly FakeRegistry _registry = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task NothingInstalled_IsNotAvailable()
    {
        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotAvailable);
    }

    [Fact]
    public async Task ListsInstalledGames_SkipsDlcLeftoversAndIncompleteKeys()
    {
        Arrange(withGalaxy: true);

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Entries.Select(e => e.Ref.ToString()).Order().ShouldBe(["gog:1000000001", "gog:1000000005"]);
        result.Status.ShouldBe(ProviderStatus.Partial); // the key without a name is a warning
        result.Diagnostics.ShouldContain(d => d.Severity == DiagnosticSeverity.Info && d.Message.Contains("Uninstalled Leftover"));
    }

    [Fact]
    public async Task WithGalaxy_LaunchesThroughGalaxy()
    {
        Arrange(withGalaxy: true);

        var game = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "1000000001");

        game.Title.ShouldBe("Retro Sample");
        game.InstallDirectory.ShouldBe(@"C:\GOG Games\Retro Sample");
        game.Launch.ShouldBe(new LaunchSpec(
            LaunchKind.ProcessWithArgs,
            Galaxy,
            @"/command=runGame /gameId=1000000001 /path=""C:\GOG Games\Retro Sample""",
            @"C:\Program Files (x86)\GOG Galaxy"));
    }

    [Fact]
    public async Task WithoutGalaxy_RunsTheExeWithItsParameters()
    {
        Arrange(withGalaxy: false);

        var entries = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries;
        var retro = entries.Single(e => e.Ref.ExternalId == "1000000001");
        var adventure = entries.Single(e => e.Ref.ExternalId == "1000000005");

        retro.Launch.ShouldBe(new LaunchSpec(LaunchKind.Executable, @"C:\GOG Games\Retro Sample\retro.exe", "-fullscreen", @"C:\GOG Games\Retro Sample\bin"));
        adventure.InstallDirectory.ShouldBe(@"D:\Games\GOG\Adventure Sample");
        adventure.Launch.ShouldBe(new LaunchSpec(LaunchKind.Executable, @"D:\Games\GOG\Adventure Sample\adventure.exe", null, @"D:\Games\GOG\Adventure Sample"));
    }

    [Fact]
    public async Task GalaxyRegisteredButMissing_FallsBackToExe()
    {
        Arrange(withGalaxy: false);
        _registry.LoadFixture("gog/registry.json"); // registry still points at Galaxy, but the exe isn't there

        var retro = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Ref.ExternalId == "1000000001");

        retro.Launch!.Kind.ShouldBe(LaunchKind.Executable);
    }

    [Fact]
    public async Task OwnedScan_NeedsSignIn()
    {
        (await Provider().ScanOwnedAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.NotConfigured);
    }

    private GogProvider Provider() => new(_registry, _fs);

    private void Arrange(bool withGalaxy)
    {
        _registry.LoadFixture("gog/registry.json");
        if (withGalaxy)
        {
            _fs.AddFile(Galaxy, "MZ");
        }
        else
        {
            _registry.Set(Lumen.Core.Platform.RegistryHive.LocalMachine, GogProvider.GalaxyPathsKey, "client", string.Empty);
        }

        _fs.AddDirectory(@"C:\GOG Games\Retro Sample");
        _fs.AddDirectory(@"D:\Games\GOG\Adventure Sample");
        _fs.AddDirectory(@"C:\GOG Games\No Name");
    }
}
