// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Updates;

public enum UpdateState
{
    /// <summary>A build run from source: there is nothing to update.</summary>
    NotInstalled,

    Idle,
    Checking,
    UpToDate,

    /// <summary>A newer version exists; nothing was downloaded (the user decides).</summary>
    Available,

    Downloading,

    /// <summary>Downloaded; it is installed when Lumen restarts (the user decides when).</summary>
    ReadyToRestart,

    /// <summary>The last check or download failed (offline, GitHub down…). The next try starts clean.</summary>
    Failed,
}

/// <summary>
/// Updates (PLAN-v1 §4.6, owner decisions 2026-10-30): Lumen looks for a new version at start-up and once a day (the user
/// can turn that off), tells the user once per version, and downloads only when the user asks. The user also chooses
/// when to restart. Beta is the default channel until 1.0. Lives wherever it is created; the events come from the thread
/// that finished the work, so the UI posts them back.
/// </summary>
public sealed partial class UpdateService : IDisposable
{
    /// <summary>How often the automatic check repeats while Lumen keeps running.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);

    /// <summary>Wait before the first automatic check, so it doesn't compete with the app starting up.</summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(15);

    private readonly IUpdateClient _client;
    private readonly ILocalSettings _settings;
    private readonly ILogger<UpdateService> _logger;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private CancellationTokenSource? _loop;

    public UpdateService(IUpdateClient client, ILocalSettings settings, ILogger<UpdateService> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
        State = client.IsInstalled ? UpdateState.Idle : UpdateState.NotInstalled;
    }

    public UpdateState State { get; private set; }

    public string? CurrentVersion => _client.CurrentVersion;

    /// <summary>The newer version, while <see cref="State"/> is Available, Downloading or ReadyToRestart.</summary>
    public UpdateOffer? Offer { get; private set; }

    /// <summary>0–100 while downloading.</summary>
    public int Percent { get; private set; }

    public UpdateChannel Channel { get; private set; } = UpdateChannel.Beta;

    public bool CheckAutomatically { get; private set; } = true;

    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>Something the page shows changed.</summary>
    public event EventHandler? Changed;

    /// <summary>A version the user wasn't told about yet was found (once per version): for the tray notice.</summary>
    public event EventHandler<UpdateOffer>? Announce;

    /// <summary>Called just before Lumen restarts to install, so the app can release the tray icon and hotkey.</summary>
    public Action? BeforeRestart { get; set; }

    /// <summary>Reads the saved choices, and notes a download that was waiting for a restart. Call once at start-up.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var saved = await _settings.GetAsync(LocalSettingKeys.UpdateChannel, ct).ConfigureAwait(false);
        Channel = UpdateChannels.TryParse(saved, out var channel) ? channel : UpdateChannels.DefaultFor(_client.CurrentVersion);
        CheckAutomatically = await _settings.GetAsync(LocalSettingKeys.UpdateCheckAutomatically, ct).ConfigureAwait(false) != "0";

        if (State != UpdateState.NotInstalled && _client.PendingRestart is { } pending)
        {
            Offer = pending;
            Percent = 100;
            State = UpdateState.ReadyToRestart;
        }

        RaiseChanged();
    }

    /// <summary>Checks now if automatic checks are on, then again every <see cref="CheckEvery"/>. Returns at once.</summary>
    public void StartAutomaticChecks()
    {
        if (State == UpdateState.NotInstalled || _loop is not null)
        {
            return;
        }

        _loop = new CancellationTokenSource();
        _ = RunLoopAsync(_loop.Token);
    }

    public async Task SetChannelAsync(UpdateChannel channel, CancellationToken ct)
    {
        if (channel == Channel)
        {
            return;
        }

        Channel = channel;
        await _settings.SetAsync(LocalSettingKeys.UpdateChannel, UpdateChannels.ToText(channel), ct).ConfigureAwait(false);

        // What was found belongs to the old channel (a beta isn't offered to someone on stable): look again.
        if (State is UpdateState.Available or UpdateState.UpToDate or UpdateState.Failed)
        {
            Offer = null;
            State = UpdateState.Idle;
        }

        RaiseChanged();
    }

    public async Task SetCheckAutomaticallyAsync(bool value, CancellationToken ct)
    {
        CheckAutomatically = value;
        await _settings.SetAsync(LocalSettingKeys.UpdateCheckAutomatically, value ? "1" : "0", ct).ConfigureAwait(false);
        RaiseChanged();
    }

    /// <summary>Looks for a new version. <paramref name="manual"/> (the user pressed the button) skips the tray notice.</summary>
    public async Task CheckAsync(bool manual, CancellationToken ct)
    {
        if (State is UpdateState.NotInstalled or UpdateState.Downloading or UpdateState.ReadyToRestart || !await _busy.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            State = UpdateState.Checking;
            RaiseChanged();
            var offer = await _client.CheckAsync(Channel, ct).ConfigureAwait(false);
            LastChecked = DateTimeOffset.UtcNow;
            Offer = offer;
            State = offer is null ? UpdateState.UpToDate : UpdateState.Available;
            LogChecked(offer?.Version ?? "none", UpdateChannels.ToText(Channel));
            RaiseChanged();

            if (offer is not null && !manual && await _settings.GetAsync(LocalSettingKeys.UpdateNotifiedVersion, ct).ConfigureAwait(false) != offer.Version)
            {
                await _settings.SetAsync(LocalSettingKeys.UpdateNotifiedVersion, offer.Version, ct).ConfigureAwait(false);
                Announce?.Invoke(this, offer);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            State = UpdateState.Failed;
            RaiseChanged();
        }
        finally
        {
            _busy.Release();
        }
    }

    /// <summary>Downloads the version found. Only when the user asks.</summary>
    public async Task DownloadAsync(CancellationToken ct)
    {
        if (State != UpdateState.Available || Offer is not { } offer || !await _busy.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            State = UpdateState.Downloading;
            Percent = 0;
            RaiseChanged();
            await _client.DownloadAsync(offer, percent =>
            {
                Percent = Math.Clamp(percent, 0, 100);
                RaiseChanged();
            }, ct).ConfigureAwait(false);
            Percent = 100;
            State = UpdateState.ReadyToRestart;
            LogDownloaded(offer.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            State = UpdateState.Failed;
            Offer = null;
        }
        finally
        {
            _busy.Release();
            RaiseChanged();
        }
    }

    /// <summary>Installs the downloaded version and starts Lumen again (the user chose to).</summary>
    public void RestartToUpdate()
    {
        if (State != UpdateState.ReadyToRestart || Offer is not { } offer)
        {
            return;
        }

        BeforeRestart?.Invoke();
        _client.ApplyAndRestart(offer);
    }

    public void Dispose()
    {
        _loop?.Cancel();
        _loop?.Dispose();
        _loop = null;
        _busy.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            if (StartupDelay > TimeSpan.Zero)
            {
                await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            }

            using var timer = new PeriodicTimer(CheckEvery);
            do
            {
                if (CheckAutomatically)
                {
                    await CheckAsync(manual: false, ct).ConfigureAwait(false);
                }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        catch (ObjectDisposedException)
        {
            // Closing down.
        }
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check: newest {Version} on the {Channel} channel")]
    private partial void LogChecked(string version, string channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update {Version} downloaded; waiting for a restart")]
    private partial void LogDownloaded(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update check or download failed")]
    private partial void LogFailed(Exception exception);
}
