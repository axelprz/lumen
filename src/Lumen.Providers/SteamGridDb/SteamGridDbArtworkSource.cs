// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Providers.Steam;

namespace Lumen.Providers.SteamGridDb;

/// <summary>
/// Artwork for any entry from SteamGridDB when the user saved a key. Finds the SteamGridDB game by Steam app id
/// when possible, otherwise by title (only accepting a result whose normalized title matches, to avoid wrong
/// art). The game id — or "not found" — is cached so the search runs once per item. Without a key, or when
/// SteamGridDB can't answer, it says "unavailable" so no miss is remembered.
/// </summary>
public sealed class SteamGridDbArtworkSource(
    ISecretStore secrets,
    SteamGridDbClient client,
    IArtworkLookupCache lookups,
    TitleNormalizer normalizer,
    TimeProvider? time = null) : IArtworkSource
{
    /// <summary>A remembered "not found" is retried after this long (SteamGridDB grows constantly).</summary>
    public static readonly TimeSpan NotFoundRetryAfter = TimeSpan.FromDays(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public bool CanHandle(LibraryEntry entry) => true;

    public async Task<ArtworkCandidates> GetCandidatesAsync(LibraryEntry entry, ArtworkKind kind, CancellationToken ct)
    {
        var apiKey = await secrets.GetAsync(SecretNames.SteamGridDbApiKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return ArtworkCandidates.Unavailable;
        }

        var (gameId, available) = await FindGameIdAsync(apiKey, entry, ct).ConfigureAwait(false);
        if (!available)
        {
            return ArtworkCandidates.Unavailable;
        }

        if (gameId is null)
        {
            return ArtworkCandidates.None;
        }

        var images = await client.GetImagesAsync(apiKey, gameId.Value, kind, ct).ConfigureAwait(false);
        return images.Error switch
        {
            SgdbError.None => new ArtworkCandidates(images.Value!, Definitive: true),
            SgdbError.NotFound => ArtworkCandidates.None,
            _ => ArtworkCandidates.Unavailable,
        };
    }

    /// <returns>The game id (null = not on SteamGridDB) and whether SteamGridDB could answer at all.</returns>
    internal async Task<(long? Id, bool Available)> FindGameIdAsync(string apiKey, LibraryEntry entry, CancellationToken ct)
    {
        var cacheKey = "sgdb:" + entry.Ref;
        var cached = await lookups.GetAsync(cacheKey, ct).ConfigureAwait(false);
        if (cached is not null && (cached.Id is not null || _time.GetUtcNow() - cached.UpdatedAt < NotFoundRetryAfter))
        {
            return (cached.Id, true);
        }

        if (entry.Ref.Source == SteamProvider.SourceId)
        {
            var bySteam = await client.GetGameBySteamAppIdAsync(apiKey, entry.Ref.ExternalId, ct).ConfigureAwait(false);
            if (bySteam.Error == SgdbError.None)
            {
                await lookups.SetAsync(cacheKey, bySteam.Value!.Id, ct).ConfigureAwait(false);
                return (bySteam.Value.Id, true);
            }

            if (bySteam.Error != SgdbError.NotFound)
            {
                return (null, false);
            }
        }

        // The full title first; stores often add a subtitle SteamGridDB doesn't have ("Home - A Unique Horror
        // Adventure" is "Home" there), so then the part before it. Only exact (normalized) name matches count.
        long? match = null;
        foreach (var title in new[] { entry.Title, MainTitle(entry.Title) }.OfType<string>())
        {
            var search = await client.SearchAsync(apiKey, title, ct).ConfigureAwait(false);
            if (search.Error is not (SgdbError.None or SgdbError.NotFound))
            {
                return (null, false);
            }

            var wanted = normalizer.Normalize(title);
            match = search.Value?.FirstOrDefault(g => normalizer.Normalize(g.Name) == wanted)?.Id;
            if (match is not null)
            {
                break;
            }
        }

        await lookups.SetAsync(cacheKey, match, ct).ConfigureAwait(false);
        return (match, true);
    }

    private static readonly string[] SubtitleSeparators = [" - ", " – ", " — ", ": "];

    /// <summary>The title before its first subtitle separator, or null when there is none (or nothing before it).</summary>
    internal static string? MainTitle(string title)
    {
        var cut = SubtitleSeparators
            .Select(s => title.IndexOf(s, StringComparison.Ordinal))
            .Where(i => i > 0)
            .DefaultIfEmpty(-1)
            .Min();
        return cut > 0 && title[..cut].Trim() is { Length: >= 2 } main ? main : null;
    }
}
