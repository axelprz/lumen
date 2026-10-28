// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Artwork;
using Lumen.Core.Library;

namespace Lumen.App.ViewModels;

/// <summary>What a tile shows: a portrait cover, or (apps, games without one) the icon centered.</summary>
public sealed record TileArt(string Path, bool IsIcon)
{
    /// <summary>The cover when there is one, else the icon; null when neither exists. Never throws (decoration).</summary>
    public static async Task<TileArt?> LoadAsync(ArtworkService artwork, LibraryItem item)
    {
        try
        {
            if (await artwork.GetForItemAsync(item, ArtworkKind.Cover, CancellationToken.None) is { } cover)
            {
                return new TileArt(cover, IsIcon: false);
            }

            return await artwork.GetForItemAsync(item, ArtworkKind.Icon, CancellationToken.None) is { } icon
                ? new TileArt(icon, IsIcon: true)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
