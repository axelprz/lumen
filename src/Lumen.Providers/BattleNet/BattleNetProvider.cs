// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using System.Text.RegularExpressions;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.BattleNet;

/// <summary>
/// Battle.net: installed games from their uninstall entries (the Blizzard uninstaller is called with
/// <c>--uid=&lt;product&gt;</c>). Launches through Battle.net with the product's launch code. Read-only.
/// </summary>
public sealed partial class BattleNetProvider(IRegistryReader registry, IFileSystem fileSystem, IKnownFolders folders) : ILibraryProvider
{
    public const string SourceId = "battlenet";

    // VERIFY: uninstall-entry layout and paths are community-documented, not yet checked on a machine with Battle.net.
    internal const string UninstallerName = "Blizzard Uninstaller.exe";
    internal const string LauncherRelativePath = @"Battle.net\Battle.net.exe";

    private const string ResourceName = "Lumen.Providers.Data.battlenet-products.json";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> _launchCodes = new(LoadLaunchCodes);

    public string Source => SourceId;

    public string DisplayName => "Battle.net";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    public static IReadOnlyDictionary<string, string> LaunchCodes => _launchCodes.Value;

    public Task<ProviderResult> ScanInstalledAsync(CancellationToken ct)
    {
        try
        {
            var launcher = FindLauncher();
            var games = UninstallEntries.ReadAll(registry)
                .Select(e => (Entry: e, Uid: ProductUid(e.UninstallString)))
                .Where(x => x.Uid is not null)
                .ToList();
            if (games.Count == 0 && launcher is null)
            {
                return Task.FromResult(ProviderResult.NotAvailable("Battle.net is not installed."));
            }

            var diagnostics = new List<ProviderDiagnostic>();
            var entries = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
            foreach (var (entry, uid) in games)
            {
                ct.ThrowIfCancellationRequested();
                var dir = string.IsNullOrWhiteSpace(entry.InstallLocation) ? null : WindowsPath.Normalize(entry.InstallLocation);
                if (dir is not null && !fileSystem.DirectoryExists(dir))
                {
                    diagnostics.Add(new(DiagnosticSeverity.Info, $"Battle.net: {entry.DisplayName ?? uid} is registered but its folder is missing."));
                    continue;
                }

                var code = LaunchCodeFor(uid!);
                if (code is null)
                {
                    diagnostics.Add(new(DiagnosticSeverity.Info, $"Battle.net: no launch code known for product '{uid}'; Play opens Battle.net."));
                }

                var extra = new Dictionary<string, string>(StringComparer.Ordinal) { ["uid"] = uid! };
                if (code is not null)
                {
                    extra["launchCode"] = code;
                }

                entries.TryAdd(uid!, new LibraryEntry(
                    new ItemRef(SourceId, uid!),
                    ItemKind.Game,
                    (entry.DisplayName ?? uid!).Trim(),
                    InstallState.Installed,
                    dir,
                    LaunchSpecFor(launcher, code),
                    null,
                    extra));
            }

            return Task.FromResult(ProviderResult.FromScan(entries.Values.ToList(), diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(ProviderResult.Failed("Battle.net: could not read the installed games.", ex));
        }
    }

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Battle.net owned library is not supported in v1."));

    /// <summary>The product uid from an uninstall string such as <c>"…\Blizzard Uninstaller.exe" --lang=enUS --uid=prometheus</c>.</summary>
    internal static string? ProductUid(string? uninstallString)
    {
        if (uninstallString is null || !uninstallString.Contains(UninstallerName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = UidPattern().Match(uninstallString);
        return match.Success ? match.Groups["uid"].Value.ToLowerInvariant() : null;
    }

    /// <summary>Exact uid first, then the part before an underscore (e.g. "wow_classic" → "wow").</summary>
    internal static string? LaunchCodeFor(string uid)
    {
        if (LaunchCodes.TryGetValue(uid, out var code))
        {
            return code;
        }

        var underscore = uid.IndexOf('_', StringComparison.Ordinal);
        return underscore > 0 && LaunchCodes.TryGetValue(uid[..underscore], out code) ? code : null;
    }

    internal static LaunchSpec? LaunchSpecFor(string? launcher, string? code) =>
        launcher is null
            ? null
            : code is null
                ? new LaunchSpec(LaunchKind.Executable, launcher)
                : new LaunchSpec(LaunchKind.ProcessWithArgs, launcher, $"--exec=\"launch {code}\"", WindowsPath.Directory(launcher));

    internal static IReadOnlyDictionary<string, string> ParseLaunchCodes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("products").EnumerateObject()
            .ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Value.GetString()!, StringComparer.Ordinal);
    }

    private string? FindLauncher()
    {
        foreach (var root in new[] { folders.ProgramFilesX86, folders.ProgramFiles })
        {
            var path = WindowsPath.Join(root, LauncherRelativePath);
            if (fileSystem.FileExists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> LoadLaunchCodes()
    {
        using var stream = typeof(BattleNetProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return ParseLaunchCodes(reader.ReadToEnd());
    }

    [GeneratedRegex(@"--uid=""?(?<uid>[A-Za-z0-9_\-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex UidPattern();
}
