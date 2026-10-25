// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Xml;
using System.Xml.Linq;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Ea;

/// <summary>
/// EA app: installed games from their uninstall entries plus each game's <c>__Installer\installerdata.xml</c>,
/// which holds the content id the EA app uses to launch it. Read-only.
/// </summary>
public sealed class EaProvider(IRegistryReader registry, IFileSystem fileSystem, IKnownFolders folders) : ILibraryProvider
{
    public const string SourceId = "ea";

    // VERIFY: community-documented (Lutris, open-source launchers), not yet checked on a machine with the EA app.
    internal const string InstallerDataRelativePath = @"__Installer\installerdata.xml";
    internal const string EaAppRelativePath = @"Electronic Arts\EA Desktop\EA Desktop\EADesktop.exe";

    public string Source => SourceId;

    public string DisplayName => "EA app";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    /// <summary>VERIFY: the EA app still handles origin2:// links with the installer content id.</summary>
    public static LaunchSpec LaunchFor(string contentId) =>
        new(LaunchKind.Uri, $"origin2://game/launch?offerIds={Uri.EscapeDataString(contentId)}");

    public async Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var eaApp = WindowsPath.Join(folders.ProgramFiles, EaAppRelativePath);
            var hasEaApp = fileSystem.FileExists(eaApp);
            var candidates = UninstallEntries.ReadAll(registry).Where(IsEaGame).ToList();
            if (candidates.Count == 0 && !hasEaApp)
            {
                return ProviderResult.NotAvailable("EA app is not installed.");
            }

            var diagnostics = new List<ProviderDiagnostic>();
            var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var dir = WindowsPath.Normalize(candidate.InstallLocation!);
                var xmlPath = WindowsPath.Join(dir, InstallerDataRelativePath);
                if (!fileSystem.FileExists(xmlPath))
                {
                    continue; // not an EA app game (or a leftover entry)
                }

                InstallerData data;
                try
                {
                    data = ParseInstallerData(await fileSystem.ReadAllTextAsync(xmlPath, ct).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new(DiagnosticSeverity.Warning, $"EA: skipped unreadable installer data for {candidate.DisplayName ?? candidate.KeyName}", ex.Message));
                    continue;
                }

                var title = FirstNonEmpty(candidate.DisplayName, data.Title, WindowsPath.FileName(dir))!.Trim();
                var id = data.ContentId ?? candidate.KeyName;
                LaunchSpec? launch = data.ContentId is not null
                    ? LaunchFor(data.ContentId)
                    : hasEaApp ? new LaunchSpec(LaunchKind.Executable, eaApp) : null;
                var extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["uninstallKey"] = candidate.KeyName };
                if (data.ContentId is not null)
                {
                    extra["contentId"] = data.ContentId;
                }

                entries.TryAdd(id, new LibraryEntry(new ItemRef(SourceId, id), ItemKind.Game, title, InstallState.Installed, dir, launch, null, extra));
            }

            return ProviderResult.FromScan(entries.Values.ToList(), diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed("EA: could not read the installed games.", ex);
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("EA owned library is not supported in v1."));

    internal sealed record InstallerData(string? ContentId, string? Title);

    /// <summary>Reads the content id and title from installerdata.xml (both the DiP 4.x and older layouts).</summary>
    internal static InstallerData ParseInstallerData(string xml)
    {
        // Untrusted file: no DTDs, no external resolution (prevents XXE / network access).
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var doc = XDocument.Load(reader);

        var contentId = doc.Descendants().Where(e => e.Name.LocalName == "contentID")
            .Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);

        var titles = doc.Descendants().Where(e => e.Name.LocalName == "gameTitle").ToList();
        var title = titles.FirstOrDefault(t => (string?)t.Attribute("locale") == "en_US")?.Value
            ?? titles.FirstOrDefault()?.Value
            ?? doc.Descendants().Where(e => e.Name.LocalName == "localeInfo" && (string?)e.Attribute("locale") == "en_US")
                .Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value;

        return new InstallerData(contentId, string.IsNullOrWhiteSpace(title) ? null : title.Trim());
    }

    /// <summary>EA installers register a cleanup tool under Common Files\EAInstaller; the publisher is a fallback.</summary>
    private static bool IsEaGame(UninstallEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.InstallLocation)
        && ((entry.UninstallString?.Contains("EAInstaller", StringComparison.OrdinalIgnoreCase) ?? false)
            || (entry.Publisher?.StartsWith("Electronic Arts", StringComparison.OrdinalIgnoreCase) ?? false));

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
