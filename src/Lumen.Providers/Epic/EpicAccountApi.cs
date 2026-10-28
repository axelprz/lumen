// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumen.Providers.Epic;

public enum EpicAccountError
{
    None,

    /// <summary>The code or token was rejected (wrong, used, expired, revoked): sign in again.</summary>
    Unauthorized,

    /// <summary>Epic wants the user to do something on its website first (accept terms, verify the account).</summary>
    ActionRequired,
    RateLimited,
    Network,
    Unexpected,
}

/// <summary>What a code or refresh exchange returns. Tokens stay inside <see cref="EpicAccount"/>.</summary>
public sealed record EpicSession(string AccessToken, DateTimeOffset AccessExpires, string RefreshToken, string AccountId, string DisplayName);

/// <summary>An owned library item (<c>library/api/public/items</c>).</summary>
public sealed record EpicLibraryRecord(string Namespace, string CatalogItemId, string AppName);

/// <summary>What the catalog says about an item: its title, whether it is a game or an app, or something to skip.</summary>
public sealed record EpicCatalogInfo(string Title, bool IsApp, bool Skip);

public sealed record EpicApiResult<T>(EpicAccountError Error, T? Value, string? Detail = null);

/// <summary>
/// Epic account sign-in and owned library, following the flow Legendary/Heroic use (unofficial; PLAN-v1 §3):
/// the user signs in on Epic's own page, which shows an authorization code they paste into Lumen; Lumen exchanges it
/// for tokens. Like the other account clients this uses a plain <see cref="HttpClient"/>, sends tokens only to Epic
/// (in the Authorization header, except the session kill, whose URL contains it and is never logged), and its
/// errors carry status and Epic error codes only.
/// </summary>
public sealed class EpicAccountApi(HttpClient http)
{
    // Epic's public client id and secret for its own launcher. They are not the user's credentials nor Lumen's: Epic
    // only accepts this sign-in from its launcher client, and these values are published in Legendary and Heroic
    // (owner-approved, 2026-10-28). The user's own sign-in is the refresh token, stored with DPAPI.
    internal const string LauncherClientId = "34a02cf8f4414e29b15921876da36f9a";
    private const string LauncherClientSecret = "daafbccc737745039dffe53d94fc76cf";

    private const string OAuthHost = "https://account-public-service-prod03.ol.epicgames.com";
    private const string LibraryHost = "https://library-service.live.use1a.on.epicgames.com";
    private const string CatalogHost = "https://catalog-public-service-prod06.ol.epicgames.com";

    /// <summary>Epic's login page; after signing in it shows a JSON page with the "authorizationCode" to paste.</summary>
    public static Uri SignInPage { get; } = new(
        "https://www.epicgames.com/id/login?redirectUrl="
        + Uri.EscapeDataString($"https://www.epicgames.com/id/api/redirect?clientId={LauncherClientId}&responseType=code"));

    /// <summary>
    /// Accepts what the user pasted: the bare code or the whole JSON page ({"authorizationCode":"…"}).
    /// Codes are 32 hexadecimal characters.
    /// </summary>
    public static string? ExtractCode(string? pasted)
    {
        var text = pasted?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.StartsWith('{'))
        {
            try
            {
                text = JsonNode.Parse(text)?["authorizationCode"]?.GetValue<string>()?.Trim();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return null;
            }
        }

        text = text?.Trim('"');
        return text is { Length: 32 } && text.All(char.IsAsciiHexDigit) ? text : null;
    }

    public Task<EpicApiResult<EpicSession>> ExchangeCodeAsync(string code, CancellationToken ct) =>
        TokenAsync([new("grant_type", "authorization_code"), new("code", code), new("token_type", "eg1")], ct);

    public Task<EpicApiResult<EpicSession>> RefreshAsync(string refreshToken, CancellationToken ct) =>
        TokenAsync([new("grant_type", "refresh_token"), new("refresh_token", refreshToken), new("token_type", "eg1")], ct);

    /// <summary>Ends the session at Epic (Disconnect). Best effort: the caller deletes the token locally either way.</summary>
    public async Task<EpicAccountError> KillSessionAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"{OAuthHost}/account/api/oauth/sessions/kill/{Uri.EscapeDataString(accessToken)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
        var (error, _, _) = await SendAsync(request, ct).ConfigureAwait(false);
        return error;
    }

    /// <summary>Every owned library record (all pages).</summary>
    public async Task<EpicApiResult<IReadOnlyList<EpicLibraryRecord>>> GetLibraryAsync(string accessToken, CancellationToken ct)
    {
        var records = new List<EpicLibraryRecord>();
        string? cursor = null;
        for (var page = 0; page < 100; page++) // a hard stop in case a cursor never ends
        {
            var url = $"{LibraryHost}/library/api/public/items?includeMetadata=true" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
            var (error, json, detail) = await SendAsync(request, ct).ConfigureAwait(false);
            if (error != EpicAccountError.None)
            {
                return new(error, null, detail);
            }

            var (items, next) = ParseLibraryPage(json!);
            records.AddRange(items);
            if (next is null || next == cursor)
            {
                break;
            }

            cursor = next;
        }

        return new(EpicAccountError.None, records);
    }

    /// <summary>Title and kind of one catalog item (public catalog, asked with the user's token like the launcher does).</summary>
    public async Task<EpicApiResult<EpicCatalogInfo>> GetCatalogInfoAsync(string accessToken, string ns, string catalogItemId, CancellationToken ct)
    {
        var url = $"{CatalogHost}/catalog/api/shared/namespace/{Uri.EscapeDataString(ns)}/bulk/items?id={Uri.EscapeDataString(catalogItemId)}"
            + "&includeDLCDetails=true&includeMainGameDetails=true&country=US&locale=en";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
        var (error, json, detail) = await SendAsync(request, ct).ConfigureAwait(false);
        return error != EpicAccountError.None
            ? new(error, null, detail)
            : ParseCatalogInfo(json!, catalogItemId) is { } info ? new(EpicAccountError.None, info) : new(EpicAccountError.Unexpected, null, "item not in catalog");
    }

    internal static EpicApiResult<EpicSession> ParseSession(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return new(EpicAccountError.Unexpected, null, "invalid JSON");
        }

        if (Text(root?["errorCode"]) is { } errorCode)
        {
            return new(errorCode.Contains("corrective_action_required", StringComparison.Ordinal) ? EpicAccountError.ActionRequired : EpicAccountError.Unauthorized, null, errorCode);
        }

        var access = Text(root?["access_token"]);
        var refresh = Text(root?["refresh_token"]);
        var accountId = Text(root?["account_id"]);
        if (access is null || refresh is null || accountId is null)
        {
            return new(EpicAccountError.Unexpected, null, "incomplete token response");
        }

        var expires = DateTimeOffset.TryParse(Text(root?["expires_at"]), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? at
            : DateTimeOffset.UtcNow.AddHours(1);
        return new(EpicAccountError.None, new EpicSession(access, expires, refresh, accountId, Text(root?["displayName"]) ?? accountId));
    }

    /// <summary>Games and apps only: Unreal Engine marketplace content (namespace "ue") and non-applications are skipped.</summary>
    internal static (IReadOnlyList<EpicLibraryRecord> Items, string? NextCursor) ParseLibraryPage(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return ([], null);
        }

        var items = new List<EpicLibraryRecord>();
        if (root?["records"] is JsonArray records)
        {
            foreach (var record in records.OfType<JsonObject>())
            {
                var ns = Text(record["namespace"]);
                var item = Text(record["catalogItemId"]);
                var app = Text(record["appName"]);
                var type = Text(record["recordType"]);
                if (ns is null || item is null || app is null || ns == "ue" || (type is not null && type != "APPLICATION"))
                {
                    continue;
                }

                items.Add(new EpicLibraryRecord(ns, item, app));
            }
        }

        return (items, Text(root?["responseMetadata"]?["nextCursor"]));
    }

    /// <summary>DLC (it has a main game), add-ons, engines and plugins are skipped; "software" without "games" is an app.</summary>
    internal static EpicCatalogInfo? ParseCatalogInfo(string json, string catalogItemId)
    {
        JsonNode? item;
        try
        {
            item = JsonNode.Parse(json)?[catalogItemId];
        }
        catch (JsonException)
        {
            return null;
        }

        var title = Text(item?["title"])?.Trim();
        if (item is null || string.IsNullOrEmpty(title))
        {
            return null;
        }

        var categories = item["categories"] is JsonArray list
            ? list.Select(c => Text(c?["path"])).OfType<string>().ToList()
            : [];
        var skip = item["mainGameItem"] is JsonObject
            || categories.Any(c => c is "addons" or "engines" or "plugins" or "plugin" or "digitalextras" || c.StartsWith("addons/", StringComparison.Ordinal));
        var isApp = categories.Contains("software") && !categories.Contains("games");
        return new EpicCatalogInfo(title, isApp, skip);
    }

    private async Task<EpicApiResult<EpicSession>> TokenAsync(IEnumerable<KeyValuePair<string, string>> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{OAuthHost}/account/api/oauth/token"))
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{LauncherClientId}:{LauncherClientSecret}")));

        // Epic answers bad codes with 400 + an errorCode in JSON: read the body for 400 too.
        var (error, json, detail) = await SendAsync(request, ct, readBodyOnBadRequest: true).ConfigureAwait(false);
        return error != EpicAccountError.None ? new(error, null, detail) : ParseSession(json!);
    }

    private async Task<(EpicAccountError Error, string? Json, string? Detail)> SendAsync(HttpRequestMessage request, CancellationToken ct, bool readBodyOnBadRequest = false)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (EpicAccountError.Network, null, ex.HttpRequestError.ToString());
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return (EpicAccountError.Network, null, "Timeout");
        }

        using (response)
        {
            var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            if (response.StatusCode == HttpStatusCode.BadRequest && readBodyOnBadRequest)
            {
                return (EpicAccountError.None, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null);
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => (EpicAccountError.Unauthorized, null, status),
                HttpStatusCode.TooManyRequests => (EpicAccountError.RateLimited, null, status),
                _ when !response.IsSuccessStatusCode => (EpicAccountError.Unexpected, null, status),
                _ => (EpicAccountError.None, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null),
            };
        }
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
