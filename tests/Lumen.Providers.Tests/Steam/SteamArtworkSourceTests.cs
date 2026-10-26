// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Providers.Steam;

namespace Lumen.Providers.Tests.Steam;

public sealed class SteamArtworkSourceTests
{
    private static readonly LibraryEntry _game = new(new ItemRef("steam", "1000010"), ItemKind.Game, "Sample", InstallState.Installed, null, null, null, LibraryEntry.NoExtra);

    private readonly SteamArtworkSource _source = new();

    [Fact]
    public void Cover_TriesLibraryArtOnBothHosts_ThenHeader()
    {
        _source.Candidates(_game, ArtworkKind.Cover).Select(u => u.ToString()).ShouldBe(
        [
            "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1000010/library_600x900.jpg",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/1000010/library_600x900.jpg",
            "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1000010/header.jpg",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/1000010/header.jpg",
        ]);
    }

    [Theory]
    [InlineData(ArtworkKind.Hero, "library_hero.jpg")]
    [InlineData(ArtworkKind.Logo, "logo.png")]
    public void HeroAndLogo(ArtworkKind kind, string file)
    {
        _source.Candidates(_game, kind).ShouldAllBe(u => u.AbsolutePath.EndsWith("/1000010/" + file));
    }

    [Fact]
    public void OnlySteamHosts_AreUsed()
    {
        foreach (var kind in Enum.GetValues<ArtworkKind>())
        {
            _source.Candidates(_game, kind).ShouldAllBe(u => u.Scheme == "https" && u.Host.EndsWith(".steamstatic.com"));
        }
    }

    [Fact]
    public void OnlyHandlesSteamEntries()
    {
        _source.CanHandle(_game).ShouldBeTrue();
        _source.CanHandle(_game with { Ref = new ItemRef("epic", "x") }).ShouldBeFalse();
    }
}
