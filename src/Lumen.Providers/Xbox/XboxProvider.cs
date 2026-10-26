// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Xbox;

/// <summary>
/// Xbox app / Microsoft Store games: packages installed for the current user that ship a
/// <c>MicrosoftGame.config</c> (GDK games), plus a small list of older Store games without it.
/// Launched through <c>shell:AppsFolder\{PackageFamilyName}!{AppId}</c>. Read-only.
/// </summary>
public sealed class XboxProvider(IPackageCatalog packages, IFileSystem fileSystem) : ILibraryProvider
{
    public const string SourceId = "xbox";

    internal const string GameConfigFile = "MicrosoftGame.config";
    internal const string ManifestFile = "AppxManifest.xml";

    private const string ResourceName = "Lumen.Providers.Data.store-games.json";
    private static readonly Lazy<IReadOnlySet<string>> _knownStoreGames = new(LoadKnownStoreGames);

    public string Source => SourceId;

    public string DisplayName => "Xbox";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    public static IReadOnlySet<string> KnownStoreGames => _knownStoreGames.Value;

    public async Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var all = await packages.GetForCurrentUserAsync(ct).ConfigureAwait(false);
            var diagnostics = new List<ProviderDiagnostic>();
            var entries = new Dictionary<ItemRef, LibraryEntry>();
            foreach (var package in all.Where(p => !p.IsFramework && !p.IsResource && !string.IsNullOrWhiteSpace(p.InstallLocation)))
            {
                ct.ThrowIfCancellationRequested();
                var dir = WindowsPath.Normalize(package.InstallLocation!);
                var configPath = WindowsPath.Join(dir, GameConfigFile);
                var isGdkGame = fileSystem.FileExists(configPath);
                if (!isGdkGame && !KnownStoreGames.Contains(package.FamilyName))
                {
                    continue;
                }

                try
                {
                    var entry = await ReadGameAsync(package, dir, isGdkGame ? configPath : null, ct).ConfigureAwait(false);
                    if (entry is not null)
                    {
                        entries.TryAdd(entry.Ref, entry);
                    }
                    else
                    {
                        diagnostics.Add(new(DiagnosticSeverity.Warning, $"Xbox: {package.FamilyName} has no launchable app in its manifest."));
                    }
                }
                catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new(DiagnosticSeverity.Warning, $"Xbox: skipped {package.FamilyName}: its manifest could not be read.", ex.Message));
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
            return ProviderResult.Failed("Xbox: could not list installed packages.", ex);
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Xbox owned library is not supported in v1."));

    private async Task<LibraryEntry?> ReadGameAsync(InstalledPackage package, string dir, string? configPath, CancellationToken ct)
    {
        var manifest = LoadXml(await fileSystem.ReadAllTextAsync(WindowsPath.Join(dir, ManifestFile), ct).ConfigureAwait(false));
        var appId = FirstVisibleAppId(manifest);
        if (appId is null)
        {
            return null;
        }

        string? configTitle = null;
        if (configPath is not null)
        {
            var config = LoadXml(await fileSystem.ReadAllTextAsync(configPath, ct).ConfigureAwait(false));
            configTitle = config.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals")?.Attribute("DefaultDisplayName")?.Value;
        }

        var manifestTitle = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Properties")
            ?.Elements().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value;

        // The API's resolved name first; "ms-resource:" values are unresolved resource keys, never shown.
        var title = new[] { package.DisplayName, configTitle, manifestTitle }
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t) && !t.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            ?? package.FamilyName.Split('_')[0];

        var aumid = $"{package.FamilyName}!{appId}";
        return new LibraryEntry(
            new ItemRef(SourceId, aumid),
            ItemKind.Game,
            title.Trim(),
            InstallState.Installed,
            dir,
            new LaunchSpec(LaunchKind.ShellAppsFolder, aumid),
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["packageFamilyName"] = package.FamilyName,
                ["appId"] = appId,
                ["gdk"] = configPath is null ? "false" : "true",
            });
    }

    /// <summary>The first Application that appears in Start (AppListEntry is not "none").</summary>
    internal static string? FirstVisibleAppId(XDocument manifest) =>
        manifest.Descendants()
            .Where(e => e.Name.LocalName == "Application")
            .Where(app => !app.Descendants().Any(v => v.Name.LocalName == "VisualElements"
                && string.Equals((string?)v.Attribute("AppListEntry"), "none", StringComparison.OrdinalIgnoreCase)))
            .Select(app => (string?)app.Attribute("Id"))
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

    /// <summary>Manifests are read like any untrusted file: no DTDs, no external resolution.</summary>
    internal static XDocument LoadXml(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        return XDocument.Load(reader);
    }

    private static IReadOnlySet<string> LoadKnownStoreGames()
    {
        using var stream = typeof(XboxProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("familyNames").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
