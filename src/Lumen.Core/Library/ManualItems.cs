// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Library;

/// <summary>
/// A game or app the user added by hand: a file (.exe, .lnk, .url) or a URL. Mirrors
/// <c>config.json → manualItems</c> so it can sync in M8. Paths may contain %ENV% variables.
/// </summary>
public sealed record ManualItem(Guid Id, ItemKind Kind, string Title, string? Path, string? Url, string? Arguments)
{
    public const string Source = "manual";

    public ItemRef Ref => new(Source, Id.ToString("D"));
}

/// <summary>Stores manual items locally (SQLite <c>manual_items</c>).</summary>
public interface IManualItemStore
{
    Task<IReadOnlyList<ManualItem>> GetAllAsync(CancellationToken ct);

    /// <summary>Inserts or replaces the item with the same id.</summary>
    Task SaveAsync(ManualItem item, CancellationToken ct);

    Task RemoveAsync(Guid id, CancellationToken ct);
}

public enum ManualItemError
{
    None,
    TitleMissing,
    TitleTooLong,
    TargetMissing,

    /// <summary>Only .exe, .lnk and .url files can be added.</summary>
    UnsupportedFileType,

    /// <summary>The path is not absolute.</summary>
    RelativePath,

    /// <summary>The URL is malformed or uses a scheme that is blocked for safety.</summary>
    UnsupportedUrl,
    ArgumentsTooLong,

    /// <summary>Arguments only apply to .exe files.</summary>
    ArgumentsNotAllowed,
}

/// <summary>Turns user input (typed, browsed or dropped) into a valid <see cref="ManualItem"/>.</summary>
public static partial class ManualItemValidator
{
    public const int MaxTitleLength = 120;
    public const int MaxArgumentsLength = 2000;

    public static IReadOnlySet<string> AllowedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".url" };

    /// <summary>
    /// URL schemes refused even though the user typed them: they run code, open local files or abuse
    /// Windows protocol handlers rather than opening a website or a launcher.
    /// </summary>
    public static IReadOnlySet<string> BlockedSchemes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "file", "javascript", "vbscript", "data", "shell", "ms-msdt", "search-ms", "search",
        "ms-officecmd", "ms-word", "ms-excel", "ms-powerpoint", "ms-cxh", "ms-cxh-full", "ms-appinstaller",
        "mk", "its", "ms-its", "hcp", "res", "about", "ldap", "ms-settings",
    };

    /// <summary>True when <paramref name="target"/> should be treated as a URL rather than a file path.</summary>
    public static bool LooksLikeUrl(string target)
    {
        // "C:\..." also parses as a URI with scheme "c"; a real scheme has at least two characters.
        var colon = target.IndexOf(':', StringComparison.Ordinal);
        // file: URLs count as URLs here so they hit the blocked-scheme check.
        return colon > 1 && Uri.TryCreate(target, UriKind.Absolute, out _);
    }

    /// <summary>
    /// Cleans user input: trims quotes/spaces and turns a bare web address typed without a scheme
    /// (<c>github.com/user/repo</c>, <c>www.example.com</c>) into <c>https://…</c>. File paths are left alone.
    /// </summary>
    public static string NormalizeTarget(string? target)
    {
        var t = target?.Trim().Trim('"').Trim() ?? string.Empty;
        if (t.Length == 0 || LooksLikeUrl(t) || IsAbsoluteWindowsPath(t) || t.Contains('\\', StringComparison.Ordinal) || t.StartsWith('%'))
        {
            return t;
        }

        return BareDomainPattern().IsMatch(t) && !NotATld.Contains(BareHost(t)[(BareHost(t).LastIndexOf('.') + 1)..])
            ? "https://" + t
            : t;
    }

    public static ManualItemError Validate(string? title, string? target, string? arguments)
    {
        title = title?.Trim();
        target = NormalizeTarget(target);
        arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();

        if (string.IsNullOrEmpty(title))
        {
            return ManualItemError.TitleMissing;
        }

        if (title.Length > MaxTitleLength)
        {
            return ManualItemError.TitleTooLong;
        }

        if (string.IsNullOrEmpty(target))
        {
            return ManualItemError.TargetMissing;
        }

        if (arguments is { Length: > MaxArgumentsLength })
        {
            return ManualItemError.ArgumentsTooLong;
        }

        if (LooksLikeUrl(target))
        {
            var uri = new Uri(target);
            if (BlockedSchemes.Contains(uri.Scheme) || !SchemePattern(uri.Scheme))
            {
                return ManualItemError.UnsupportedUrl;
            }

            return arguments is null ? ManualItemError.None : ManualItemError.ArgumentsNotAllowed;
        }

        var expanded = Environment.ExpandEnvironmentVariables(target);
        if (!IsAbsoluteWindowsPath(expanded))
        {
            return ManualItemError.RelativePath;
        }

        var extension = ExtensionOf(expanded);
        if (!AllowedExtensions.Contains(extension))
        {
            return ManualItemError.UnsupportedFileType;
        }

        return arguments is not null && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            ? ManualItemError.ArgumentsNotAllowed
            : ManualItemError.None;
    }

    /// <summary>Builds the item; call <see cref="Validate"/> first.</summary>
    public static ManualItem Create(Guid id, ItemKind kind, string title, string target, string? arguments)
    {
        var error = Validate(title, target, arguments);
        if (error != ManualItemError.None)
        {
            throw new ArgumentException($"Invalid manual item: {error}.");
        }

        target = NormalizeTarget(target);
        var args = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        return LooksLikeUrl(target)
            ? new ManualItem(id, kind, title.Trim(), null, target, null)
            : new ManualItem(id, kind, title.Trim(), target, null, args);
    }

    /// <summary>A sensible default title for a dropped/browsed target: file name or URL host.</summary>
    public static string SuggestTitle(string target)
    {
        target = NormalizeTarget(target);
        if (LooksLikeUrl(target))
        {
            var uri = new Uri(target);
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            return string.IsNullOrEmpty(host) ? uri.Scheme : host;
        }

        var fileName = FileNameOf(target);
        var extension = ExtensionOf(fileName);
        var name = fileName[..^extension.Length];
        return name.Length > MaxTitleLength ? name[..MaxTitleLength] : name;
    }

    /// <summary>Directory of a Windows path, or null. OS-agnostic (does not depend on the test machine's separators).</summary>
    public static string? DirectoryOf(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return i <= 0 ? null : path[..i];
    }

    // Paths are Windows paths even when tests run elsewhere: "C:\..." / "C:/..." or "\\server\share\...".
    private static bool IsAbsoluteWindowsPath(string path) =>
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
        || (path.StartsWith(@"\\", StringComparison.Ordinal) && path.Length > 2 && path[2] != '?' && path[2] != '.');

    private static string FileNameOf(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    private static string ExtensionOf(string path)
    {
        var name = FileNameOf(path);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[dot..];
    }

    // config.schema.json: ^[a-z][a-z0-9+.-]*  (Uri normalizes schemes to lowercase)
    private static bool SchemePattern(string scheme) =>
        scheme.Length > 1 && char.IsAsciiLetterLower(scheme[0])
        && scheme.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '+' or '.' or '-');

    // "notepad.exe" looks like a host name too; these endings mean a file, not a website.
    private static readonly HashSet<string> NotATld = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "lnk", "url", "bat", "cmd", "ps1", "msi", "scr", "vbs", "js", "jar", "dll", "txt", "ini",
    };

    private static string BareHost(string target)
    {
        var end = target.IndexOfAny(['/', '?', '#', ':']);
        return end < 0 ? target : target[..end];
    }

    // host.with.dots[:port][/path...] where the last label is letters only (a TLD).
    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:[a-z0-9-]+\.)+[a-z]{2,63}(?::\d{1,5})?(?:[/?#].*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex BareDomainPattern();
}
