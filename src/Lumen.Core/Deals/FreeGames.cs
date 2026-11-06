// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Deals;

/// <summary>One free game (or giveaway) as a service reported it.</summary>
/// <param name="Source">Which service said so ("epic").</param>
/// <param name="Store">Where to claim it, as the service names it ("Epic Games Store", "Steam, PC").</param>
/// <param name="Url">The page to open in the user's browser; always one that <see cref="DealUrls.IsSafeToOpen"/> accepts.</param>
/// <param name="StartsAt">Set for games that become free later; null when free now.</param>
/// <param name="EndsAt">When it stops being free; null when the service gives no date.</param>
/// <param name="Worth">What it normally costs ("$19.99"), when the service says.</param>
public sealed record FreeGame(string Source, string Title, string Store, string Url, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Worth)
{
    public bool IsUpcoming => StartsAt is not null;
}

/// <summary>What one source answered; a source that fails returns <see cref="Failed"/> instead of throwing.</summary>
public sealed record FreeGamesResult(bool Ok, IReadOnlyList<FreeGame> Games)
{
    public static FreeGamesResult Failed { get; } = new(false, []);
}

/// <summary>A service that lists free games. Read-only, no account, nothing about the user is sent.</summary>
public interface IFreeGamesSource
{
    string Id { get; }

    Task<FreeGamesResult> GetAsync(CancellationToken ct);
}

/// <summary>Links that come from a service's answer are untrusted: only these may be opened.</summary>
public static partial class DealUrls
{
    private static readonly string[] AllowedHosts = ["store.epicgames.com", "steamcito.com.ar"];

    /// <summary>
    /// An <c>https</c> address on one of the hosts Lumen links to (never a script, file or other scheme). CheapShark is
    /// only accepted for its home page and its <c>/redirect?dealID=…</c> link, which opens the offer in the store, and Steam's
    /// store only for a game's page (<c>/app/&lt;id&gt;/</c>).
    /// </summary>
    public static bool IsSafeToOpen(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Host.Equals("www.cheapshark.com", StringComparison.OrdinalIgnoreCase))
        {
            return (uri.AbsolutePath == "/" && uri.Query.Length == 0) || (uri.AbsolutePath == "/redirect" && CheapSharkQuery().IsMatch(uri.Query));
        }

        if (uri.Host.Equals("store.steampowered.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.Query.Length == 0 && SteamAppPath().IsMatch(uri.AbsolutePath);
        }

        return AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A Steam app id: digits only, so it can be put in a store address.</summary>
    public static bool IsSteamAppId(string? id) => id is not null && SteamAppId().IsMatch(id);

    /// <summary>The app id of a Steam store link (<c>/app/&lt;id&gt;/</c>), or null.</summary>
    public static string? SteamAppIdOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals("store.steampowered.com", StringComparison.OrdinalIgnoreCase)
            && SteamAppPath().IsMatch(uri.AbsolutePath) ? uri.AbsolutePath.Trim('/').Split('/')[1] : null;

    /// <summary>
    /// The cover of a Steam game on Steam's own CDN (an approved host); null for anything that is not a plain app id.
    /// It is a plain download with nothing about the user, shown only on the Deals page.
    /// </summary>
    public static string? SteamPosterUrl(string? appId) =>
        IsSteamAppId(appId) ? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg" : null;

    [System.Text.RegularExpressions.GeneratedRegex(@"^[0-9]{1,11}$")]
    private static partial System.Text.RegularExpressions.Regex SteamAppId();

    [System.Text.RegularExpressions.GeneratedRegex(@"^/app/[0-9]{1,11}/?$")]
    private static partial System.Text.RegularExpressions.Regex SteamAppPath();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\?dealID=[A-Za-z0-9%]{10,120}$")]
    private static partial System.Text.RegularExpressions.Regex CheapSharkQuery();
}
