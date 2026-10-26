// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lumen.Core.Artwork;

namespace Lumen.Providers.SteamGridDb;

public enum SgdbError
{
    None,
    NotFound,

    /// <summary>401/403: the key is wrong or revoked.</summary>
    InvalidKey,
    RateLimited,
    Network,
    Unexpected,
}

public sealed record SgdbResult<T>(SgdbError Error, T? Value)
{
    public static SgdbResult<T> Ok(T value) => new(SgdbError.None, value);

    public static SgdbResult<T> Fail(SgdbError error) => new(error, default);
}

public sealed record SgdbGame(long Id, string Name);

/// <summary>
/// SteamGridDB API v2 (an allowed host). The key travels only in the Authorization header, never in a URL; the
/// client is a plain HttpClient (no URL-logging handler). Image URLs are accepted only on steamgriddb.com hosts.
/// Explicit, humorous and epilepsy-flagged art is filtered out by the API query.
/// </summary>
public sealed class SteamGridDbClient(HttpClient http)
{
    public const string BaseAddress = "https://www.steamgriddb.com/api/v2/";

    // Up to this many images per request are offered as candidates (best-scored first).
    private const int MaxImages = 3;
    private const string SafeFilters = "nsfw=false&humor=false&epilepsy=false";

    public Task<SgdbResult<SgdbGame>> GetGameBySteamAppIdAsync(string apiKey, string appId, CancellationToken ct) =>
        SendAsync(apiKey, $"games/steam/{Uri.EscapeDataString(appId)}", ParseGame, ct);

    public Task<SgdbResult<IReadOnlyList<SgdbGame>>> SearchAsync(string apiKey, string title, CancellationToken ct) =>
        SendAsync(apiKey, $"search/autocomplete/{Uri.EscapeDataString(title)}", ParseGames, ct);

    public Task<SgdbResult<IReadOnlyList<Uri>>> GetImagesAsync(string apiKey, long gameId, ArtworkKind kind, CancellationToken ct)
    {
        var path = kind switch
        {
            ArtworkKind.Cover => $"grids/game/{gameId}?dimensions=600x900&types=static&{SafeFilters}",
            ArtworkKind.Hero => $"heroes/game/{gameId}?types=static&{SafeFilters}",
            ArtworkKind.Logo => $"logos/game/{gameId}?types=static&{SafeFilters}",
            ArtworkKind.Icon => $"icons/game/{gameId}?types=static&{SafeFilters}",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return SendAsync(apiKey, path, ParseImages, ct);
    }

    /// <summary>A trusted image URL: https on steamgriddb.com or one of its subdomains (e.g. cdn2.steamgriddb.com).</summary>
    public static bool IsTrustedImageUrl(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps
        && (url.Host.Equals("steamgriddb.com", StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith(".steamgriddb.com", StringComparison.OrdinalIgnoreCase));

    internal static SgdbGame? ParseGame(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var id) && id.TryGetInt64(out var value)
            ? new SgdbGame(value, data.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty)
            : null;

    internal static IReadOnlyList<SgdbGame>? ParseGames(JsonElement data) =>
        data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(ParseGame).OfType<SgdbGame>().ToList() : null;

    internal static IReadOnlyList<Uri>? ParseImages(JsonElement data) =>
        data.ValueKind != JsonValueKind.Array
            ? null
            : data.EnumerateArray()
                .Where(img => !(img.TryGetProperty("nsfw", out var nsfw) && nsfw.ValueKind == JsonValueKind.True))
                .Select(img => img.TryGetProperty("url", out var url) ? url.GetString() : null)
                .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null)
                .OfType<Uri>()
                .Where(IsTrustedImageUrl)
                .Take(MaxImages)
                .ToList();

    private async Task<SgdbResult<T>> SendAsync<T>(string apiKey, string relative, Func<JsonElement, T?> parse, CancellationToken ct)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(BaseAddress), relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return SgdbResult<T>.Fail(SgdbError.Network);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return SgdbResult<T>.Fail(SgdbError.Network);
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    return SgdbResult<T>.Fail(SgdbError.InvalidKey);
                case HttpStatusCode.NotFound:
                    return SgdbResult<T>.Fail(SgdbError.NotFound);
                case HttpStatusCode.TooManyRequests:
                    return SgdbResult<T>.Fail(SgdbError.RateLimited);
                case var code when !response.IsSuccessStatusCode:
                    return SgdbResult<T>.Fail((int)code >= 500 ? SgdbError.Network : SgdbError.Unexpected);
            }

            try
            {
                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
                if (!doc.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                    || !doc.RootElement.TryGetProperty("data", out var data))
                {
                    return SgdbResult<T>.Fail(SgdbError.Unexpected);
                }

                var value = parse(data);
                return value is null ? SgdbResult<T>.Fail(SgdbError.Unexpected) : SgdbResult<T>.Ok(value);
            }
            catch (JsonException)
            {
                return SgdbResult<T>.Fail(SgdbError.Unexpected);
            }
        }
    }
}
