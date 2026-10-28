// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using System.Text.Json.Nodes;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Epic;

/// <summary>
/// Epic Games Store: installed games from the launcher's manifests (<c>*.item</c> JSON files), no login; owned games
/// from the optional Epic sign-in (<see cref="EpicAccount"/>), titled from Epic's catalog (cached in
/// <see cref="IMetadataCache"/> so each item is looked up once). Only reads Epic's files and registry.
/// </summary>
public sealed class EpicProvider(
    IRegistryReader registry,
    IFileSystem fileSystem,
    IKnownFolders folders,
    EpicAccount? account = null,
    IMetadataCache? metadata = null,
    TimeProvider? time = null) : ILibraryProvider, IWatchableProvider, ICachingProvider
{
    /// <summary>The owned list barely changes; automatic rescans reuse it for this long (manual Rescan refreshes).</summary>
    public static readonly TimeSpan OwnedCacheLifetime = TimeSpan.FromMinutes(10);

    private const int ParallelLookups = 6;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private (ProviderResult Result, DateTimeOffset At)? _ownedCache;

    public void InvalidateCache() => _ownedCache = null;

    /// <summary>The launcher writes/removes a .item manifest when an install finishes or a game is uninstalled.</summary>
    public Task<IReadOnlyList<WatchTarget>> GetWatchTargetsAsync(CancellationToken ct)
    {
        var manifests = ManifestsDirectory();
        IReadOnlyList<WatchTarget> targets = fileSystem.DirectoryExists(manifests) ? [new WatchTarget(manifests, "*.item")] : [];
        return Task.FromResult(targets);
    }

    public const string SourceId = "epic";

    // Verified on Windows 11 (2026-10): manifests live in %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests;
    // the AppDataPath value below was absent there, so it is only an override for custom data folders.
    internal const string RegistryKey = @"SOFTWARE\WOW6432Node\Epic Games\EpicGamesLauncher";
    internal const string RegistryValue = "AppDataPath";

    private static readonly string[] _excludedCategories = ["addons", "engines", "plugins", "plugin"];

    public string Source => SourceId;

    public string DisplayName => "Epic Games";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan | ProviderCapabilities.OwnedLibrary;

    /// <summary>Launcher URI; verified on Windows 11 (2026-10): starts the game through the Epic launcher.</summary>
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

    public async Task<ProviderResult> ScanOwnedAsync(CancellationToken ct)
    {
        try
        {
            if (account is null)
            {
                return ProviderResult.NotConfigured("Epic sign-in not available.");
            }

            if (_ownedCache is { } cached && _time.GetUtcNow() - cached.At < OwnedCacheLifetime)
            {
                return cached.Result;
            }

            var owned = await account.GetOwnedAsync(ct).ConfigureAwait(false);
            var scan = owned.Status switch
            {
                EpicOwnedStatus.NotConnected => ProviderResult.NotConfigured("Not signed in to Epic."),
                EpicOwnedStatus.NeedsReconnect => ProviderResult.Failed("Epic: the sign-in was rejected; connect again in Settings."),
                EpicOwnedStatus.Failed => ProviderResult.Failed($"Epic library unavailable ({owned.Detail})."),
                _ => await ToOwnedResultAsync(owned.Records, ct).ConfigureAwait(false),
            };

            _ownedCache = scan.Status == ProviderStatus.Ok ? (scan, _time.GetUtcNow()) : null;
            return scan;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed("Epic: could not read the owned library.", ex);
        }
    }

    /// <summary>Titles every record from the cache or the catalog; DLC, add-ons and engines are dropped.</summary>
    internal async Task<ProviderResult> ToOwnedResultAsync(IReadOnlyList<EpicLibraryRecord> records, CancellationToken ct)
    {
        var unique = records.DistinctBy(r => r.AppName).ToList();
        static string Key(EpicLibraryRecord r) => "epic:catalog:" + r.CatalogItemId;
        var known = metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(await metadata.GetManyAsync(unique.Select(Key), ct).ConfigureAwait(false));

        var missing = unique.Where(r => !known.ContainsKey(Key(r))).ToList();
        var fetched = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = 0;
        using (var gate = new SemaphoreSlim(ParallelLookups))
        {
            await Task.WhenAll(missing.Select(async record =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var info = await account!.LookupCatalogAsync(record.Namespace, record.CatalogItemId, ct).ConfigureAwait(false);
                    lock (fetched)
                    {
                        if (info.Value is { } value)
                        {
                            fetched[Key(record)] = new JsonObject { ["t"] = value.Title, ["a"] = value.IsApp, ["s"] = value.Skip }.ToJsonString();
                        }
                        else
                        {
                            failures++;
                        }
                    }
                }
                finally
                {
                    gate.Release();
                }
            })).ConfigureAwait(false);
        }

        if (metadata is not null && fetched.Count > 0)
        {
            await metadata.SetManyAsync(fetched, ct).ConfigureAwait(false);
        }

        foreach (var (key, value) in fetched)
        {
            known[key] = value;
        }

        var entries = new List<LibraryEntry>();
        foreach (var record in unique)
        {
            if (!known.TryGetValue(Key(record), out var json) || JsonNode.Parse(json) is not JsonObject info
                || info["s"]?.GetValue<bool>() == true || info["t"]?.GetValue<string>() is not { Length: > 0 } title)
            {
                continue;
            }

            var extra = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["appName"] = record.AppName,
                ["namespace"] = record.Namespace,
                ["catalogItemId"] = record.CatalogItemId,
            };
            entries.Add(new LibraryEntry(
                new ItemRef(SourceId, record.AppName),
                info["a"]?.GetValue<bool>() == true ? ItemKind.App : ItemKind.Game,
                title.Trim(),
                InstallState.NotInstalled,
                null,
                LaunchFor(record.AppName, record.Namespace, record.CatalogItemId),
                InstallFor(record.AppName, record.Namespace, record.CatalogItemId),
                extra));
        }

        IReadOnlyList<ProviderDiagnostic> diagnostics = failures == 0
            ? []
            : [new ProviderDiagnostic(DiagnosticSeverity.Warning, $"Epic: {failures} owned item(s) couldn't be looked up in the catalog; they'll be retried.")];
        return ProviderResult.FromScan(entries, diagnostics);
    }

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
