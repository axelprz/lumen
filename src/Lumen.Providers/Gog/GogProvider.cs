// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Gog;

/// <summary>
/// GOG: installed games from the registry keys GOG installers write (works with or without GOG Galaxy).
/// Launches through Galaxy when it is installed, otherwise runs the game's executable. Read-only.
/// </summary>
public sealed class GogProvider(IRegistryReader registry, IFileSystem fileSystem) : ILibraryProvider
{
    public const string SourceId = "gog";

    // VERIFY: starting points from ARCHITECTURE §4, not yet checked on a machine with GOG games installed.
    internal const string GamesKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";
    internal const string GalaxyPathsKey = @"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths";
    internal const string GalaxyKey = @"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient";

    public string Source => SourceId;

    public string DisplayName => "GOG";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    public Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var galaxy = FindGalaxyExecutable();
            var gameIds = registry.GetSubKeyNames(RegistryHive.LocalMachine, GamesKey);
            if (gameIds.Count == 0 && galaxy is null)
            {
                return Task.FromResult(ProviderResult.NotAvailable("No GOG games or GOG Galaxy found."));
            }

            var diagnostics = new List<ProviderDiagnostic>();
            var entries = new List<LibraryEntry>();
            foreach (var id in gameIds)
            {
                ct.ThrowIfCancellationRequested();
                var entry = ReadGame(id, galaxy, diagnostics);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            return Task.FromResult(ProviderResult.FromScan(entries, diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProviderResult.Failed("GOG: could not read the installed games.", ex));
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("GOG owned library needs the GOG sign-in (M5)."));

    internal string? FindGalaxyExecutable()
    {
        var folder = registry.GetString(RegistryHive.LocalMachine, GalaxyPathsKey, "client");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var exeName = registry.GetString(RegistryHive.LocalMachine, GalaxyKey, "clientExecutable");
        var exe = WindowsPath.Join(folder, string.IsNullOrWhiteSpace(exeName) ? "GalaxyClient.exe" : exeName);
        return fileSystem.FileExists(exe) ? exe : null;
    }

    private LibraryEntry? ReadGame(string id, string? galaxy, List<ProviderDiagnostic> diagnostics)
    {
        var key = $@"{GamesKey}\{id}";
        string? Value(string name) => registry.GetString(RegistryHive.LocalMachine, key, name);

        if (!id.All(char.IsAsciiDigit))
        {
            return null;
        }

        // DLC keys carry the id of the game they belong to.
        if (!string.IsNullOrWhiteSpace(Value("dependsOn")))
        {
            return null;
        }

        var title = Value("gameName");
        var path = Value("path");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(path))
        {
            diagnostics.Add(new(DiagnosticSeverity.Warning, $"GOG: skipped game {id} with incomplete registry data."));
            return null;
        }

        path = WindowsPath.Normalize(path);
        if (!fileSystem.DirectoryExists(path))
        {
            // Leftover keys after a manual uninstall: not an error, just not installed.
            diagnostics.Add(new(DiagnosticSeverity.Info, $"GOG: {title} is registered but its folder is missing."));
            return null;
        }

        var exe = Value("exe");
        var extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["gameId"] = id };
        if (!string.IsNullOrWhiteSpace(exe))
        {
            extra["exe"] = exe;
        }

        return new LibraryEntry(
            new ItemRef(SourceId, id),
            ItemKind.Game,
            title.Trim(),
            InstallState.Installed,
            path,
            LaunchSpecFor(id, path, exe, Value("launchParam"), Value("workingDir"), galaxy),
            null,
            extra);
    }

    /// <summary>Galaxy when available (keeps overlay, cloud saves and playtime in Galaxy); otherwise the game's exe.</summary>
    internal static LaunchSpec? LaunchSpecFor(string id, string path, string? exe, string? launchParam, string? workingDir, string? galaxy)
    {
        if (galaxy is not null)
        {
            // VERIFY: Galaxy command line from ARCHITECTURE §4.
            return new LaunchSpec(LaunchKind.ProcessWithArgs, galaxy, $"/command=runGame /gameId={id} /path=\"{path}\"", WindowsPath.Directory(galaxy));
        }

        if (string.IsNullOrWhiteSpace(exe))
        {
            return null;
        }

        var exePath = WindowsPath.Normalize(exe);
        return new LaunchSpec(
            LaunchKind.Executable,
            exePath,
            string.IsNullOrWhiteSpace(launchParam) ? null : launchParam,
            string.IsNullOrWhiteSpace(workingDir) ? WindowsPath.Directory(exePath) : WindowsPath.Normalize(workingDir));
    }
}
