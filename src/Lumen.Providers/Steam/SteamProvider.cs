// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Core.Settings;

namespace Lumen.Providers.Steam;

/// <summary>
/// Steam: installed games from the local library folders (no login), plus owned games from the Steam
/// Web API when the user supplied a key and SteamID64, plus games the Steam family shares when the user signed in on
/// Steam's website (<see cref="SteamFamilyAccess"/>). Only reads Steam's files and registry.
/// </summary>
public sealed class SteamProvider(
    IRegistryReader registry,
    IFileSystem fileSystem,
    ISecretStore secrets,
    ILocalSettings settings,
    SteamWebApi webApi,
    TimeProvider? time = null,
    SteamFamilyAccess? family = null) : ILibraryProvider, ICachingProvider, IWatchableProvider
{
    public const string SourceId = "steam";

    /// <summary>The owned list barely changes; automatic rescans reuse it for this long (manual Rescan refreshes).</summary>
    public static readonly TimeSpan OwnedCacheLifetime = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private (ProviderResult Result, DateTimeOffset At)? _ownedCache;

    public void InvalidateCache() => _ownedCache = null;

    /// <summary>Each library's steamapps folder: Steam rewrites appmanifest_*.acf when an install finishes or a game is removed.</summary>
    public async Task<IReadOnlyList<WatchTarget>> GetWatchTargetsAsync(CancellationToken ct)
    {
        var steamPath = FindSteamPath();
        if (steamPath is null)
        {
            return [];
        }

        var libraries = await FindLibrariesAsync(steamPath, [], ct).ConfigureAwait(false);
        return libraries
            .Select(lib => Join(lib, "steamapps"))
            .Where(fileSystem.DirectoryExists)
            .Select(dir => new WatchTarget(dir, "appmanifest_*.acf"))
            .ToList();
    }

    /// <summary>
    /// The Steam account to show in the sidebar, read from Steam's own files (no network). Prefers the SteamID64 set in
    /// Settings. Null when Steam or a signed-in account isn't there; never throws.
    /// </summary>
    public async Task<SteamProfile?> ReadProfileAsync(CancellationToken ct)
    {
        try
        {
            var steamPath = FindSteamPath();
            if (steamPath is null)
            {
                return null;
            }

            var preferred = await settings.GetAsync(LocalSettingKeys.SteamId64, ct).ConfigureAwait(false);
            return await SteamLocalProfile.ReadAsync(fileSystem, steamPath, preferred, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // VERIFY: confirmed on one Windows 11 machine (2026-10): SteamPath is lowercase with forward slashes.
    internal const string RegistryKey = @"Software\Valve\Steam";
    internal const string RegistryValue = "SteamPath";

    // AppState.StateFlags bit meaning "fully installed".
    private const int StateFullyInstalled = 4;

    private readonly IReadOnlySet<string> _excluded = SteamExclusions.Embedded;

    public string Source => SourceId;

    public string DisplayName => "Steam";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan | ProviderCapabilities.OwnedLibrary;

    public static LaunchSpec LaunchFor(string appId) => new(LaunchKind.Uri, $"steam://rungameid/{appId}");

    public static LaunchSpec InstallFor(string appId) => new(LaunchKind.Uri, $"steam://install/{appId}");

    public async Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var steamPath = FindSteamPath();
            if (steamPath is null)
            {
                return ProviderResult.NotAvailable("Steam is not installed.");
            }

            var diagnostics = new List<ProviderDiagnostic>();
            var libraries = await FindLibrariesAsync(steamPath, diagnostics, ct).ConfigureAwait(false);
            var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
            foreach (var library in libraries)
            {
                ct.ThrowIfCancellationRequested();
                await ScanLibraryAsync(library, entries, diagnostics, ct).ConfigureAwait(false);
            }

            return ProviderResult.FromScan(entries.Values.ToList(), diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed("Steam: could not read the local library.", ex);
        }
    }

    /// <summary>
    /// Owned games (Web API key) and family-shared games (sign-in), each optional. Family games the user also owns are
    /// left out (the owned copy wins). When one part fails the other still counts (Partial).
    /// </summary>
    public async Task<ProviderResult> ScanOwnedAsync(CancellationToken ct)
    {
        try
        {
            if (_ownedCache is { } cached && _time.GetUtcNow() - cached.At < OwnedCacheLifetime)
            {
                return cached.Result;
            }

            var owned = await ScanOwnedByKeyAsync(ct).ConfigureAwait(false);
            var shared = family is null
                ? new SteamFamilyResult(SteamFamilyStatus.NotConnected, [])
                : await family.GetSharedGamesAsync(ct).ConfigureAwait(false);
            var scan = CombineOwnedAndFamily(owned, shared);

            // Only good answers are reused; failures are retried on the next scan.
            _ownedCache = scan.Status == ProviderStatus.Ok ? (scan, _time.GetUtcNow()) : null;
            return scan;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed("Steam: could not read the owned library.", ex);
        }
    }

    internal ProviderResult CombineOwnedAndFamily(ProviderResult owned, SteamFamilyResult family)
    {
        if (owned.Status == ProviderStatus.NotConfigured && family.Status == SteamFamilyStatus.NotConnected)
        {
            return owned;
        }

        var diagnostics = owned.Status == ProviderStatus.NotConfigured ? [] : owned.Diagnostics.ToList();
        var familyFailed = family.Status is SteamFamilyStatus.Failed or SteamFamilyStatus.NeedsReconnect;
        if (family.Status == SteamFamilyStatus.NeedsReconnect)
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning, "Steam family: Steam no longer accepts the sign-in; sign in again in Settings.", family.Detail));
        }
        else if (family.Status == SteamFamilyStatus.Failed)
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning, "Steam family: shared games unavailable right now.", family.Detail));
        }

        var entries = owned.IsUsable ? owned.Entries.ToList() : [];
        var ownedIds = entries.Select(e => e.Ref.ExternalId).ToHashSet(StringComparer.Ordinal);
        entries.AddRange(ToFamilyEntries(family.Games.Where(g => !ownedIds.Contains(g.AppId))));

        var ownedFailed = owned.Status is ProviderStatus.Failed;
        var anyUsable = owned.IsUsable || family.Status is SteamFamilyStatus.Ok or SteamFamilyStatus.NotInFamily;
        if (!anyUsable)
        {
            return new ProviderResult(ProviderStatus.Failed, [], diagnostics);
        }

        return new ProviderResult(ownedFailed || familyFailed || owned.Status == ProviderStatus.Partial ? ProviderStatus.Partial : ProviderStatus.Ok, entries, diagnostics);
    }

    // Signed in on Steam's website: the account's own session reads its library (no key needed). Otherwise the key.
    private async Task<ProviderResult> ScanOwnedByKeyAsync(CancellationToken ct)
    {
        if (family is not null && await family.TokenIfSignedInAsync(ct).ConfigureAwait(false) is { } token)
        {
            var mine = await webApi.GetOwnedGamesWithTokenAsync(token.AccessToken, token.SteamId, ct).ConfigureAwait(false);
            if (mine.Error == SteamApiError.None)
            {
                return ProviderResult.FromScan(ToEntries(mine.Games), []);
            }
        }

        var apiKey = await secrets.GetAsync(SecretNames.SteamWebApiKey, ct).ConfigureAwait(false);
        var steamId = await settings.GetAsync(LocalSettingKeys.SteamId64, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey) || steamId is null || !SteamWebApi.IsValidSteamId64(steamId))
        {
            return ProviderResult.NotConfigured("Steam Web API key or SteamID64 not set.");
        }

        var result = await webApi.GetOwnedGamesAsync(apiKey, steamId, ct).ConfigureAwait(false);
        return result.Error switch
        {
            SteamApiError.None => ProviderResult.FromScan(ToEntries(result.Games), []),
            SteamApiError.PrivateProfile => new ProviderResult(
                ProviderStatus.Partial,
                [],
                [new ProviderDiagnostic(DiagnosticSeverity.Warning, "Steam returned no games: the profile's game details are private.")]),
            SteamApiError.InvalidKey => ProviderResult.Failed($"Steam rejected the Web API key (HTTP {result.Detail}). Signing in on Steam's website in Settings reads the library without a key."),
            _ => ProviderResult.Failed($"Steam Web API unavailable ({result.Error}: {result.Detail})."),
        };
    }

    /// <summary>Family-shared games: not installed (an installed copy comes from the local scan), marked "family".</summary>
    internal IReadOnlyList<LibraryEntry> ToFamilyEntries(IEnumerable<SteamSharedGame> games) =>
        games
            .Where(g => !_excluded.Contains(g.AppId))
            .DistinctBy(g => g.AppId)
            .Select(g =>
            {
                var extra = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["appid"] = g.AppId,
                    ["family"] = "true",
                    ["familyOwners"] = g.OwnerCount.ToString(CultureInfo.InvariantCulture),
                    ["playtimeMinutes"] = g.PlaytimeMinutes.ToString(CultureInfo.InvariantCulture),
                };
                if (g.LastPlayedUnix > 0)
                {
                    extra["lastPlayed"] = g.LastPlayedUnix.ToString(CultureInfo.InvariantCulture);
                }

                return new LibraryEntry(
                    new ItemRef(SourceId, g.AppId),
                    ItemKind.Game,
                    g.Name,
                    InstallState.NotInstalled,
                    null,
                    LaunchFor(g.AppId),
                    InstallFor(g.AppId),
                    extra);
            })
            .ToList();

    /// <summary>Checks a key/SteamID pair without saving anything (Settings → Steam → Save and test).</summary>
    public Task<SteamOwnedGamesResult> TestConnectionAsync(string apiKey, string steamId64, CancellationToken ct) =>
        webApi.GetOwnedGamesAsync(apiKey, steamId64, ct);

    internal IReadOnlyList<LibraryEntry> ToEntries(IEnumerable<SteamOwnedGame> games) =>
        games
            .Where(g => !_excluded.Contains(g.AppId))
            .DistinctBy(g => g.AppId)
            .Select(g =>
            {
                var extra = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["appid"] = g.AppId,
                    ["playtimeMinutes"] = g.PlaytimeMinutes.ToString(CultureInfo.InvariantCulture),
                };
                if (g.LastPlayedUnix > 0)
                {
                    extra["lastPlayed"] = g.LastPlayedUnix.ToString(CultureInfo.InvariantCulture);
                }

                return new LibraryEntry(
                    new ItemRef(SourceId, g.AppId),
                    ItemKind.Game,
                    g.Name,
                    InstallState.NotInstalled,
                    null,
                    LaunchFor(g.AppId),
                    InstallFor(g.AppId),
                    extra);
            })
            .ToList();

    internal string? FindSteamPath()
    {
        var raw = registry.GetString(RegistryHive.CurrentUser, RegistryKey, RegistryValue);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var path = NormalizePath(raw);
        return fileSystem.DirectoryExists(path) ? path : null;
    }

    internal async Task<IReadOnlyList<string>> FindLibrariesAsync(string steamPath, List<ProviderDiagnostic> diagnostics, CancellationToken ct)
    {
        var libraries = new List<string>();
        var file = Join(steamPath, @"steamapps\libraryfolders.vdf");
        if (!fileSystem.FileExists(file))
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning, "Steam: libraryfolders.vdf not found; only the main library was scanned."));
            return [steamPath];
        }

        try
        {
            var root = VdfParser.Parse(await fileSystem.ReadAllTextAsync(file, ct).ConfigureAwait(false));
            var folders = root.GetObject("libraryfolders") ?? new VdfObject();
            foreach (var (key, value) in folders.Properties)
            {
                var path = value switch
                {
                    VdfObject folder => folder.GetString("path"),          // current format
                    string legacy when IsNumeric(key) => legacy,           // pre-2021 format: "1" "D:\\Games"
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(path))
                {
                    libraries.Add(NormalizePath(path));
                }
            }
        }
        catch (VdfParseException ex)
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning, "Steam: libraryfolders.vdf could not be parsed; only the main library was scanned.", ex.Message));
        }

        // The registry value is lowercased; prefer the properly cased path from libraryfolders.vdf.
        if (!libraries.Contains(steamPath, StringComparer.OrdinalIgnoreCase))
        {
            libraries.Insert(0, steamPath);
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task ScanLibraryAsync(string library, Dictionary<string, LibraryEntry> entries, List<ProviderDiagnostic> diagnostics, CancellationToken ct)
    {
        var steamApps = Join(library, "steamapps");
        if (!fileSystem.DirectoryExists(steamApps))
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning, $"Steam: library folder not found: {library}"));
            return;
        }

        foreach (var manifest in fileSystem.GetFiles(steamApps, "appmanifest_*.acf"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var entry = ParseManifest(VdfParser.Parse(await fileSystem.ReadAllTextAsync(manifest, ct).ConfigureAwait(false)), library);
                if (entry is not null)
                {
                    entries.TryAdd(entry.Ref.ExternalId, entry);
                }
            }
            catch (Exception ex) when (ex is VdfParseException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new(DiagnosticSeverity.Warning, $"Steam: skipped unreadable manifest {manifest[(manifest.LastIndexOfAny(['\\', '/']) + 1)..]}", ex.Message));
            }
        }
    }

    internal LibraryEntry? ParseManifest(VdfObject document, string library)
    {
        var app = document.GetObject("AppState");
        var appId = app?.GetString("appid");
        var name = app?.GetString("name");
        var installDir = app?.GetString("installdir");
        if (app is null || string.IsNullOrWhiteSpace(appId) || !IsNumeric(appId) || string.IsNullOrWhiteSpace(name))
        {
            throw new VdfParseException("Manifest is missing AppState/appid/name");
        }

        if (_excluded.Contains(appId))
        {
            return null;
        }

        // Partially downloaded games are not playable yet; the owned scan still lists them as not installed.
        _ = int.TryParse(app.GetString("StateFlags"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags);
        if ((flags & StateFullyInstalled) == 0)
        {
            return null;
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["appid"] = appId,
            ["library"] = library,
        };
        if (long.TryParse(app.GetString("LastPlayed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lastPlayed) && lastPlayed > 0)
        {
            extra["lastPlayed"] = lastPlayed.ToString(CultureInfo.InvariantCulture);
        }

        return new LibraryEntry(
            new ItemRef(SourceId, appId),
            ItemKind.Game,
            name.Trim(),
            InstallState.Installed,
            string.IsNullOrWhiteSpace(installDir) ? null : Join(library, @"steamapps\common\" + installDir),
            LaunchFor(appId),
            InstallFor(appId),
            extra);
    }

    /// <summary>Steam paths are Windows paths; keep backslashes regardless of the OS running the tests.</summary>
    internal static string NormalizePath(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');

    internal static string Join(string left, string right) => left.TrimEnd('\\') + "\\" + right;

    private static bool IsNumeric(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);
}
