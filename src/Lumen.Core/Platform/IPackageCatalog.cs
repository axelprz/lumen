// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>An installed MSIX/AppX package (Microsoft Store, Xbox app, sideloaded).</summary>
/// <param name="FamilyName">e.g. <c>Microsoft.MicrosoftSolitaireCollection_8wekyb3d8bbwe</c>.</param>
/// <param name="DisplayName">Resolved display name, or null when Windows could not resolve it.</param>
/// <param name="InstallLocation">Folder holding AppxManifest.xml (and MicrosoftGame.config for GDK games).</param>
public sealed record InstalledPackage(
    string FamilyName,
    string FullName,
    string? DisplayName,
    string? InstallLocation,
    bool IsFramework,
    bool IsResource,
    bool IsStoreSigned);

/// <summary>Packages installed for the current user. Read-only; never needs admin.</summary>
public interface IPackageCatalog
{
    Task<IReadOnlyList<InstalledPackage>> GetForCurrentUserAsync(CancellationToken ct);
}
