// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumen.Providers.Steam;

/// <summary>The token the Steam store page gives the signed-in browser (<c>webapi_token</c>), and whose it is.</summary>
public sealed record SteamWebToken(string SteamId, string AccessToken);

/// <summary>
/// A Steam sign-in on Steam's own website, in a built-in browser (as Playnite does; owner decision 2026-10-29).
/// The user signs in on store.steampowered.com like in any browser (QR or password typed into Steam's page, never
/// read by Lumen); the browser profile keeps the session. To read family games, the store page is opened in the
/// background and the token Steam puts in that page for the browser is used, in memory only. Lumen never claims to be
/// another app or device (CLAUDE.md rule 8).
/// </summary>
public interface ISteamWebSession
{
    /// <summary>True after a successful sign-in on this PC (until disconnect).</summary>
    Task<bool> IsSignedInAsync(CancellationToken ct);

    /// <summary>
    /// Opens the store page in the background and returns its token; null when Steam no longer considers this
    /// browser signed in (expired or signed out elsewhere). Never shows anything.
    /// </summary>
    Task<SteamWebToken?> GetTokenAsync(CancellationToken ct);
}

/// <summary>Reads the store page's <c>#application_config</c> attributes (the values Steam's own page script uses).</summary>
public static class SteamStorePage
{
    /// <summary>The script run on store.steampowered.com only (never on the sign-in page): reads two attributes.</summary>
    public const string ReadConfigScript =
        "(() => { const c = document.getElementById('application_config'); " +
        "return c ? JSON.stringify({ user: c.getAttribute('data-userinfo'), store: c.getAttribute('data-store_user_config') }) : null; })()";

    /// <summary>Parses the script's result (a JSON string of the two attribute values), or null when not signed in.</summary>
    public static SteamWebToken? ParseConfig(string? scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult) || scriptResult == "null")
        {
            return null;
        }

        try
        {
            // ExecuteScriptAsync returns the value JSON-encoded: a JSON string that holds our JSON object.
            var inner = JsonSerializer.Deserialize<string>(scriptResult);
            var config = inner is null ? null : JsonNode.Parse(inner);
            var user = Parse(config?["user"]?.GetValue<string>());
            var store = Parse(config?["store"]?.GetValue<string>());
            var signedIn = user?["logged_in"] is JsonValue v && v.TryGetValue(out bool loggedIn) && loggedIn;
            var steamId = user?["steamid"]?.GetValue<string>();
            var token = store?["webapi_token"]?.GetValue<string>();
            return signedIn && SteamWebApi.IsValidSteamId64(steamId) && !string.IsNullOrWhiteSpace(token)
                ? new SteamWebToken(steamId!, token!)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static JsonNode? Parse(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
}
