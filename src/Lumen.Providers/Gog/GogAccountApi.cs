// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Lumen.Providers.Gog;

public enum GogAccountError
{
    None,

    /// <summary>The code or token was rejected (wrong, used, expired): sign in again.</summary>
    Unauthorized,
    RateLimited,
    Network,
    Unexpected,
}

/// <summary>What a code or refresh exchange returns. Tokens stay inside <see cref="GogAccount"/>.</summary>
public sealed record GogSession(string AccessToken, DateTimeOffset AccessExpires, string RefreshToken, string UserId);

/// <summary>An owned game from <c>account/getFilteredProducts</c>.</summary>
public sealed record GogOwnedGame(string Id, string Title);

public sealed record GogApiResult<T>(GogAccountError Error, T? Value, string? Detail = null);

/// <summary>
/// GOG account sign-in and owned games, following the flow Heroic (gogdl) uses (unofficial; PLAN-v1 §3): the user signs
/// in on GOG's page; the browser ends on <c>embed.gog.com/on_login_success?…&amp;code=…</c> and they paste that address
/// (or the code). GOG's token endpoint takes its parameters in the query string, so — like the other account clients
/// — this uses a plain <see cref="HttpClient"/> that never logs URLs, and errors carry status / GOG error codes only.
/// GOG offers no way to revoke a sign-in from outside: Disconnect deletes it locally and it expires by itself.
/// </summary>
public sealed partial class GogAccountApi(HttpClient http)
{
    // GOG's public client id and secret for GOG Galaxy. They are not the user's credentials nor Lumen's: GOG only
    // accepts this sign-in from the Galaxy client, and these values are published in Heroic's gogdl
    // (owner-approved, 2026-10-28). The user's own sign-in is the refresh token, stored with DPAPI.
    internal const string GalaxyClientId = "46899977096215655";
    private const string GalaxyClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
    private const string RedirectUri = "https://embed.gog.com/on_login_success?origin=client";

    /// <summary>GOG's login page; after signing in the browser lands on the redirect address carrying the code.</summary>
    public static Uri SignInPage { get; } = new(
        $"https://auth.gog.com/auth?client_id={GalaxyClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}&response_type=code&layout=client2");

    /// <summary>Accepts the whole address the browser ended on, or just the code.</summary>
    public static string? ExtractCode(string? pasted)
    {
        var text = pasted?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Contains("code=", StringComparison.Ordinal))
        {
            var match = CodeParameter().Match(text);
            text = match.Success ? Uri.UnescapeDataString(match.Groups["code"].Value) : null;
        }

        return text is not null && CodeShape().IsMatch(text) ? text : null;
    }

    public Task<GogApiResult<GogSession>> ExchangeCodeAsync(string code, CancellationToken ct) =>
        TokenAsync($"grant_type=authorization_code&code={Uri.EscapeDataString(code)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}", ct);

    public Task<GogApiResult<GogSession>> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync($"grant_type=refresh_token&refresh_token={Uri.EscapeDataString(refreshToken)}", ct);

    /// <summary>The account's user name, for Settings.</summary>
    public async Task<GogApiResult<string>> GetUserNameAsync(string accessToken, CancellationToken ct)
    {
        var (error, json, detail) = await GetAsync(new Uri("https://embed.gog.com/userData.json"), accessToken, ct).ConfigureAwait(false);
        if (error != GogAccountError.None)
        {
            return new(error, null, detail);
        }

        var name = Parse(json!)?["username"] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;
        return name is null ? new(GogAccountError.Unexpected, null, "no username") : new(GogAccountError.None, name);
    }

    /// <summary>Every owned game (all pages).</summary>
    public async Task<GogApiResult<IReadOnlyList<GogOwnedGame>>> GetOwnedGamesAsync(string accessToken, CancellationToken ct)
    {
        var games = new List<GogOwnedGame>();
        for (var page = 1; page <= 200; page++) // a hard stop in case totalPages is wrong
        {
            var (error, json, detail) = await GetAsync(
                new Uri($"https://embed.gog.com/account/getFilteredProducts?mediaType=1&page={page.ToString(CultureInfo.InvariantCulture)}"), accessToken, ct).ConfigureAwait(false);
            if (error != GogAccountError.None)
            {
                return new(error, null, detail);
            }

            var (items, totalPages) = ParseProductsPage(json!);
            games.AddRange(items);
            if (page >= totalPages)
            {
                break;
            }
        }

        return new(GogAccountError.None, games.DistinctBy(g => g.Id).ToList());
    }

    internal static GogApiResult<GogSession> ParseSession(string json)
    {
        var root = Parse(json);
        if (Text(root?["error"]) is { } error)
        {
            return new(GogAccountError.Unauthorized, null, error);
        }

        var access = Text(root?["access_token"]);
        var refresh = Text(root?["refresh_token"]);
        var user = Text(root?["user_id"]);
        if (access is null || refresh is null || user is null)
        {
            return new(GogAccountError.Unexpected, null, "incomplete token response");
        }

        var seconds = root?["expires_in"] is JsonValue v && v.TryGetValue(out double s) && s > 0 ? s : 3600;
        return new(GogAccountError.None, new GogSession(access, DateTimeOffset.UtcNow.AddSeconds(seconds), refresh, user));
    }

    /// <summary>Games only (mediaType 1 already excludes movies); entries without an id or title are skipped.</summary>
    internal static (IReadOnlyList<GogOwnedGame> Games, int TotalPages) ParseProductsPage(string json)
    {
        var root = Parse(json);
        var games = new List<GogOwnedGame>();
        if (root?["products"] is JsonArray products)
        {
            foreach (var product in products.OfType<JsonObject>())
            {
                var id = Text(product["id"]);
                var title = Text(product["title"])?.Trim();
                var isGame = product["isGame"] is not JsonValue g || !g.TryGetValue(out bool flag) || flag;
                if (id is not null && id.All(char.IsAsciiDigit) && !string.IsNullOrEmpty(title) && isGame)
                {
                    games.Add(new GogOwnedGame(id, title));
                }
            }
        }

        var total = root?["totalPages"] is JsonValue t && t.TryGetValue(out int pages) && pages > 0 ? pages : 1;
        return (games, total);
    }

    private async Task<GogApiResult<GogSession>> TokenAsync(string grant, CancellationToken ct)
    {
        var uri = new Uri($"https://auth.gog.com/token?client_id={GalaxyClientId}&client_secret={GalaxyClientSecret}&{grant}");
        var (error, json, detail) = await GetAsync(uri, accessToken: null, ct, readBodyOnClientError: true).ConfigureAwait(false);
        return error != GogAccountError.None ? new(error, null, detail) : ParseSession(json!);
    }

    private async Task<(GogAccountError Error, string? Json, string? Detail)> GetAsync(Uri uri, string? accessToken, CancellationToken ct, bool readBodyOnClientError = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (GogAccountError.Network, null, ex.HttpRequestError.ToString());
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (GogAccountError.Network, null, "Timeout");
        }

        using (response)
        {
            var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            if (readBodyOnClientError && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                return (GogAccountError.None, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (GogAccountError.Unauthorized, null, status),
                HttpStatusCode.TooManyRequests => (GogAccountError.RateLimited, null, status),
                _ when !response.IsSuccessStatusCode => (GogAccountError.Unexpected, null, status),
                _ => (GogAccountError.None, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null),
            };
        }
    }

    private static JsonNode? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue(out string? s) => string.IsNullOrWhiteSpace(s) ? null : s,
        JsonValue v when v.TryGetValue(out long l) => l.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    [GeneratedRegex(@"[?&]code=(?<code>[^&#\s]+)")]
    private static partial Regex CodeParameter();

    // GOG codes are long URL-safe strings.
    [GeneratedRegex(@"^[A-Za-z0-9_\-\.]{16,1024}$")]
    private static partial Regex CodeShape();
}
