// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Localization;
using Lumen.Core.Updates;

namespace Lumen.App.ViewModels;

/// <summary>One choice in the channel list.</summary>
public sealed record UpdateChannelOption(UpdateChannel Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Settings → Updates (PLAN-v1 §4.6): the running version, the channel (stable / beta), "check automatically", and the
/// buttons Check now, Download and Restart to update. The user decides each step; nothing is downloaded or installed by
/// itself. All texts are written from the current state each time, so they follow a language change.
/// </summary>
public sealed partial class UpdateSettingsViewModel : ObservableObject
{
    private readonly UpdateService _updates;
    private readonly ILocalizer _loc;
    private readonly SynchronizationContext _ui;
    private bool _loading;

    [ObservableProperty]
    private UpdateChannelOption? _selectedChannel;

    [ObservableProperty]
    private bool _checkAutomatically = true;

    public UpdateSettingsViewModel(UpdateService updates, ILocalizer loc)
    {
        _updates = updates;
        _loc = loc;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        updates.Changed += (_, _) => _ui.Post(_ => Refresh(), null);
        loc.CultureChanged += (_, _) => Refresh();
        Options = BuildOptions();
        Refresh();
    }

    public IReadOnlyList<UpdateChannelOption> Options { get; private set; }

    /// <summary>"Lumen 0.2.0-beta.1", or the development build's own version.</summary>
    public string VersionText => string.Format(CultureInfo.CurrentCulture, _loc["Settings_Updates_Version"], CurrentVersion);

    public bool NotInstalled => _updates.State == UpdateState.NotInstalled;

    public bool CanCheck => _updates.State is UpdateState.Idle or UpdateState.UpToDate or UpdateState.Available or UpdateState.Failed;

    public bool CanDownload => _updates.State == UpdateState.Available;

    public bool CanRestart => _updates.State == UpdateState.ReadyToRestart;

    public bool IsDownloading => _updates.State == UpdateState.Downloading;

    public int Percent => _updates.Percent;

    public bool IsFailed => _updates.State == UpdateState.Failed;

    public string DownloadText => string.Format(CultureInfo.CurrentCulture, _loc["Settings_Updates_Download"], _updates.Offer?.Version);

    public string StateText => _updates.State switch
    {
        UpdateState.NotInstalled => _loc["Settings_Updates_NotInstalled"],
        UpdateState.Checking => _loc["Settings_Updates_Checking"],
        UpdateState.UpToDate => _loc["Settings_Updates_UpToDate"],
        UpdateState.Available => Available(),
        UpdateState.Downloading => string.Format(CultureInfo.CurrentCulture, _loc["Settings_Updates_Downloading"], _updates.Offer?.Version, _updates.Percent),
        UpdateState.ReadyToRestart => string.Format(CultureInfo.CurrentCulture, _loc["Settings_Updates_Ready"], _updates.Offer?.Version),
        UpdateState.Failed => _loc["Settings_Updates_Failed"],
        _ => _loc["Settings_Updates_NotChecked"],
    };

    private string CurrentVersion
    {
        get
        {
            var version = _updates.CurrentVersion
                ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "?";
            var plus = version.IndexOf('+', StringComparison.Ordinal); // build metadata (the commit) isn't for people
            return plus < 0 ? version : version[..plus];
        }
    }

    partial void OnSelectedChannelChanged(UpdateChannelOption? value)
    {
        if (!_loading && value is not null)
        {
            _ = _updates.SetChannelAsync(value.Value, CancellationToken.None);
        }
    }

    partial void OnCheckAutomaticallyChanged(bool value)
    {
        if (!_loading)
        {
            _ = _updates.SetCheckAutomaticallyAsync(value, CancellationToken.None);
        }
    }

    [RelayCommand]
    private Task CheckNowAsync() => _updates.CheckAsync(manual: true, CancellationToken.None);

    [RelayCommand]
    private Task DownloadAsync() => _updates.DownloadAsync(CancellationToken.None);

    [RelayCommand]
    private void Restart() => _updates.RestartToUpdate();

    private string Available()
    {
        var offer = _updates.Offer;
        var size = offer is { SizeBytes: > 0 } ? $" ({offer.SizeBytes / 1_000_000.0:0.#} MB)" : string.Empty;
        return string.Format(CultureInfo.CurrentCulture, _loc["Settings_Updates_Available"], offer?.Version, size);
    }

    private IReadOnlyList<UpdateChannelOption> BuildOptions() =>
    [
        new UpdateChannelOption(UpdateChannel.Stable, _loc["Settings_Updates_Channel_Stable"]),
        new UpdateChannelOption(UpdateChannel.Beta, _loc["Settings_Updates_Channel_Beta"]),
    ];

    // Re-reads everything from the service (it changes on other threads) and rebuilds the language-dependent labels.
    private void Refresh()
    {
        _loading = true;
        try
        {
            Options = BuildOptions();
            OnPropertyChanged(nameof(Options));
            SelectedChannel = Options.First(o => o.Value == _updates.Channel);
            CheckAutomatically = _updates.CheckAutomatically;
        }
        finally
        {
            _loading = false;
        }

        OnPropertyChanged(string.Empty);
    }
}
