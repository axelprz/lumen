// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Ubisoft;

/// <summary>
/// Ubisoft Connect: installed games from the launcher's registry keys; titles from the matching
/// "Uplay Install {id}" uninstall entry. Launches through Ubisoft Connect. Read-only.
/// </summary>
public sealed class UbisoftProvider(IRegistryReader registry, IFileSystem fileSystem) : ILibraryProvider
{
    public const string SourceId = "ubisoft";

    // Verified on Windows 11 (2026-10): Installs\{id}\InstallDir uses forward slashes and a trailing slash;
    // "Uplay Install {id}" carries the title. The launcher's own entry is "Uplay" (not matched).
    internal const string LauncherKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher";
    internal const string InstallsKey = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
    internal const string UninstallPrefix = "Uplay Install ";

    public string Source => SourceId;

    public string DisplayName => "Ubisoft Connect";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    /// <summary>Launch URI; verified (2026-10): starts the game through Ubisoft Connect.</summary>
    public static LaunchSpec LaunchFor(string id) => new(LaunchKind.Uri, $"uplay://launch/{id}/0");

    public Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var ids = registry.GetSubKeyNames(RegistryHive.LocalMachine, InstallsKey);
            var launcherDir = registry.GetString(RegistryHive.LocalMachine, LauncherKey, "InstallDir");
            if (ids.Count == 0 && string.IsNullOrWhiteSpace(launcherDir))
            {
                return Task.FromResult(ProviderResult.NotAvailable("Ubisoft Connect is not installed."));
            }

            var titles = UninstallEntries.ReadAll(registry)
                .Where(e => e.KeyName.StartsWith(UninstallPrefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.DisplayName))
                .GroupBy(e => e.KeyName[UninstallPrefix.Length..].Trim(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().DisplayName!.Trim(), StringComparer.Ordinal);

            var diagnostics = new List<ProviderDiagnostic>();
            var entries = new List<LibraryEntry>();
            foreach (var id in ids.Where(id => id.All(char.IsAsciiDigit)))
            {
                ct.ThrowIfCancellationRequested();
                var dir = registry.GetString(RegistryHive.LocalMachine, $@"{InstallsKey}\{id}", "InstallDir");
                if (string.IsNullOrWhiteSpace(dir))
                {
                    continue;
                }

                dir = WindowsPath.Normalize(dir);
                if (!fileSystem.DirectoryExists(dir))
                {
                    diagnostics.Add(new(DiagnosticSeverity.Info, $"Ubisoft: game {id} is registered but its folder is missing."));
                    continue;
                }

                // Without an uninstall entry, the install folder name is the best title available.
                var title = titles.GetValueOrDefault(id) ?? WindowsPath.FileName(dir);
                entries.Add(new LibraryEntry(
                    new ItemRef(SourceId, id),
                    ItemKind.Game,
                    title,
                    InstallState.Installed,
                    dir,
                    LaunchFor(id),
                    null,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["gameId"] = id }));
            }

            return Task.FromResult(ProviderResult.FromScan(entries, diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProviderResult.Failed("Ubisoft: could not read the installed games.", ex));
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Ubisoft owned library is not supported in v1."));
}
