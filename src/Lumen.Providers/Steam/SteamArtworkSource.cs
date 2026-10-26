// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Artwork;
using Lumen.Core.Library;

namespace Lumen.Providers.Steam;

/// <summary>
/// Library artwork from Steam's CDN (an allowed host). Verified 2026-10: both hosts below serve
/// library_600x900.jpg, library_hero.jpg, logo.png and header.jpg for current games.
/// </summary>
public sealed class SteamArtworkSource : IArtworkSource
{
    internal const string PrimaryHost = "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/";
    internal const string FallbackHost = "https://cdn.cloudflare.steamstatic.com/steam/apps/";

    public bool CanHandle(LibraryEntry entry) => entry.Ref.Source == SteamProvider.SourceId && entry.Ref.ExternalId.All(char.IsAsciiDigit);

    public IReadOnlyList<Uri> Candidates(LibraryEntry entry, ArtworkKind kind)
    {
        var id = entry.Ref.ExternalId;
        IEnumerable<string> files = kind switch
        {
            // The landscape header is a last resort for very old games without library art.
            ArtworkKind.Cover => ["library_600x900.jpg", "header.jpg"],
            ArtworkKind.Hero => ["library_hero.jpg"],
            ArtworkKind.Logo => ["logo.png"],
            _ => [],
        };

        return files
            .SelectMany(file => new[] { new Uri($"{PrimaryHost}{id}/{file}"), new Uri($"{FallbackHost}{id}/{file}") })
            .ToList();
    }
}
