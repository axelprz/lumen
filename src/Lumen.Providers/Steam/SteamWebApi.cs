// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lumen.Providers.Steam;

public sealed record SteamOwnedGame(string AppId, string Name, int PlaytimeMinutes, long LastPlayedUnix);

/// <summary>A public Steam profile: its SteamID64 and display name.</summary>
public sealed record SteamProfile(string SteamId, string Name);

public enum SteamApiError
{
    None,

    /// <summary>401/403: the key is wrong or revoked (for the friend list: also a private friend list).</summary>
    InvalidKey,

    /// <summary>The profile's game details (or friend list) are private, so Steam returns nothing.</summary>
    PrivateProfile,
    RateLimited,
    Network,
    NotFound,
    Unexpected,
}

public sealed record SteamOwnedGamesResult(SteamApiError Error, IReadOnlyList<SteamOwnedGame> Games, string? Detail = null);

public sealed record SteamProfilesResult(SteamApiError Error, IReadOnlyList<SteamProfile> Profiles, string? Detail = null);

/// <summary>
/// The official, documented Steam Web API, used only with the user's own key (no sign-in, no tokens):
/// <c>IPlayerService/GetOwnedGames</c>, <c>ISteamUser/GetFriendList</c>, <c>ISteamUser/GetPlayerSummaries</c> and
/// <c>ISteamUser/ResolveVanityURL</c>. These only return what the profiles make public. Steam only accepts the key as
/// a query parameter, so this class never logs URLs, and its errors carry only status codes, never the request URI.
/// It uses a plain <see cref="HttpClient"/> (no IHttpClientFactory logging handler that would log URLs).
/// </summary>
public sealed partial class SteamWebApi(HttpClient http)
{
    public const string BaseAddress = "https://api.steampowered.com/";

    /// <param name="includeFreeGames">Free-to-play games the account has played; left out for family members (anyone can get those).</param>
    public async Task<SteamOwnedGamesResult> GetOwnedGamesAsync(string apiKey, string steamId64, CancellationToken ct, bool includeFreeGames = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        RequireSteamId(steamId64);

        var (error, body, detail) = await GetAsync(
            $"IPlayerService/GetOwnedGames/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={steamId64}&include_appinfo=1&include_played_free_games={(includeFreeGames ? 1 : 0)}&format=json",
            ct).ConfigureAwait(false);
        return error == SteamApiError.None ? ParseOwnedGames(body!) : new(error, [], detail);
    }

    /// <summary>The account's friends (ids only). Steam answers 401 when the friend list isn't public.</summary>
    public async Task<SteamProfilesResult> GetFriendIdsAsync(string apiKey, string steamId64, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        RequireSteamId(steamId64);

        var (error, body, detail) = await GetAsync(
            $"ISteamUser/GetFriendList/v1/?key={Uri.EscapeDataString(apiKey)}&steamid={steamId64}&relationship=friend&format=json",
            ct).ConfigureAwait(false);
        return error switch
        {
            SteamApiError.None => ParseFriendList(body!),
            SteamApiError.InvalidKey => new(SteamApiError.PrivateProfile, [], detail), // 401 here usually means a private friend list
            _ => new(error, [], detail),
        };
    }

    /// <summary>Display names for up to 100 SteamID64s (profiles that don't exist are left out).</summary>
    public async Task<SteamProfilesResult> GetProfilesAsync(string apiKey, IReadOnlyCollection<string> steamIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(steamIds);
        if (steamIds.Count == 0)
        {
            return new(SteamApiError.None, []);
        }

        if (steamIds.Count > 100 || !steamIds.All(IsValidSteamId64))
        {
            throw new ArgumentException("Up to 100 SteamID64s.", nameof(steamIds));
        }

        var (error, body, detail) = await GetAsync(
            $"ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(apiKey)}&steamids={string.Join(',', steamIds)}&format=json",
            ct).ConfigureAwait(false);
        return error == SteamApiError.None ? ParseProfiles(body!) : new(error, [], detail);
    }

    /// <summary>The SteamID64 behind a custom profile name (<c>steamcommunity.com/id/{name}</c>), or null.</summary>
    public async Task<(SteamApiError Error, string? SteamId)> ResolveVanityAsync(string apiKey, string vanityName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (!VanityNamePattern().IsMatch(vanityName))
        {
            return (SteamApiError.NotFound, null);
        }

        var (error, body, _) = await GetAsync(
            $"ISteamUser/ResolveVanityURL/v1/?key={Uri.EscapeDataString(apiKey)}&vanityurl={Uri.EscapeDataString(vanityName)}&format=json",
            ct).ConfigureAwait(false);
        if (error != SteamApiError.None)
        {
            return (error, null);
        }

        var id = ParseVanity(body!);
        return id is null ? (SteamApiError.NotFound, null) : (SteamApiError.None, id);
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

    public static SteamProfilesResult ParseFriendList(string json)
    {
        try
        {
            var friends = JsonSerializer.Deserialize<FriendListEnvelope>(json)?.FriendsList?.Friends;
            if (friends is null)
            {
                return new(SteamApiError.PrivateProfile, []);
            }

            var ids = friends.Select(f => f.SteamId).Where(IsValidSteamId64).Distinct(StringComparer.Ordinal)
                .Select(id => new SteamProfile(id!, id!)).ToList();
            return new(SteamApiError.None, ids);
        }
        catch (JsonException ex)
        {
            return new(SteamApiError.Unexpected, [], "Invalid JSON: " + ex.Message);
        }
    }

    public static SteamProfilesResult ParseProfiles(string json)
    {
        try
        {
            var players = JsonSerializer.Deserialize<SummariesEnvelope>(json)?.Response?.Players ?? [];
            var profiles = players
                .Where(p => IsValidSteamId64(p.SteamId))
                .Select(p => new SteamProfile(p.SteamId!, string.IsNullOrWhiteSpace(p.PersonaName) ? p.SteamId! : p.PersonaName.Trim()))
                .ToList();
            return new(SteamApiError.None, profiles);
        }
        catch (JsonException ex)
        {
            return new(SteamApiError.Unexpected, [], "Invalid JSON: " + ex.Message);
        }
    }

    public static string? ParseVanity(string json)
    {
        try
        {
            var response = JsonSerializer.Deserialize<VanityEnvelope>(json)?.Response;
            return response is { Success: 1 } && IsValidSteamId64(response.SteamId) ? response.SteamId : null;
        }
        catch (JsonException)
        {
            return null;
        }
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

    /// <summary>The custom name in a <c>steamcommunity.com/id/{name}</c> URL, or null.</summary>
    public static string? ExtractVanityName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var match = VanityUrlPattern().Match(input.Trim());
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static void RequireSteamId(string steamId64)
    {
        if (!IsValidSteamId64(steamId64))
        {
            throw new ArgumentException("Not a SteamID64.", nameof(steamId64));
        }
    }

    private async Task<(SteamApiError Error, string? Body, string? Detail)> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri(BaseAddress + pathAndQuery), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // HttpRequestException messages describe the connection problem, not the URI.
            return (SteamApiError.Network, null, ex.HttpRequestError.ToString());
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (SteamApiError.Network, null, "Timeout");
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return (SteamApiError.InvalidKey, null, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                case HttpStatusCode.TooManyRequests:
                    return (SteamApiError.RateLimited, null, "429");
                case var code when !response.IsSuccessStatusCode:
                    return (SteamApiError.Unexpected, null, ((int)code).ToString(CultureInfo.InvariantCulture));
            }

            return (SteamApiError.None, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null);
        }
    }

    [GeneratedRegex(@"^7656119\d{10}$")]
    private static partial Regex SteamId64Pattern();

    [GeneratedRegex(@"steamcommunity\.com/profiles/(?<id>7656119\d{10})(?:/|$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileUrlPattern();

    [GeneratedRegex(@"steamcommunity\.com/id/(?<name>[A-Za-z0-9_-]{2,32})(?:/|$|\?)", RegexOptions.IgnoreCase)]
    private static partial Regex VanityUrlPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{2,32}$")]
    private static partial Regex VanityNamePattern();

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

    private sealed class FriendListEnvelope
    {
        [JsonPropertyName("friendslist")]
        public FriendList? FriendsList { get; init; }
    }

    private sealed class FriendList
    {
        [JsonPropertyName("friends")]
        public List<FriendDto>? Friends { get; init; }
    }

    private sealed class FriendDto
    {
        [JsonPropertyName("steamid")]
        public string? SteamId { get; init; }
    }

    private sealed class SummariesEnvelope
    {
        [JsonPropertyName("response")]
        public SummariesResponse? Response { get; init; }
    }

    private sealed class SummariesResponse
    {
        [JsonPropertyName("players")]
        public List<PlayerDto>? Players { get; init; }
    }

    private sealed class PlayerDto
    {
        [JsonPropertyName("steamid")]
        public string? SteamId { get; init; }

        [JsonPropertyName("personaname")]
        public string? PersonaName { get; init; }
    }

    private sealed class VanityEnvelope
    {
        [JsonPropertyName("response")]
        public VanityResponse? Response { get; init; }
    }

    private sealed class VanityResponse
    {
        [JsonPropertyName("steamid")]
        public string? SteamId { get; init; }

        [JsonPropertyName("success")]
        public int Success { get; init; }
    }
}
