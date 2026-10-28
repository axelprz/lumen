// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Providers.Tests.Fakes;

internal sealed class FakePackageCatalog : IPackageCatalog
{
    public List<InstalledPackage> Packages { get; } = [];

    public bool Throw { get; set; }

    public Task<IReadOnlyList<InstalledPackage>> GetForCurrentUserAsync(CancellationToken ct) =>
        Throw ? throw new UnauthorizedAccessException() : Task.FromResult<IReadOnlyList<InstalledPackage>>(Packages);
}
