// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Core.Library;

/// <summary>Secondary actions of the detail view, as launch specs (so they run through the same shell launcher).</summary>
public static class EntryActions
{
    /// <summary>
    /// Opens the entry's page inside its store's own app, so Lumen contacts no extra host. Only stores whose link
    /// format is reliable are supported; others return null and the button is hidden.
    /// </summary>
    public static LaunchSpec? StorePage(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Ref.Source switch
        {
            "steam" when entry.Ref.ExternalId.All(char.IsAsciiDigit) =>
                new LaunchSpec(LaunchKind.Uri, $"steam://store/{entry.Ref.ExternalId}"),
            "xbox" when entry.Extra.TryGetValue("packageFamilyName", out var pfn) && IsSafeToken(pfn) =>
                new LaunchSpec(LaunchKind.Uri, $"ms-windows-store://pdp/?PFN={pfn}"),
            _ => null,
        };
    }

    /// <summary>Opens the install folder in File Explorer, or null when the entry has none or it no longer exists.</summary>
    public static LaunchSpec? OpenFolder(LibraryEntry entry, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(fileSystem);
        var dir = entry.InstallDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !fileSystem.DirectoryExists(dir))
        {
            return null;
        }

        // Quoted so paths with spaces work; explorer.exe only opens the folder, it runs nothing in it.
        return new LaunchSpec(LaunchKind.ProcessWithArgs, "explorer.exe", $"\"{dir.TrimEnd('\\')}\"");
    }

    // Package family names are letters, digits, '.', '_' and '-': nothing that could break out of the URI.
    private static bool IsSafeToken(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
