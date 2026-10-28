// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Core.Library;

/// <summary>Where "Uninstall" takes the user. Lumen never deletes files or runs an uninstaller itself.</summary>
public enum UninstallRoute
{
    /// <summary>The store's own uninstall flow for this game (the store asks for confirmation).</summary>
    Store,

    /// <summary>The store's library, where the user picks Uninstall on the game.</summary>
    StoreLibrary,

    /// <summary>Windows Settings → Apps → Installed apps, where the user finds the game and picks Uninstall.</summary>
    WindowsSettings,
}

public sealed record UninstallAction(LaunchSpec Spec, UninstallRoute Route);

/// <summary>Secondary actions of the detail view, as launch specs (so they run through the same shell launcher).</summary>
public static class EntryActions
{
    /// <summary>
    /// Hands uninstalling over to the store (security rules 2 and 3: Lumen writes nothing of the store's and runs no
    /// uninstaller, so nothing can ask for admin on its behalf). Steam and Ubisoft have a per-game link, Epic opens its
    /// library, and stores without a link go to Windows Settings → Apps. Manual items have none (they are the user's
    /// own shortcuts; "Remove" takes them out of Lumen). Null when the entry is not installed.
    /// </summary>
    public static UninstallAction? Uninstall(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsInstalled)
        {
            return null;
        }

        var id = entry.Ref.ExternalId;

        // Verified on Windows 11 (2026-10): Steam and Ubisoft open their own uninstall confirmation, Epic opens its
        // library, ms-settings:appsfeatures opens Settings → Apps. Links taken from Playnite's store plugins.
        return entry.Ref.Source switch
        {
            "steam" when IsNumber(id) => new(new LaunchSpec(LaunchKind.Uri, $"steam://uninstall/{id}"), UninstallRoute.Store),
            "ubisoft" when IsNumber(id) => new(new LaunchSpec(LaunchKind.Uri, $"uplay://uninstall/{id}"), UninstallRoute.Store),
            "epic" => new(new LaunchSpec(LaunchKind.Uri, "com.epicgames.launcher://store/library"), UninstallRoute.StoreLibrary),
            "gog" or "ea" or "battlenet" or "xbox" => new(new LaunchSpec(LaunchKind.Uri, "ms-settings:appsfeatures"), UninstallRoute.WindowsSettings),
            _ => null,
        };
    }

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

    private static bool IsNumber(string value) => value.Length > 0 && value.All(char.IsAsciiDigit);

    // Package family names are letters, digits, '.', '_' and '-': nothing that could break out of the URI.
    private static bool IsSafeToken(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
