// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Providers.Apps;

/// <summary>
/// Apps from the Start menu (all users + current user): <c>.lnk</c> shortcuts to programs, plus <c>.url</c> shortcuts
/// with app protocols. These are candidates: they reach the library only once the user adds them in "Discover apps".
/// Ref id is the path relative to the Start menu's Programs folder (same on every PC), e.g.
/// <c>startmenu:7-Zip/7-Zip File Manager.lnk</c>. Names come from the folder's <c>desktop.ini</c> when Windows shows a
/// translated one (e.g. "Character Map.lnk" shown as "Mapa de caracteres"). Read-only; launched by opening the
/// shortcut itself.
/// </summary>
public sealed class StartMenuProvider(
    IKnownFolders folders,
    IFileSystem fileSystem,
    IShortcutReader shortcuts,
    IIndirectStrings? names = null,
    AppJunkFilter? junk = null)
    : ILibraryProvider
{
    public const string SourceId = "startmenu";

    /// <summary>Start menu folders are shallow; this only guards against odd junctions.</summary>
    private const int MaxDepth = 6;

    private readonly AppJunkFilter _junk = junk ?? AppJunkFilter.Default;

    public string Source => SourceId;

    public string DisplayName => "Start menu";

    public ProviderCapabilities Capabilities => ProviderCapabilities.InstalledScan;

    // Reading hundreds of shortcuts through COM: keep it off the caller's (possibly UI) thread.
    public Task<ProviderResult> ScanInstalledAsync(CancellationToken ct) => Task.Run(() => Scan(ct), ct);

    public Task<ProviderResult> ScanOwnedAsync(CancellationToken ct) =>
        Task.FromResult(ProviderResult.NotConfigured("Start menu apps have no owned library."));

    private ProviderResult Scan(CancellationToken ct)
    {
        try
        {
            var diagnostics = new List<ProviderDiagnostic>();
            var byId = new Dictionary<string, LibraryEntry>(StringComparer.OrdinalIgnoreCase);

            // The current user's copy of a shortcut wins over the all-users one with the same relative path.
            foreach (var root in new[] { folders.UserStartMenuPrograms, folders.CommonStartMenuPrograms })
            {
                foreach (var file in Walk(root, root, 0))
                {
                    ct.ThrowIfCancellationRequested();
                    var id = Relative(root, file);
                    if (byId.ContainsKey(id))
                    {
                        continue;
                    }

                    if (ReadEntry(file, id, diagnostics) is { } entry)
                    {
                        byId[id] = entry;
                    }
                }
            }

            // Copies of the same shortcut (same program and arguments, e.g. in two folders): keep the one nearest the
            // top of the Start menu. Different arguments are different things (cmd.exe consoles, VLC's skinned mode).
            var unique = byId.Values
                .GroupBy(e => e.Extra.TryGetValue("target", out var target) ? target + "|" + e.Extra.GetValueOrDefault("arguments") : e.Ref.ExternalId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(e => e.Ref.ExternalId.Count(c => c == '/')).ThenBy(e => e.Title.Length).ThenBy(e => e.Ref.ExternalId, StringComparer.OrdinalIgnoreCase).First())
                .ToList();
            return ProviderResult.FromScan(unique, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Failed("Start menu: could not list shortcuts.", ex);
        }
    }

    private IEnumerable<string> Walk(string root, string dir, int depth)
    {
        if (depth > MaxDepth || !fileSystem.DirectoryExists(dir))
        {
            yield break;
        }

        foreach (var file in fileSystem.GetFiles(dir, "*.lnk").Concat(fileSystem.GetFiles(dir, "*.url")))
        {
            yield return file;
        }

        _localizedNames.Remove(dir);

        foreach (var sub in fileSystem.GetDirectories(dir))
        {
            if (!_junk.IsSkippedFolder(WindowsPath.FileName(sub)))
            {
                foreach (var file in Walk(root, sub, depth + 1))
                {
                    yield return file;
                }
            }
        }
    }

    // desktop.ini [LocalizedFileNames] per folder, read once per folder while walking it.
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _localizedNames = new(StringComparer.OrdinalIgnoreCase);

    private LibraryEntry? ReadEntry(string file, string id, List<ProviderDiagnostic> diagnostics)
    {
        var fileName = WindowsPath.FileName(file);
        var name = LocalizedName(file, fileName) ?? Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(name) || _junk.IsJunkName(name))
        {
            return null;
        }

        return file.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
            ? ReadUrlShortcut(file, id, name)
            : ReadLinkShortcut(file, id, name, diagnostics);
    }

    private LibraryEntry? ReadLinkShortcut(string file, string id, string name, List<ProviderDiagnostic> diagnostics)
    {
        var target = shortcuts.Read(file);
        if (target is null)
        {
            diagnostics.Add(new(DiagnosticSeverity.Info, $"Start menu: could not read {id}."));
            return null;
        }

        // Shortcuts to things that aren't files (Control Panel items, shell folders) and to missing or junk targets.
        if (target.TargetPath is not { } path || _junk.IsJunkTarget(path) || !fileSystem.FileExists(path))
        {
            return null;
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target"] = WindowsPath.Normalize(path),
            ["shortcut"] = file,
        };
        if (target.IconPath is { } icon)
        {
            extra["icon"] = icon;
        }

        if (target.Arguments is { } arguments)
        {
            extra["arguments"] = arguments;
        }

        return new LibraryEntry(
            new ItemRef(SourceId, id),
            ItemKind.App,
            name.Trim(),
            InstallState.Installed,
            WindowsPath.Directory(path),
            new LaunchSpec(LaunchKind.Executable, file), // opening the shortcut keeps its arguments and working folder
            null,
            extra);
    }

    private LibraryEntry? ReadUrlShortcut(string file, string id, string name)
    {
        var url = ReadUrl(file);
        if (url is null || _junk.IsSkippedUrl(url))
        {
            return null;
        }

        return new LibraryEntry(
            new ItemRef(SourceId, id),
            ItemKind.App,
            name.Trim(),
            InstallState.Installed,
            null,
            new LaunchSpec(LaunchKind.Executable, file),
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["shortcut"] = file, ["url"] = url });
    }

    // .url files are INI text: [InternetShortcut] URL=...
    private string? ReadUrl(string file)
    {
        try
        {
            var text = fileSystem.ReadAllTextAsync(file, CancellationToken.None).GetAwaiter().GetResult();
            return text.Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..]
                .Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? LocalizedName(string file, string fileName)
    {
        if (names is null || WindowsPath.Directory(file) is not { } dir)
        {
            return null;
        }

        if (!_localizedNames.TryGetValue(dir, out var map))
        {
            map = ReadDesktopIni(WindowsPath.Join(dir, "desktop.ini"));
            _localizedNames[dir] = map;
        }

        return map.TryGetValue(fileName, out var value) ? names.Resolve(value) : null;
    }

    /// <summary>The <c>[LocalizedFileNames]</c> section: file name → indirect string ("@shell32.dll,-22020").</summary>
    internal IReadOnlyDictionary<string, string> ReadDesktopIni(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!fileSystem.FileExists(path))
        {
            return map;
        }

        try
        {
            var inSection = false;
            foreach (var raw in fileSystem.ReadAllTextAsync(path, CancellationToken.None).GetAwaiter().GetResult().Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line.Equals("[LocalizedFileNames]", StringComparison.OrdinalIgnoreCase);
                }
                else if (inSection && line.IndexOf('=', StringComparison.Ordinal) is > 0 and var eq)
                {
                    map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No translated names for this folder; file names are used.
        }

        return map;
    }

    private static string Relative(string root, string file) =>
        WindowsPath.Normalize(file)[(WindowsPath.Normalize(root).Length + 1)..].Replace('\\', '/');
}
