// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Lumen.Platform.Windows;

/// <summary>
/// Lists packages with <see cref="PackageManager.FindPackagesForUser(string)"/> for the current user
/// (empty SID = current user; no admin). Some packages throw when a property is read; each one is
/// read defensively so a single odd package never hides the rest.
/// </summary>
public sealed class WindowsPackageCatalog : IPackageCatalog
{
    public Task<IReadOnlyList<InstalledPackage>> GetForCurrentUserAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<InstalledPackage>>(
            () =>
            {
                var result = new List<InstalledPackage>();
                foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
                {
                    ct.ThrowIfCancellationRequested();
                    var mapped = TryMap(package);
                    if (mapped is not null)
                    {
                        result.Add(mapped);
                    }
                }

                return result;
            },
            ct);

    private static InstalledPackage? TryMap(Package package)
    {
        try
        {
            return new InstalledPackage(
                package.Id.FamilyName,
                package.Id.FullName,
                Try(() => package.DisplayName),
                Try(() => package.EffectivePath) ?? Try(() => package.InstalledLocation?.Path),
                Try(() => package.IsFramework),
                Try(() => package.IsResourcePackage),
                Try(() => package.SignatureKind == PackageSignatureKind.Store));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return null;
        }
    }

    private static T? Try<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            return default;
        }
    }
}
