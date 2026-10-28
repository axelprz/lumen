// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Providers.Apps;
using Lumen.Providers.Tests.Fakes;

namespace Lumen.Providers.Tests.Apps;

public sealed class PackageLogoSourceTests
{
    private const string Dir = @"C:\Program Files\WindowsApps\Contoso.Notes_2.0.0.0_x64__abcdefghijklm";

    private readonly InMemoryFileSystem _fs = new();

    [Fact]
    public void BestVariant_PrefersUnplated_ThenTheLargestUpTo256()
    {
        foreach (var name in new[]
        {
            "Square44x44Logo.scale-100.png",
            "Square44x44Logo.scale-400.png",
            "Square44x44Logo.targetsize-256.png",
            "Square44x44Logo.targetsize-256_altform-unplated.png",
            "Square44x44Logo.targetsize-48_altform-unplated.png",
            "Square44x44Logo.targetsize-256_altform-lightunplated.png",
            "Square44x44Logo.targetsize-256_contrast-black.png",
        })
        {
            _fs.AddFile($@"{Dir}\Assets\{name}", "x");
        }

        PackageLogoSource.BestVariant(_fs, Dir, @"Assets\Square44x44Logo.png")
            .ShouldBe($@"{Dir}\Assets\Square44x44Logo.targetsize-256_altform-unplated.png");
    }

    [Fact]
    public void BestVariant_UsesScale_WhenThereAreNoTargetSizes()
    {
        _fs.AddFile($@"{Dir}\Assets\Square150x150Logo.scale-100.png", "x");
        _fs.AddFile($@"{Dir}\Assets\Square150x150Logo.scale-200.png", "x");
        _fs.AddFile($@"{Dir}\Assets\Square150x150Logo.scale-400.png", "x");

        // 300 px and 600 px both cover 256; the smaller one is enough.
        PackageLogoSource.BestVariant(_fs, Dir, @"Assets\Square150x150Logo.png")
            .ShouldBe($@"{Dir}\Assets\Square150x150Logo.scale-200.png");
    }

    [Theory]
    [InlineData(@"..\..\Windows\evil.png")]
    [InlineData(@"C:\Windows\evil.png")]
    [InlineData("noextension")]
    public void BestVariant_StaysInsideThePackage(string logical)
    {
        PackageLogoSource.BestVariant(_fs, Dir, logical).ShouldBeNull();
    }

    [Fact]
    public async Task Image_ComesFromTheManifestsSquareLogo()
    {
        _fs.AddFixture($@"{Dir}\AppxManifest.xml", "xbox/app/AppxManifest.xml");
        _fs.AddFile($@"{Dir}\b.targetsize-256.png", "square44");
        _fs.AddFile($@"{Dir}\a.scale-200.png", "square150");
        var source = new PackageLogoSource(_fs);
        var entry = new LibraryEntry(new ItemRef("appsfolder", "Contoso.Notes_abcdefghijklm!App"), ItemKind.App, "Contoso Notes",
            InstallState.Installed, Dir, null, null, new Dictionary<string, string> { ["appId"] = "App" });

        source.CanHandle(entry, ArtworkKind.Icon).ShouldBeTrue();
        source.CanHandle(entry, ArtworkKind.Cover).ShouldBeFalse();
        System.Text.Encoding.UTF8.GetString((await source.GetImageAsync(entry, ArtworkKind.Icon, CancellationToken.None))!).ShouldBe("square44");
    }
}
