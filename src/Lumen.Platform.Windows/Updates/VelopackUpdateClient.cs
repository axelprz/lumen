// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace Lumen.Platform.Windows.Updates;

/// <summary>
/// <see cref="IUpdateClient"/> on Velopack, reading GitHub Releases of one fixed repository (no setting can point it
/// elsewhere, no token: the repository is public). Beta = pre-releases included; stable = normal releases only.
/// Velopack checks the SHA of what it downloads against the release's manifest; builds are unsigned in v1.
/// </summary>
public sealed class VelopackUpdateClient(string repositoryUrl) : IUpdateClient
{
    private readonly object _lock = new();
    private UpdateManager? _manager;
    private UpdateChannel _managerChannel;

    public bool IsInstalled => Current()?.IsInstalled ?? false;

    public string? CurrentVersion => Current()?.CurrentVersion?.ToString();

    public UpdateOffer? PendingRestart => Current()?.UpdatePendingRestart is { } asset ? ToOffer(asset, asset) : null;

    public async Task<UpdateOffer?> CheckAsync(UpdateChannel channel, CancellationToken ct)
    {
        var info = await Required(channel).CheckForUpdatesAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return info is null ? null : ToOffer(info.TargetFullRelease, info);
    }

    public async Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken ct)
    {
        var info = (UpdateInfo)offer.Native;
        await Required(null).DownloadUpdatesAsync(info, progress, ct).ConfigureAwait(false);
    }

    public void ApplyAndRestart(UpdateOffer offer)
    {
        var manager = Required(null);
        var asset = offer.Native is UpdateInfo info ? info.TargetFullRelease : (VelopackAsset)offer.Native;
        manager.ApplyUpdatesAndRestart(asset);
    }

    // A build run from source has nothing installed to update: if Velopack can't set itself up there, that is "not installed",
    // never a crash at start-up.
    private UpdateManager? Current()
    {
        lock (_lock)
        {
            return _manager ?? Create(UpdateChannel.Stable);
        }
    }

    private UpdateManager Required(UpdateChannel? channel)
    {
        lock (_lock)
        {
            var manager = channel is { } wanted && (_manager is null || _managerChannel != wanted) ? Create(wanted) : Current();
            return manager is { IsInstalled: true } ? manager : throw new InvalidOperationException("Lumen is not installed, so it can't be updated.");
        }
    }

    // One manager per channel: the source (pre-releases or not) is fixed when it is created. The manager of the last check is
    // kept, so downloading and installing use the same source that found the update.
    private UpdateManager? Create(UpdateChannel channel)
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(repositoryUrl, accessToken: null, prerelease: channel == UpdateChannel.Beta));
            _managerChannel = channel;
            return _manager;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static UpdateOffer ToOffer(VelopackAsset asset, object native) => new(asset.Version.ToString(), asset.Size, native);
}
