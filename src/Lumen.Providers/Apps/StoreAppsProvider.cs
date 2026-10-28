// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Xml;
using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Providers.Xbox;

namespace Lumen.Providers.Apps;

/// <summary>
/// Packaged apps (Microsoft Store / MSIX) that show up in Start, i.e. what <c>shell:AppsFolder</c> lists for packages.
/// Games are left to <see cref="XboxProvider"/>. These are candidates for "Discover apps", like Start menu shortcuts.
/// Ref id is the AUMID (<c>appsfolder:Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</c>). Read-only.
/// </summary>
public sealed class StoreAppsProvider(IPackageCatalog packages, IFileSystem fileSystem, AppJunkFilter? junk = null) : ILibraryProvider
{
    public const string SourceId = "appsfolder";

    private readonly AppJunkFilter _junk = junk ?? AppJunkFilter.Default;

    public string Source => SourceId;

    public string DisplayName => "Microsoft Store apps";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

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
                if (fileSystem.FileExists(WindowsPath.Join(dir, XboxProvider.GameConfigFile)) || XboxProvider.KnownStoreGames.Contains(package.FamilyName))
                {
                    continue; // a game: the Xbox provider shows it
                }

                try
                {
                    if (await ReadAppAsync(package, dir, ct).ConfigureAwait(false) is { } entry)
                    {
                        entries.TryAdd(entry.Ref, entry);
                    }
                }
                catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new(DiagnosticSeverity.Info, $"Store apps: skipped {package.FamilyName}: its manifest could not be read.", ex.Message));
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
            return ProviderResult.Failed("Store apps: could not list installed packages.", ex);
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Store apps have no owned library."));

    private async Task<LibraryEntry?> ReadAppAsync(InstalledPackage package, string dir, CancellationToken ct)
    {
        var manifestPath = WindowsPath.Join(dir, XboxProvider.ManifestFile);
        if (!fileSystem.FileExists(manifestPath))
        {
            return null;
        }

        var manifest = XboxProvider.LoadXml(await fileSystem.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false));
        if (XboxProvider.FirstVisibleAppId(manifest) is not { } appId)
        {
            return null; // nothing in Start (background services, extensions)
        }

        // Only names Windows resolved: unresolved "ms-resource:" keys and bare family names aren't presentable.
        var title = package.DisplayName;
        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) || _junk.IsJunkName(title))
        {
            return null;
        }

        var aumid = $"{package.FamilyName}!{appId}";
        return new LibraryEntry(
            new ItemRef(SourceId, aumid),
            ItemKind.App,
            title.Trim(),
            InstallState.Installed,
            dir,
            new LaunchSpec(LaunchKind.ShellAppsFolder, aumid),
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["packageFamilyName"] = package.FamilyName,
                ["appId"] = appId,
            });
    }
}
