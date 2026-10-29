// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lumen.Providers.Steam;

public sealed record SteamOwnedGame(string AppId, string Name, int PlaytimeMinutes, long LastPlayedUnix);

public enum SteamApiError
{
    None,

    /// <summary>401/403: the key is wrong or revoked.</summary>
    InvalidKey,

    /// <summary>The profile's game details are private, so Steam returns no games.</summary>
    PrivateProfile,
    RateLimited,
    Network,
    Unexpected,
}

public sealed record SteamOwnedGamesResult(SteamApiError Error, IReadOnlyList<SteamOwnedGame> Games, string? Detail = null);

/// <summary>
/// Steam Web API: <c>IPlayerService/GetOwnedGames/v1</c>. Steam only accepts the key as a query
/// parameter, so this class never logs URLs, and its errors carry only status codes, never the request URI.
/// It uses a plain <see cref="HttpClient"/> (no IHttpClientFactory logging handler that would log URLs).
/// </summary>
public sealed partial class SteamWebApi(HttpClient http)
{
    public const string BaseAddress = "https://api.steampowered.com/";

    public Task<SteamOwnedGamesResult> GetOwnedGamesAsync(string apiKey, string steamId64, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        return GetOwnedAsync("key=" + Uri.EscapeDataString(apiKey), steamId64, ct);
    }

    /// <summary>
    /// The same list with the access token Steam's store page gives a signed-in browser (no key needed; as Playnite
    /// does). Works for private profiles too, since it is the account itself asking.
    /// </summary>
    public Task<SteamOwnedGamesResult> GetOwnedGamesWithTokenAsync(string accessToken, string steamId64, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return GetOwnedAsync("access_token=" + Uri.EscapeDataString(accessToken), steamId64, ct);
    }

    private async Task<SteamOwnedGamesResult> GetOwnedAsync(string credential, string steamId64, CancellationToken ct)
    {
        if (!IsValidSteamId64(steamId64))
        {
            throw new ArgumentException("Not a SteamID64.", nameof(steamId64));
        }

        var uri = new Uri(
            $"{BaseAddress}IPlayerService/GetOwnedGames/v1/?{credential}&steamid={steamId64}&include_appinfo=1&include_played_free_games=1&format=json");

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // HttpRequestException messages describe the connection problem, not the URI.
            return new(SteamApiError.Network, [], ex.HttpRequestError.ToString());
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(SteamApiError.Network, [], "Timeout");
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return new(SteamApiError.InvalidKey, [], ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                case HttpStatusCode.TooManyRequests:
                    return new(SteamApiError.RateLimited, [], "429");
                case var code when !response.IsSuccessStatusCode:
                    return new(SteamApiError.Unexpected, [], ((int)code).ToString(CultureInfo.InvariantCulture));
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseOwnedGames(body);
        }
    }

    /// <summary>Parses the GetOwnedGames JSON body. Steam answers <c>{"response":{}}</c> for private profiles.</summary>
    public static SteamOwnedGamesResult ParseOwnedGames(string json)
    {
        OwnedGamesEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<OwnedGamesEnvelope>(json);
        }
        catch (JsonException ex)
        {
            return new(SteamApiError.Unexpected, [], "Invalid JSON: " + ex.Message);
        }

        var games = envelope?.Response?.Games;
        if (games is null)
        {
            return new(SteamApiError.PrivateProfile, []);
        }

        var result = games
            .Where(g => g.AppId > 0)
            .Select(g => new SteamOwnedGame(
                g.AppId.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(g.Name) ? g.AppId.ToString(CultureInfo.InvariantCulture) : g.Name.Trim(),
                g.PlaytimeForever,
                g.LastPlayed))
            .ToList();
        return new(SteamApiError.None, result);
    }

    /// <summary>A SteamID64 for an individual account: 17 digits starting with 7656119.</summary>
    public static bool IsValidSteamId64(string? value) => value is not null && SteamId64Pattern().IsMatch(value);

    /// <summary>Accepts a bare SteamID64 or a <c>steamcommunity.com/profiles/{id}</c> URL. Vanity URLs are not resolved.</summary>
    public static string? ExtractSteamId64(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (IsValidSteamId64(trimmed))
        {
            return trimmed;
        }

        var match = ProfileUrlPattern().Match(trimmed);
        return match.Success ? match.Groups["id"].Value : null;
    }

    [GeneratedRegex(@"^7656119\d{10}$")]
    private static partial Regex SteamId64Pattern();

    [GeneratedRegex(@"steamcommunity\.com/profiles/(?<id>7656119\d{10})(?:/|$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileUrlPattern();

    private sealed class OwnedGamesEnvelope
    {
        [JsonPropertyName("response")]
        public OwnedGamesResponse? Response { get; init; }
    }

    private sealed class OwnedGamesResponse
    {
        [JsonPropertyName("games")]
        public List<OwnedGameDto>? Games { get; init; }
    }

    private sealed class OwnedGameDto
    {
        [JsonPropertyName("appid")]
        public long AppId { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("playtime_forever")]
        public int PlaytimeForever { get; init; }

        [JsonPropertyName("rtime_last_played")]
        public long LastPlayed { get; init; }
    }
}
