// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Platform;

/// <summary>Starts a <see cref="LaunchSpec"/>. Never elevates.</summary>
public interface ILauncher
{
    /// <returns>The started process id when one is known (URIs handed to a store usually return null).</returns>
    Task<int?> StartAsync(LaunchSpec spec, CancellationToken ct);
}
