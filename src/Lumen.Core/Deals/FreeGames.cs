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
    /// only accepted for its home page and its <c>/redirect?dealID=…</c> link, which opens the offer in the store.
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

        return AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\?dealID=[A-Za-z0-9%]{10,120}$")]
    private static partial System.Text.RegularExpressions.Regex CheapSharkQuery();
}
