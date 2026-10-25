// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Manual;

/// <summary>
/// Items the user added by hand. A file that does not exist on this PC (e.g. synced from another machine,
/// or uninstalled) is listed as not installed with no action, never deleted.
/// </summary>
public sealed class ManualItemsProvider(IManualItemStore store, IFileSystem fileSystem) : ILibraryProvider
{
    public string Source => ManualItem.Source;

    public string DisplayName => "Manual";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    public async Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var items = await store.GetAllAsync(ct).ConfigureAwait(false);
            return ProviderResult.FromScan(items.Select(ToEntry).ToList(), []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed("Manual items could not be read.", ex);
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Manual items have no owned library."));

    internal LibraryEntry ToEntry(ManualItem item)
    {
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.Url is not null)
        {
            extra["url"] = item.Url;
            return new LibraryEntry(item.Ref, item.Kind, item.Title, InstallState.Installed, null,
                new LaunchSpec(LaunchKind.Uri, item.Url), null, extra);
        }

        var path = Environment.ExpandEnvironmentVariables(item.Path!);
        extra["path"] = item.Path!;
        if (!fileSystem.FileExists(path))
        {
            // Not on this PC: shown as unavailable, no launch or install action.
            return new LibraryEntry(item.Ref, item.Kind, item.Title, InstallState.NotInstalled, null, null, null, extra);
        }

        var isExe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return new LibraryEntry(
            item.Ref,
            item.Kind,
            item.Title,
            InstallState.Installed,
            isExe ? ManualItemValidator.DirectoryOf(path) : null,
            new LaunchSpec(LaunchKind.Executable, path, isExe ? item.Arguments : null, isExe ? ManualItemValidator.DirectoryOf(path) : null),
            null,
            extra);
    }
}
