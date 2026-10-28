// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Providers.Apps;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Apps;

// Reuses the synthetic Xbox manifests: one plain app, one GDK game, one known Store game.
public sealed class StoreAppsProviderTests
{
    private const string AppDir = @"C:\Program Files\WindowsApps\Contoso.Notes_2.0.0.0_x64__abcdefghijklm";
    private const string GdkDir = @"C:\XboxGames\Sample Racer\Content";
    private const string UwpDir = @"C:\Program Files\WindowsApps\Microsoft.MicrosoftSolitaireCollection_4.0.0.0_x64__8wekyb3d8bbwe";

    private readonly FakePackageCatalog _catalog = new();
    private readonly InMemoryFileSystem _fs = new();

    public StoreAppsProviderTests()
    {
        _catalog.Packages.Add(new InstalledPackage("Contoso.Notes_abcdefghijklm", "Contoso.Notes_2.0.0.0_x64__abcdefghijklm", "Contoso Notes", AppDir, false, false, true));
        _catalog.Packages.Add(new InstalledPackage("Contoso.SampleRacer_abcdefghijklm", "racer", "Sample Racer", GdkDir, false, false, true));
        _catalog.Packages.Add(new InstalledPackage("Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe", "solitaire", "Solitaire", UwpDir, false, false, true));
        _catalog.Packages.Add(new InstalledPackage("Microsoft.VCLibs.140.00_8wekyb3d8bbwe", "fw", "VCLibs", @"C:\fw", IsFramework: true, IsResource: false, IsStoreSigned: true));
        _fs.AddFixture(AppDir + @"\AppxManifest.xml", "xbox/app/AppxManifest.xml");
        _fs.AddFixture(GdkDir + @"\AppxManifest.xml", "xbox/gdk/AppxManifest.xml");
        _fs.AddFixture(GdkDir + @"\MicrosoftGame.config", "xbox/gdk/MicrosoftGame.config");
        _fs.AddFixture(UwpDir + @"\AppxManifest.xml", "xbox/uwp/AppxManifest.xml");
    }

    [Fact]
    public async Task PlainApps_AreListed_GamesAndFrameworksAreNot()
    {
        var entry = (await Scan()).Entries.ShouldHaveSingleItem();

        entry.Ref.Source.ShouldBe("appsfolder");
        entry.Kind.ShouldBe(ItemKind.App);
        entry.Title.ShouldBe("Contoso Notes");
        entry.Launch!.Kind.ShouldBe(LaunchKind.ShellAppsFolder);
        entry.Launch!.Target.ShouldStartWith("Contoso.Notes_abcdefghijklm!");
    }

    [Fact]
    public async Task UnresolvedNames_AreSkipped()
    {
        _catalog.Packages[0] = _catalog.Packages[0] with { DisplayName = "ms-resource:AppName" };

        (await Scan()).Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task CatalogFailure_IsAFailedResult_NotAnException()
    {
        _catalog.Throw = true;

        (await Scan()).Status.ShouldBe(ProviderStatus.Failed);
    }

    private Task<ProviderResult> Scan() => new StoreAppsProvider(_catalog, _fs).ScanInstalledAsync(CancellationToken.None);
}
