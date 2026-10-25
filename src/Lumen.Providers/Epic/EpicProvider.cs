// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Epic;

/// <summary>
/// Epic Games Store: installed games from the launcher's manifests (<c>*.item</c> JSON files), no login.
/// Owned-not-installed games need the optional Epic sign-in (M5). Only reads Epic's files and registry.
/// </summary>
public sealed class EpicProvider(IRegistryReader registry, IFileSystem fileSystem, IKnownFolders folders) : ILibraryProvider
{
    public const string SourceId = "epic";

    // VERIFY: starting points from ARCHITECTURE §4, not yet checked on a machine with Epic installed.
    internal const string RegistryKey = @"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher";
    internal const string RegistryValue = "AppDataPath";

    private static readonly string[] _excludedCategories = ["addons", "engines", "plugins", "plugin"];

    public string Source => SourceId;

    public string DisplayName => "Epic Games";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    /// <summary>VERIFY: launcher URI format from community documentation (Heroic/Legendary/Playnite).</summary>
    public static LaunchSpec LaunchFor(string appName, string? catalogNamespace, string? catalogItemId) =>
        new(LaunchKind.Uri, $"com.epicgames.launcher://apps/{AppPath(appName, catalogNamespace, catalogItemId)}?action=launch&silent=true");

    /// <summary>VERIFY: opens the launcher on the game so the user can install it.</summary>
    public static LaunchSpec InstallFor(string appName, string? catalogNamespace, string? catalogItemId) =>
        new(LaunchKind.Uri, $"com.epicgames.launcher://apps/{AppPath(appName, catalogNamespace, catalogItemId)}?action=install");

    public async Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var manifests = ManifestsDirectory();
            if (!fileSystem.DirectoryExists(manifests))
            {
                return ProviderResult.NotAvailable("Epic Games Launcher is not installed.");
            }

            var diagnostics = new List<ProviderDiagnostic>();
            var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
            foreach (var file in fileSystem.GetFiles(manifests, "*.item"))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var entry = ParseManifest(await fileSystem.ReadAllTextAsync(file, ct).ConfigureAwait(false));
                    if (entry is not null)
                    {
                        entries.TryAdd(entry.Ref.ExternalId, entry);
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    diagnostics.Add(new(DiagnosticSeverity.Warning, $"Epic: skipped unreadable manifest {WindowsPath.FileName(file)}", ex.Message));
                }
            }

            return ProviderResult.FromScan(entries.Values.ToList(), diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed("Epic: could not read the installed games.", ex);
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Epic owned library needs the Epic sign-in (M5)."));

    internal string ManifestsDirectory()
    {
        var dataPath = registry.GetString(RegistryHive.LocalMachine, RegistryKey, RegistryValue);
        var root = string.IsNullOrWhiteSpace(dataPath)
            ? WindowsPath.Join(folders.ProgramData, @"Epic\EpicGamesLauncher\Data")
            : WindowsPath.Normalize(dataPath);
        return WindowsPath.Join(root, "Manifests");
    }

    /// <returns>The entry, or null for DLC, engines, plugins and incomplete installs.</returns>
    internal static LibraryEntry? ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        var appName = GetString(root, "AppName");
        var title = GetString(root, "DisplayName");
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidDataException("Manifest is missing AppName or DisplayName.");
        }

        if (GetBool(root, "bIsIncompleteInstall"))
        {
            return null;
        }

        var mainGame = GetString(root, "MainGameAppName");
        if (!string.IsNullOrEmpty(mainGame) && !string.Equals(mainGame, appName, StringComparison.Ordinal))
        {
            return null; // DLC
        }

        var categories = root.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
            ? cats.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
            : [];
        if (categories.Any(c => _excludedCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
        {
            return null;
        }

        var kind = categories.Contains("software", StringComparer.OrdinalIgnoreCase) && !categories.Contains("games", StringComparer.OrdinalIgnoreCase)
            ? ItemKind.App
            : ItemKind.Game;
        var ns = GetString(root, "CatalogNamespace");
        var catalogItemId = GetString(root, "CatalogItemId");
        var installLocation = GetString(root, "InstallLocation");

        var extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["appName"] = appName };
        AddIfPresent(extra, "namespace", ns);
        AddIfPresent(extra, "catalogItemId", catalogItemId);
        AddIfPresent(extra, "launchExecutable", GetString(root, "LaunchExecutable"));

        return new LibraryEntry(
            new ItemRef(SourceId, appName),
            kind,
            title.Trim(),
            InstallState.Installed,
            string.IsNullOrWhiteSpace(installLocation) ? null : WindowsPath.Normalize(installLocation),
            LaunchFor(appName, ns, catalogItemId),
            InstallFor(appName, ns, catalogItemId),
            extra);
    }

    // "{namespace}:{catalogItemId}:{appName}" with ':' encoded, or just the app name when the catalog ids are unknown.
    private static string AppPath(string appName, string? ns, string? catalogItemId) =>
        string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(catalogItemId)
            ? Uri.EscapeDataString(appName)
            : $"{Uri.EscapeDataString(ns)}%3A{Uri.EscapeDataString(catalogItemId)}%3A{Uri.EscapeDataString(appName)}";

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static void AddIfPresent(Dictionary<string, string> extra, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            extra[key] = value;
        }
    }
}
