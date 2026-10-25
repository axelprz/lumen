// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Steam;

/// <summary>
/// Steam: installed games from the local library folders (no login). Owned-not-installed games arrive
/// with the Steam Web API integration. Only reads Steam's files and registry.
/// </summary>
public sealed class SteamProvider(IRegistryReader registry, IFileSystem fileSystem) : ILibraryProvider
{
    public const string SourceId = "steam";

    // VERIFY: confirmed on one Windows 11 machine (2026-10): SteamPath is lowercase with forward slashes.
    internal const string RegistryKey = @"Software\Valve\Steam";
    internal const string RegistryValue = "SteamPath";

    // AppState.StateFlags bit meaning "fully installed".
    private const int StateFullyInstalled = 4;

    private readonly IReadOnlySet<string> _excluded = SteamExclusions.Embedded;

    public string Source => SourceId;

    public string DisplayName => "Steam";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

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

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Steam owned library is not configured."));

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
