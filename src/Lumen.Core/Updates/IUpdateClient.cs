// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Updates;

/// <summary>A newer version found on GitHub Releases.</summary>
/// <param name="Version">The version text, for example "0.2.0-beta.1".</param>
/// <param name="SizeBytes">Size of the download (the delta when there is one), 0 when unknown.</param>
/// <param name="Native">What the client needs to download and apply it; opaque to everyone else.</param>
public sealed record UpdateOffer(string Version, long SizeBytes, object Native);

/// <summary>
/// The one thing that talks to GitHub Releases (rule 4 of CLAUDE.md: that host only) and installs updates. The real one
/// is Velopack (<c>Lumen.Platform.Windows</c>); tests use a fake.
/// </summary>
public interface IUpdateClient
{
    /// <summary>False for a build run from source: updates only work in the installed app or the portable zip.</summary>
    bool IsInstalled { get; }

    /// <summary>The running version; null when it isn't known.</summary>
    string? CurrentVersion { get; }

    /// <summary>A downloaded update waiting for a restart (from an earlier run), or null.</summary>
    UpdateOffer? PendingRestart { get; }

    /// <summary>Looks for a version newer than the running one. Nothing is downloaded.</summary>
    Task<UpdateOffer?> CheckAsync(UpdateChannel channel, CancellationToken ct);

    /// <summary>Downloads the update (a delta when possible). <paramref name="progress"/> gets 0–100.</summary>
    Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken ct);

    /// <summary>Installs the downloaded update and starts Lumen again. Does not return in the real client.</summary>
    void ApplyAndRestart(UpdateOffer offer);
}
