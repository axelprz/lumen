// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Providers.Tests.Fakes;
using Lumen.Providers.Xbox;

namespace Lumen.Providers.Tests.Xbox;

// Synthetic manifests following the documented AppxManifest / MicrosoftGame.config formats. The package list
// shape matches PackageManager output; GDK detection is not yet verified on a machine with a GDK game.
public sealed class XboxProviderTests
{
    private const string GdkDir = @"C:\XboxGames\Sample Racer\Content";
    private const string UwpDir = @"C:\Program Files\WindowsApps\Microsoft.MicrosoftSolitaireCollection_4.0.0.0_x64__8wekyb3d8bbwe";
    private const string AppDir = @"C:\Program Files\WindowsApps\Contoso.Notes_2.0.0.0_x64__abcdefghijklm";
    private const string GdkFamily = "Contoso.SampleRacer_abcdefghijklm";
    private const string UwpFamily = "Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe";

    private readonly FakePackageCatalog _catalog = new();
    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public async Task GdkGame_IsListed_WithShellAppsFolderLaunch()
    {
        Arrange();

        var game = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Extra["gdk"] == "true");

        game.Ref.ToString().ShouldBe($"xbox:{GdkFamily}!Game");
        game.InstallDirectory.ShouldBe(GdkDir);
        game.Launch.ShouldBe(new LaunchSpec(LaunchKind.ShellAppsFolder, $"{GdkFamily}!Game"));
    }

    [Fact]
    public async Task HiddenApps_AreSkipped_WhenChoosingTheAppId()
    {
        Arrange();

        var game = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Extra["packageFamilyName"] == GdkFamily);

        game.Extra["appId"].ShouldBe("Game"); // "Helper" has AppListEntry="none"
    }

    [Fact]
    public async Task Title_PrefersResolvedName_ThenGameConfig_NeverMsResource()
    {
        Arrange(gdkDisplayName: null);

        var game = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Extra["packageFamilyName"] == GdkFamily);

        game.Title.ShouldBe("Sample Racer"); // from MicrosoftGame.config, not "ms-resource:GameDisplayName"
    }

    [Fact]
    public async Task KnownUwpGame_IsListed_WithoutGameConfig()
    {
        Arrange();

        var solitaire = (await Provider().ScanInstalledAsync(CancellationToken.None)).Entries.Single(e => e.Extra["packageFamilyName"] == UwpFamily);

        solitaire.Title.ShouldBe("Microsoft Solitaire Collection");
        solitaire.Extra["gdk"].ShouldBe("false");
        solitaire.Launch!.Target.ShouldBe($"{UwpFamily}!App");
    }

    [Fact]
    public async Task OrdinaryAppsAndFrameworks_AreIgnored()
    {
        Arrange();
        _catalog.Packages.Add(new InstalledPackage("Microsoft.VCLibs.140.00_8wekyb3d8bbwe", "fw", "VCLibs", @"C:\fw", IsFramework: true, IsResource: false, IsStoreSigned: true));

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Entries.Count.ShouldBe(2);
        result.Entries.ShouldNotContain(e => e.Title == "Contoso Notes");
        result.Status.ShouldBe(ProviderStatus.Ok);
    }

    [Fact]
    public async Task BrokenManifest_IsAWarning()
    {
        Arrange();
        _fs.AddFile(GdkDir + @"\AppxManifest.xml", "<Package><broken");

        var result = await Provider().ScanInstalledAsync(CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Partial);
        result.Entries.Single().Extra["packageFamilyName"].ShouldBe(UwpFamily);
    }

    [Fact]
    public async Task CatalogFailure_IsReportedNotThrown()
    {
        _catalog.Throw = true;

        (await Provider().ScanInstalledAsync(CancellationToken.None)).Status.ShouldBe(ProviderStatus.Failed);
    }

    [Fact]
    public void KnownStoreGames_IncludeSolitaire()
    {
        XboxProvider.KnownStoreGames.ShouldContain(UwpFamily);
    }

    private XboxProvider Provider() => new(_catalog, _fs);

    private void Arrange(string? gdkDisplayName = "Sample Racer")
    {
        _catalog.Packages.Add(new InstalledPackage(GdkFamily, "Contoso.SampleRacer_1.0.0.0_x64__abcdefghijklm", gdkDisplayName, GdkDir, false, false, true));
        _catalog.Packages.Add(new InstalledPackage(UwpFamily, "Microsoft.MicrosoftSolitaireCollection_4.0.0.0_x64__8wekyb3d8bbwe", "Microsoft Solitaire Collection", UwpDir, false, false, true));
        _catalog.Packages.Add(new InstalledPackage("Contoso.Notes_abcdefghijklm", "Contoso.Notes_2.0.0.0_x64__abcdefghijklm", "Contoso Notes", AppDir, false, false, true));
        _fs.AddFixture(GdkDir + @"\AppxManifest.xml", "xbox/gdk/AppxManifest.xml");
        _fs.AddFixture(GdkDir + @"\MicrosoftGame.config", "xbox/gdk/MicrosoftGame.config");
        _fs.AddFixture(UwpDir + @"\AppxManifest.xml", "xbox/uwp/AppxManifest.xml");
        _fs.AddFixture(AppDir + @"\AppxManifest.xml", "xbox/app/AppxManifest.xml");
    }
}
