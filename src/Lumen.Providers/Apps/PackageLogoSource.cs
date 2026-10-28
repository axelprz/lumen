// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Platform;
using Lumen.Providers.Xbox;

namespace Lumen.Providers.Apps;

/// <summary>
/// Icons for packaged apps and Xbox games: the logo the package ships, as named in <c>AppxManifest.xml</c>
/// (<c>Square44x44Logo</c>, then <c>Square150x150Logo</c>, then the Store logo). Packages ship each logo in several
/// sizes and styles (<c>Logo.targetsize-256_altform-unplated.png</c>, <c>Logo.scale-200.png</c>…); the largest one
/// up to 256 px wins, preferring "unplated" (transparent, no colored tile behind it). Read-only, no network.
/// </summary>
public sealed class PackageLogoSource(IFileSystem fileSystem) : ILocalArtworkSource
{
    private const int WantedSize = 256;

    public bool CanHandle(LibraryEntry entry, ArtworkKind kind) =>
        kind == ArtworkKind.Icon
        && entry.Ref.Source is StoreAppsProvider.SourceId or XboxProvider.SourceId
        && !string.IsNullOrWhiteSpace(entry.InstallDirectory);

    public async Task<byte[]?> GetImageAsync(LibraryEntry entry, ArtworkKind kind, CancellationToken ct)
    {
        var dir = WindowsPath.Normalize(entry.InstallDirectory!);
        var manifestPath = WindowsPath.Join(dir, XboxProvider.ManifestFile);
        if (!fileSystem.FileExists(manifestPath))
        {
            return null;
        }

        XDocument manifest;
        try
        {
            manifest = XboxProvider.LoadXml(await fileSystem.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false));
        }
        catch (XmlException)
        {
            return null;
        }

        foreach (var logical in LogoPaths(manifest, entry.Extra.GetValueOrDefault("appId")))
        {
            if (BestVariant(fileSystem, dir, logical) is { } file)
            {
                return await fileSystem.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    /// <summary>Logo paths for the app, best first: the square icon, the medium tile, then the package's Store logo.</summary>
    internal static IEnumerable<string> LogoPaths(XDocument manifest, string? appId)
    {
        var apps = manifest.Descendants().Where(e => e.Name.LocalName == "Application").ToList();
        var app = apps.FirstOrDefault(a => (string?)a.Attribute("Id") == appId) ?? apps.FirstOrDefault();
        var visual = app?.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
        foreach (var attribute in new[] { "Square44x44Logo", "Square150x150Logo" })
        {
            if ((string?)visual?.Attribute(attribute) is { Length: > 0 } path)
            {
                yield return path;
            }
        }

        var storeLogo = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Properties")
            ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Logo")?.Value;
        if (!string.IsNullOrWhiteSpace(storeLogo))
        {
            yield return storeLogo.Trim();
        }
    }

    /// <summary>The best existing file for a logical asset path such as <c>Assets\Square44x44Logo.png</c>.</summary>
    internal static string? BestVariant(IFileSystem fileSystem, string packageDir, string logicalPath)
    {
        var logical = WindowsPath.Normalize(logicalPath);
        if (logical.Contains("..", StringComparison.Ordinal) || logical.Contains(':', StringComparison.Ordinal))
        {
            return null; // stay inside the package folder
        }

        var fileName = WindowsPath.FileName(logical);
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0)
        {
            return null;
        }

        var stem = fileName[..dot];
        var ext = fileName[dot..];
        var folder = WindowsPath.Directory(logical) is { } sub ? WindowsPath.Join(packageDir, sub) : packageDir;
        var baseSize = BaseSize(stem);

        return fileSystem.GetFiles(folder, stem + "*" + ext)
            .Select(path => (Path: path, Variant: Parse(WindowsPath.FileName(path)[stem.Length..^ext.Length], baseSize)))
            .Where(c => c.Variant is not null)
            .OrderByDescending(c => c.Variant!.Value.Unplated)
            .ThenByDescending(c => Math.Min(c.Variant!.Value.Size, WantedSize))
            .ThenBy(c => c.Variant!.Value.Size) // between two sizes over 256, the smaller is enough
            .Select(c => c.Path)
            .FirstOrDefault();
    }

    // ".targetsize-256_altform-unplated" → (256, unplated). High-contrast and light-theme variants are skipped (null).
    private static (int Size, bool Unplated)? Parse(string qualifiers, int baseSize)
    {
        var size = baseSize;
        var unplated = false;
        foreach (var part in qualifiers.Split(['.', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = part.ToLowerInvariant();
            if (lower.StartsWith("contrast-", StringComparison.Ordinal) || lower is "altform-lightunplated" or "theme-light")
            {
                return null;
            }

            if (lower.StartsWith("targetsize-", StringComparison.Ordinal) && int.TryParse(lower["targetsize-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var target))
            {
                size = target;
            }
            else if (lower.StartsWith("scale-", StringComparison.Ordinal) && int.TryParse(lower["scale-".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
            {
                size = baseSize * scale / 100;
            }
            else if (lower == "altform-unplated")
            {
                unplated = true;
            }
        }

        return (size, unplated);
    }

    // "Square44x44Logo" → 44, "Square150x150Logo" → 150; Store logos are 50×50.
    private static int BaseSize(string stem)
    {
        var digits = new string(stem.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size > 0 ? size : 50;
    }
}
