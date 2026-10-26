// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lumen.Core.Library;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Artwork;

public enum ArtworkKind
{
    /// <summary>Portrait cover, 600×900 (library grid).</summary>
    Cover,

    /// <summary>Wide background art (detail view, home, dynamic background).</summary>
    Hero,

    /// <summary>Transparent title logo drawn over the hero.</summary>
    Logo,

    /// <summary>Small square icon (apps, tray, quick search).</summary>
    Icon,
}

/// <summary>Knows where a store keeps artwork for its entries. Only returns URLs on allowed hosts (security rule 4).</summary>
public interface IArtworkSource
{
    bool CanHandle(LibraryEntry entry);

    /// <summary>URLs to try in order; the first one that returns a valid image wins.</summary>
    IReadOnlyList<Uri> Candidates(LibraryEntry entry, ArtworkKind kind);
}

/// <summary>
/// Resolves artwork to a local file: disk cache first, then each source's candidate URLs. Downloads are
/// limited in parallel, deduplicated, size-capped and checked to really be an image before they are kept.
/// A confirmed "not found" is remembered for a week so the network isn't asked again on every start.
/// </summary>
public sealed partial class ArtworkService
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public static readonly TimeSpan MissRetryAfter = TimeSpan.FromDays(7);

    private static readonly string[] _extensions = [".jpg", ".png", ".webp"];

    private readonly HttpClient _http;
    private readonly IReadOnlyList<IArtworkSource> _sources;
    private readonly string _cacheDirectory;
    private readonly ILogger<ArtworkService> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _downloads = new(4, 4);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new(StringComparer.Ordinal);

    public ArtworkService(HttpClient http, IEnumerable<IArtworkSource> sources, string cacheDirectory, ILogger<ArtworkService> logger, TimeProvider? time = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToList();
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = cacheDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Artwork for a (possibly merged) item: tries the primary entry, then the item's other stores.</summary>
    public async Task<string?> GetForItemAsync(LibraryItem item, ArtworkKind kind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (var entry in item.Entries.OrderBy(e => e == item.Primary ? 0 : 1))
        {
            var path = await GetAsync(entry, kind, ct).ConfigureAwait(false);
            if (path is not null)
            {
                return path;
            }
        }

        return null;
    }

    /// <returns>Path of a cached image file, or null when no source has one.</returns>
    public Task<string?> GetAsync(LibraryEntry entry, ArtworkKind kind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var source = _sources.FirstOrDefault(s => s.CanHandle(entry));
        if (source is null)
        {
            return Task.FromResult<string?>(null);
        }

        var basePath = BasePath(entry.Ref, kind);
        var cached = _extensions.Select(ext => basePath + ext).FirstOrDefault(File.Exists);
        if (cached is not null)
        {
            return Task.FromResult<string?>(cached);
        }

        if (IsRecentMiss(basePath))
        {
            return Task.FromResult<string?>(null);
        }

        // One download per key even if many tiles ask at once.
        var lazy = _inFlight.GetOrAdd(basePath, _ => new Lazy<Task<string?>>(() => DownloadAsync(source, entry, kind, basePath, ct)));
        return AwaitAndForget(basePath, lazy.Value);
    }

    /// <summary>Deletes every cached image (Settings → Artwork → Clear cache).</summary>
    public void ClearCache()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    /// <summary>Detects JPEG, PNG or WebP from the first bytes; returns the extension, or null if it is not an image.</summary>
    public static string? DetectImageExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (header.Length >= 8 && header[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ".png";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }

    private async Task<string?> AwaitAndForget(string key, Task<string?> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<string?> DownloadAsync(IArtworkSource source, LibraryEntry entry, ArtworkKind kind, string basePath, CancellationToken ct)
    {
        await _downloads.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var allNotFound = true;
            foreach (var url in source.Candidates(entry, kind))
            {
                var (bytes, definitive) = await TryFetchAsync(url, ct).ConfigureAwait(false);
                if (bytes is not null)
                {
                    var ext = DetectImageExtension(bytes)!;
                    Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
                    var temp = basePath + ".tmp";
                    await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
                    File.Move(temp, basePath + ext, overwrite: true);
                    File.Delete(basePath + ".miss");
                    return basePath + ext;
                }

                allNotFound &= definitive;
            }

            // Only remember a miss when every source said "not found"; network errors retry next time.
            if (allNotFound)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(basePath)!);
                await File.WriteAllTextAsync(basePath + ".miss", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCacheWriteFailed(ex, entry.Ref.ToString(), kind);
            return null;
        }
        finally
        {
            _downloads.Release();
        }
    }

    /// <returns>
    /// The image bytes, or null. <c>Definitive</c> is true when the answer won't change soon (404/403/410, or a
    /// response that isn't a usable image) and false for network errors and 5xx, which are retried next time.
    /// </returns>
    private async Task<(byte[]? Bytes, bool Definitive)> TryFetchAsync(Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (null, response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Gone);
            }

            if (response.Content.Headers.ContentLength is > MaxImageBytes)
            {
                return (null, true);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes)
                {
                    return (null, true); // larger than announced: refuse
                }

                buffer.Write(chunk, 0, read);
            }

            var bytes = buffer.ToArray();
            return DetectImageExtension(bytes) is null ? (null, true) : (bytes, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return (null, false);
        }
    }

    private bool IsRecentMiss(string basePath)
    {
        var marker = basePath + ".miss";
        if (!File.Exists(marker))
        {
            return false;
        }

        return DateTimeOffset.TryParse(File.ReadAllText(marker), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
            && _time.GetUtcNow() - when < MissRetryAfter;
    }

    // %LOCALAPPDATA%\Lumen\cache\artwork\{source}\{hash of ref}\{kind}: hashing keeps any external id path-safe.
    private string BasePath(ItemRef itemRef, ArtworkKind kind)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(itemRef.ToString())))[..16];
        return Path.Combine(_cacheDirectory, itemRef.Source, hash, kind.ToString().ToLowerInvariant());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write artwork to the cache for {Ref} ({Kind})")]
    private partial void LogCacheWriteFailed(Exception exception, string @ref, ArtworkKind kind);
}
