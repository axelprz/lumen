// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumen.Providers.Steam;

public enum SteamAccountError
{
    None,

    /// <summary>The QR session expired or was cancelled; start a new one.</summary>
    SessionExpired,

    /// <summary>The refresh/access token was rejected: the user must sign in again.</summary>
    Unauthorized,
    RateLimited,
    Network,
    Unexpected,
}

/// <summary>A QR sign-in in progress: show <see cref="ChallengeUrl"/> as a QR code and poll every <see cref="Interval"/>.</summary>
public sealed record SteamQrSession(string ClientId, string RequestId, string ChallengeUrl, TimeSpan Interval);

/// <summary>One poll: still waiting (maybe with a new QR), approved (tokens), or an error.</summary>
public sealed record SteamQrPoll(
    SteamAccountError Error,
    string? NewChallengeUrl = null,
    string? RefreshToken = null,
    string? AccessToken = null,
    string? AccountName = null,
    string? Detail = null)
{
    public bool IsApproved => Error == SteamAccountError.None && RefreshToken is not null;
}

/// <summary>A game another family member shares (<c>IFamilyGroupsService/GetSharedLibraryApps</c>).</summary>
public sealed record SteamSharedGame(string AppId, string Name, int OwnerCount, int PlaytimeMinutes, long LastPlayedUnix);

public sealed record SteamApiResult<T>(SteamAccountError Error, T? Value, string? Detail = null);

/// <summary>
/// Steam sign-in by QR (<c>IAuthenticationService</c>) and the family library (<c>IFamilyGroupsService</c>).
/// These services are not officially documented by Valve: requests follow the public method list
/// (steamapi.xpaw.me) and the input_json form. Tokens travel only to api.steampowered.com; the access token goes in
/// the query string because Steam requires it there, so — like <see cref="SteamWebApi"/> — this class uses a plain
/// <see cref="HttpClient"/>, never logs URLs, and its errors carry only status codes.
/// </summary>
public sealed class SteamAccountApi(HttpClient http)
{
    private const string Base = SteamWebApi.BaseAddress;

    /// <summary>
    /// Platform "Steam client" (1): its refresh tokens can mint new access tokens with GenerateAccessTokenForApp, so the
    /// user scans the QR once. The device name shown in the user's Steam account is just "Lumen", nothing about the PC.
    /// </summary>
    internal const int PlatformSteamClient = 1;

    internal const string DeviceName = "Lumen";

    public async Task<SteamApiResult<SteamQrSession>> BeginQrAsync(CancellationToken ct)
    {
        var input = new JsonObject
        {
            ["device_friendly_name"] = DeviceName,
            ["platform_type"] = PlatformSteamClient,
            ["device_details"] = new JsonObject { ["device_friendly_name"] = DeviceName, ["platform_type"] = PlatformSteamClient },
        };
        var (error, json, detail) = await PostAsync("IAuthenticationService/BeginAuthSessionViaQR/v1/", input, accessToken: null, ct).ConfigureAwait(false);
        return error != SteamAccountError.None ? new(error, null, detail) : ParseBeginQr(json!);
    }

    public async Task<SteamQrPoll> PollAsync(SteamQrSession session, CancellationToken ct)
    {
        var input = new JsonObject { ["client_id"] = session.ClientId, ["request_id"] = session.RequestId };
        var (error, json, detail) = await PostAsync("IAuthenticationService/PollAuthSessionStatus/v1/", input, accessToken: null, ct).ConfigureAwait(false);
        return error != SteamAccountError.None ? new SteamQrPoll(error, Detail: detail) : ParsePoll(json!);
    }

    /// <summary>A fresh access token (~24 h) from the stored refresh token.</summary>
    public async Task<SteamApiResult<string>> RefreshAccessTokenAsync(string refreshToken, string steamId, CancellationToken ct)
    {
        var input = new JsonObject { ["refresh_token"] = refreshToken, ["steamid"] = steamId };
        var (error, json, detail) = await PostAsync("IAuthenticationService/GenerateAccessTokenForApp/v1/", input, accessToken: null, ct).ConfigureAwait(false);
        if (error != SteamAccountError.None)
        {
            return new(error, null, detail);
        }

        var token = Response(json!)?["access_token"]?.GetValue<string>();
        return string.IsNullOrEmpty(token) ? new(SteamAccountError.Unauthorized, null, "no access_token") : new(SteamAccountError.None, token);
    }

    /// <summary>
    /// Asks Steam to retire the refresh token immediately (Disconnect), so it can't mint access tokens anymore. Best
    /// effort: the caller deletes it locally either way and tells the user when Steam didn't confirm.
    /// </summary>
    public async Task<SteamAccountError> RevokeAsync(string refreshToken, string accessToken, CancellationToken ct)
    {
        // revoke_action 2 = Permanent ("log out and forget this device").
        var input = new JsonObject { ["token"] = refreshToken, ["revoke_action"] = 2 };
        var (error, _, _) = await PostAsync("IAuthenticationService/RevokeToken/v1/", input, accessToken, ct).ConfigureAwait(false);
        return error;
    }

    /// <returns>The family group id, or null (Value) when the account isn't in a family.</returns>
    public async Task<SteamApiResult<string?>> GetFamilyGroupAsync(string accessToken, string steamId, CancellationToken ct)
    {
        var (error, json, detail) = await GetAsync($"IFamilyGroupsService/GetFamilyGroupForUser/v1/?access_token={Uri.EscapeDataString(accessToken)}&steamid={steamId}", ct).ConfigureAwait(false);
        return error != SteamAccountError.None ? new(error, null, detail) : new(SteamAccountError.None, ParseFamilyGroup(json!));
    }

    public async Task<SteamApiResult<IReadOnlyList<SteamSharedGame>>> GetSharedGamesAsync(string accessToken, string steamId, string familyGroupId, CancellationToken ct)
    {
        var (error, json, detail) = await GetAsync(
            $"IFamilyGroupsService/GetSharedLibraryApps/v1/?access_token={Uri.EscapeDataString(accessToken)}&steamid={steamId}" +
            $"&family_groupid={familyGroupId}&include_own=false&include_excluded=false&include_free=false&include_non_games=false",
            ct).ConfigureAwait(false);
        return error != SteamAccountError.None ? new(error, null, detail) : new(SteamAccountError.None, ParseSharedGames(json!));
    }

    internal static SteamApiResult<SteamQrSession> ParseBeginQr(string json)
    {
        var r = Response(json);
        var clientId = Text(r?["client_id"]);
        var requestId = Text(r?["request_id"]);
        var url = Text(r?["challenge_url"]);
        if (clientId is null || requestId is null || url is null || !url.StartsWith("https://", StringComparison.Ordinal))
        {
            return new(SteamAccountError.Unexpected, null, "incomplete QR session");
        }

        var interval = r?["interval"] is JsonValue v && v.TryGetValue(out double seconds) && seconds is > 0 and < 60 ? seconds : 5;
        return new(SteamAccountError.None, new SteamQrSession(clientId, requestId, url, TimeSpan.FromSeconds(interval)));
    }

    internal static SteamQrPoll ParsePoll(string json)
    {
        var r = Response(json);
        var refresh = Text(r?["refresh_token"]);
        return refresh is not null
            ? new SteamQrPoll(SteamAccountError.None, RefreshToken: refresh, AccessToken: Text(r?["access_token"]), AccountName: Text(r?["account_name"]))
            : new SteamQrPoll(SteamAccountError.None, NewChallengeUrl: Text(r?["new_challenge_url"]));
    }

    internal static string? ParseFamilyGroup(string json)
    {
        var r = Response(json);
        if (r?["is_not_member_of_any_group"] is JsonValue member && member.TryGetValue(out bool notMember) && notMember)
        {
            return null;
        }

        var id = Text(r?["family_groupid"]);
        return id is null or "0" || !id.All(char.IsAsciiDigit) ? null : id;
    }

    /// <summary>Shareable games only: excluded ones (exclude_reason ≠ 0), non-games (app_type ≠ 1) and nameless apps are dropped.</summary>
    internal static IReadOnlyList<SteamSharedGame> ParseSharedGames(string json)
    {
        if (Response(json)?["apps"] is not JsonArray apps)
        {
            return [];
        }

        var games = new List<SteamSharedGame>();
        foreach (var app in apps.OfType<JsonObject>())
        {
            var appId = Text(app["appid"]);
            var name = Text(app["name"])?.Trim();
            if (appId is null || !appId.All(char.IsAsciiDigit) || string.IsNullOrEmpty(name)
                || Number(app["exclude_reason"]) is not (null or 0) || Number(app["app_type"]) is not (null or 1))
            {
                continue;
            }

            var owners = app["owner_steamids"] is JsonArray list ? list.Count : 0;
            games.Add(new SteamSharedGame(appId, name, owners, (int)(Number(app["rt_playtime"]) ?? 0), Number(app["rt_last_played"]) ?? 0));
        }

        return games;
    }

    private async Task<(SteamAccountError Error, string? Json, string? Detail)> PostAsync(string path, JsonObject input, string? accessToken, CancellationToken ct)
    {
        var query = accessToken is null ? string.Empty : "?access_token=" + Uri.EscapeDataString(accessToken);
        using var content = new FormUrlEncodedContent([new("input_json", input.ToJsonString())]);
        return await SendAsync(() => http.PostAsync(new Uri(Base + path + query), content, ct), ct).ConfigureAwait(false);
    }

    private Task<(SteamAccountError Error, string? Json, string? Detail)> GetAsync(string pathAndQuery, CancellationToken ct) =>
        SendAsync(() => http.GetAsync(new Uri(Base + pathAndQuery), HttpCompletionOption.ResponseHeadersRead, ct), ct);

    private static async Task<(SteamAccountError Error, string? Json, string? Detail)> SendAsync(Func<Task<HttpResponseMessage>> send, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await send().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (SteamAccountError.Network, null, ex.HttpRequestError.ToString());
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (SteamAccountError.Network, null, "Timeout");
        }

        using (response)
        {
            var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return (SteamAccountError.Unauthorized, null, status);
                case HttpStatusCode.TooManyRequests:
                    return (SteamAccountError.RateLimited, null, status);
                case var _ when !response.IsSuccessStatusCode:
                    return (SteamAccountError.Unexpected, null, status);
            }

            // Steam reports call-level failures in the x-eresult header with HTTP 200: 1 = OK.
            var eresult = response.Headers.TryGetValues("x-eresult", out var values) ? values.FirstOrDefault() : null;
            if (eresult is not null and not "1")
            {
                return (eresult switch
                {
                    "9" or "27" or "29" => SteamAccountError.SessionExpired, // file not found / expired / duplicate request
                    "5" or "15" or "21" => SteamAccountError.Unauthorized,  // invalid password / access denied / not logged on
                    "84" => SteamAccountError.RateLimited,
                    _ => SteamAccountError.Unexpected,
                }, null, "eresult " + eresult);
            }

            return (SteamAccountError.None, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null);
        }
    }

    private static JsonNode? Response(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["response"];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Steam sends 64-bit ids as strings and small numbers as numbers; accept both.
    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) => string.IsNullOrEmpty(s) ? null : s,
        JsonValue v when v.TryGetValue(out long l) => l.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static long? Number(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out long l) => l,
        JsonValue v when v.TryGetValue(out string? s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}

/// <summary>The public claims of a Steam token: "sub" (SteamID64) and "exp" (expiry).</summary>
public sealed record SteamTokenClaims(string? SteamId, DateTimeOffset? Expires);

/// <summary>Reads the public part of a Steam token (a JWT). Nothing secret is derived; the signature isn't checked.</summary>
public static class SteamTokens
{
    public static SteamTokenClaims? Read(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            var claims = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            var sub = claims?["sub"]?.GetValue<string>();
            var exp = claims?["exp"] is JsonValue e && e.TryGetValue(out long seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : (DateTimeOffset?)null;
            return new SteamTokenClaims(SteamWebApi.IsValidSteamId64(sub) ? sub : null, exp);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }
}
