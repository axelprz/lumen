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

/// <summary>Image URLs a source suggests for one entry and kind.</summary>
/// <param name="Definitive">
/// True when the empty/short list is a real answer ("this game has no art here"); false when the source could
/// not answer now (not configured, offline, rate-limited), so a miss must not be remembered.
/// </param>
public sealed record ArtworkCandidates(IReadOnlyList<Uri> Urls, bool Definitive)
{
    public static ArtworkCandidates None { get; } = new([], true);

    public static ArtworkCandidates Unavailable { get; } = new([], false);
}

/// <summary>Knows where artwork lives for some entries. Only returns URLs on allowed hosts (security rule 4).</summary>
public interface IArtworkSource
{
    bool CanHandle(LibraryEntry entry);

    /// <summary>URLs to try in order; the first one that returns a valid image wins.</summary>
    Task<ArtworkCandidates> GetCandidatesAsync(LibraryEntry entry, ArtworkKind kind, CancellationToken ct);
}

/// <summary>
/// Artwork that lives on this PC (app and package icons): no network. The bytes are validated and cached like
/// downloads.
/// </summary>
public interface ILocalArtworkSource
{
    bool CanHandle(LibraryEntry entry, ArtworkKind kind);

    /// <returns>A PNG/JPEG/WebP image, or null when this entry has none.</returns>
    Task<byte[]?> GetImageAsync(LibraryEntry entry, ArtworkKind kind, CancellationToken ct);
}

/// <summary>
/// Resolves artwork to a local file: the user's override first, then the disk cache, then local sources (icons),
/// then each source's
/// candidate URLs (in registration order, e.g. Steam CDN before SteamGridDB). Downloads are limited in parallel,
/// deduplicated, size-capped and checked to really be an image before they are kept. A definitive "not found"
/// is remembered for a week so the network isn't asked again on every start.
/// </summary>
public sealed partial class ArtworkService
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public static readonly TimeSpan MissRetryAfter = TimeSpan.FromDays(7);

    private static readonly string[] _extensions = [".jpg", ".png", ".webp"];

    private readonly HttpClient _http;
    private readonly IReadOnlyList<IArtworkSource> _sources;
    private readonly IReadOnlyList<ILocalArtworkSource> _localSources;
    private readonly string _cacheDirectory;
    private readonly string _overridesDirectory;
    private readonly ILogger<ArtworkService> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _downloads = new(4, 4);
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new(StringComparer.Ordinal);

    /// <param name="overridesDirectory">Where user-chosen images live; kept apart so clearing the cache never deletes them.</param>
    public ArtworkService(
        HttpClient http,
        IEnumerable<IArtworkSource> sources,
        string cacheDirectory,
        string overridesDirectory,
        ILogger<ArtworkService> logger,
        TimeProvider? time = null,
        IEnumerable<ILocalArtworkSource>? localSources = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToList();
        _localSources = localSources?.ToList() ?? [];
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(overridesDirectory);
        _cacheDirectory = cacheDirectory;
        _overridesDirectory = overridesDirectory;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Artwork for a (possibly merged) item: override, then the primary entry, then the item's other stores.</summary>
    public async Task<string?> GetForItemAsync(LibraryItem item, ArtworkKind kind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(item);
        var overridden = item.Entries.Select(e => FindOverride(e.Ref, kind)).FirstOrDefault(p => p is not null);
        if (overridden is not null)
        {
            return overridden;
        }

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
        var overridden = FindOverride(entry.Ref, kind);
        if (overridden is not null)
        {
            return Task.FromResult<string?>(overridden);
        }

        var sources = _sources.Where(s => s.CanHandle(entry)).ToList();
        var local = _localSources.Where(s => s.CanHandle(entry, kind)).ToList();
        if (sources.Count == 0 && local.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        var basePath = BasePath(_cacheDirectory, entry.Ref, kind);
        var cached = FindExisting(basePath);
        if (cached is not null)
        {
            return Task.FromResult<string?>(cached);
        }

        if (IsRecentMiss(basePath))
        {
            return Task.FromResult<string?>(null);
        }

        // One download per key even if many tiles ask at once.
        var lazy = _inFlight.GetOrAdd(basePath, _ => new Lazy<Task<string?>>(() => DownloadAsync(sources, local, entry, kind, basePath, ct)));
        return AwaitAndForget(basePath, lazy.Value);
    }

    /// <summary>
    /// Uses a local image the user picked as this entry's artwork. The file is copied (so moving or deleting the
    /// original is fine) and must be a real JPEG/PNG/WebP of at most <see cref="MaxImageBytes"/>.
    /// </summary>
    /// <returns>False when the file is not a usable image.</returns>
    public async Task<bool> SetOverrideAsync(ItemRef itemRef, ArtworkKind kind, string imagePath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        var info = new FileInfo(imagePath);
        if (!info.Exists || info.Length > MaxImageBytes)
        {
            return false;
        }

        var bytes = await File.ReadAllBytesAsync(imagePath, ct).ConfigureAwait(false);
        var ext = DetectImageExtension(bytes);
        if (ext is null)
        {
            return false;
        }

        var basePath = BasePath(_overridesDirectory, itemRef, kind);
        RemoveOverride(itemRef, kind);
        await WriteAtomicallyAsync(basePath + ext, bytes, ct).ConfigureAwait(false);
        return true;
    }

    public void RemoveOverride(ItemRef itemRef, ArtworkKind kind)
    {
        var basePath = BasePath(_overridesDirectory, itemRef, kind);
        foreach (var path in _extensions.Select(ext => basePath + ext).Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    public bool HasOverride(ItemRef itemRef, ArtworkKind kind) => FindOverride(itemRef, kind) is not null;

    /// <summary>Deletes every downloaded image (Settings → Clear artwork cache). Overrides are kept.</summary>
    public void ClearCache()
    {
        if (Directory.Exists(_cacheDirectory))
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
    }

    /// <summary>Forgets remembered misses, e.g. after a new source (SteamGridDB key) becomes available.</summary>
    public void ClearMisses()
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return;
        }

        foreach (var marker in Directory.EnumerateFiles(_cacheDirectory, "*.miss", SearchOption.AllDirectories))
        {
            File.Delete(marker);
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

    private string? FindOverride(ItemRef itemRef, ArtworkKind kind) => FindExisting(BasePath(_overridesDirectory, itemRef, kind));

    private static string? FindExisting(string basePath) => _extensions.Select(ext => basePath + ext).FirstOrDefault(File.Exists);

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

    private async Task<string?> DownloadAsync(
        IReadOnlyList<IArtworkSource> sources,
        IReadOnlyList<ILocalArtworkSource> local,
        LibraryEntry entry,
        ArtworkKind kind,
        string basePath,
        CancellationToken ct)
    {
        await _downloads.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Local images first: no network, and a missing icon is a definitive answer.
            foreach (var source in local)
            {
                byte[]? image;
                try
                {
                    image = await source.GetImageAsync(entry, kind, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSourceFailed(ex, entry.Ref.ToString(), kind);
                    image = null;
                }

                if (image is { Length: > 0 } && image.LongLength <= MaxImageBytes && DetectImageExtension(image) is { } ext)
                {
                    var path = basePath + ext;
                    await WriteAtomicallyAsync(path, image, ct).ConfigureAwait(false);
                    return path;
                }
            }

            var allDefinitive = true;
            foreach (var source in sources)
            {
                ArtworkCandidates candidates;
                try
                {
                    candidates = await source.GetCandidatesAsync(entry, kind, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogSourceFailed(ex, entry.Ref.ToString(), kind);
                    candidates = ArtworkCandidates.Unavailable;
                }

                allDefinitive &= candidates.Definitive;
                foreach (var url in candidates.Urls)
                {
                    var (bytes, definitive) = await TryFetchAsync(url, ct).ConfigureAwait(false);
                    if (bytes is not null)
                    {
                        var path = basePath + DetectImageExtension(bytes)!;
                        await WriteAtomicallyAsync(path, bytes, ct).ConfigureAwait(false);
                        if (File.Exists(basePath + ".miss"))
                        {
                            File.Delete(basePath + ".miss");
                        }

                        return path;
                    }

                    allDefinitive &= definitive;
                }
            }

            // Only remember a miss when every source gave a real answer; "not configured"/offline retries next time.
            if (allDefinitive)
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

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
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

    // {root}\{source}\{sha256(ref)[..16]}\{kind}: hashing keeps any external id path-safe.
    private static string BasePath(string root, ItemRef itemRef, ArtworkKind kind)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(itemRef.ToString())))[..16];
        return Path.Combine(root, itemRef.Source, hash, kind.ToString().ToLowerInvariant());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write artwork to the cache for {Ref} ({Kind})")]
    private partial void LogCacheWriteFailed(Exception exception, string @ref, ArtworkKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An artwork source failed for {Ref} ({Kind})")]
    private partial void LogSourceFailed(Exception exception, string @ref, ArtworkKind kind);
}
