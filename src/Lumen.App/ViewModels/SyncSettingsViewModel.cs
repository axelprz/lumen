// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Localization;
using Lumen.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>
/// Settings → Sync folder (PLAN-v1 §4.5/§4.7): pick the folder, see the status, sync now, stop. When the folder and this
/// PC both already have data, an inline choice (combine / use the folder / use this PC) appears before anything is
/// written; the two that remove data ask for a confirmation first.
/// </summary>
public sealed partial class SyncSettingsViewModel : ObservableObject
{
    private readonly SyncCoordinator _coordinator;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _loc;
    private readonly ILogger<SyncSettingsViewModel> _logger;
    private SyncPairing? _pending;
    private string? _pendingFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    [ObservableProperty]
    private bool _isBusy;

    public SyncSettingsViewModel(SyncCoordinator coordinator, IDialogService dialogs, ILocalizer loc, ILogger<SyncSettingsViewModel> logger)
    {
        _coordinator = coordinator;
        _dialogs = dialogs;
        _loc = loc;
        _logger = logger;
        coordinator.Changed += (_, _) => Refresh();
        loc.CultureChanged += (_, _) => Refresh();
    }

    public bool IsOn => _coordinator.Service.Folder is not null;

    public bool IsOff => !IsOn;

    public string FolderText => _coordinator.Service.Folder ?? _loc["Settings_Sync_NoFolder"];

    public string StatusText
    {
        get
        {
            var sync = _coordinator.Service;
            return sync.Status switch
            {
                SyncStatus.Active => sync.LastSyncedAt is { } at
                    ? string.Format(CultureInfo.CurrentCulture, _loc["Settings_Sync_Active"], at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))
                    : _loc["Settings_Sync_ActiveNow"],
                SyncStatus.FolderUnavailable => _loc["Settings_Sync_Unavailable"],
                SyncStatus.Failed => _loc["Settings_Sync_Failed"],
                _ => _loc["Settings_Sync_Off"],
            };
        }
    }

    public bool HasProblem => _coordinator.Service.Status is SyncStatus.FolderUnavailable or SyncStatus.Failed;

    public string? Notice => _coordinator.Notice;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>Files the last sync skipped (technical English), so the user can see why something didn't arrive.</summary>
    public string ProblemsText => string.Join(Environment.NewLine, _coordinator.Service.Problems);

    public bool HasProblems => _coordinator.Service.Problems.Count > 0;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool HasChoice => _pending is not null;

    public string FolderSummary => Describe(_pending?.Folder);

    public string ThisPcSummary => Describe(_pending?.ThisPc);

    private void Refresh() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Message = null;
        var folder = _dialogs.PickFolder();
        if (folder is null)
        {
            return;
        }

        var problem = SyncFolderRules.Check(folder);
        if (problem != SyncFolderProblem.None)
        {
            Message = _loc["Settings_Sync_Problem_" + problem];
            return;
        }

        await RunAsync(async () =>
        {
            var pairing = await _coordinator.InspectAsync(folder, CancellationToken.None);
            if (pairing.NeedsChoice)
            {
                _pending = pairing;
                _pendingFolder = folder;
                return;
            }

            await _coordinator.AttachAsync(folder, null, CancellationToken.None);
            Message = _loc["Settings_Sync_Started"];
        });
    }

    [RelayCommand]
    private Task MergeAsync() => StartChosenAsync(SyncStart.Merge, confirmKey: null);

    [RelayCommand]
    private Task UseFolderAsync() => StartChosenAsync(SyncStart.UseFolder, "Settings_Sync_UseFolder_Confirm");

    [RelayCommand]
    private Task UseThisPcAsync() => StartChosenAsync(SyncStart.UseThisPc, "Settings_Sync_UseThisPc_Confirm");

    [RelayCommand]
    private void CancelChoice()
    {
        _pending = null;
        _pendingFolder = null;
        Refresh();
    }

    [RelayCommand]
    private Task SyncNowAsync() => RunAsync(() => _coordinator.SyncNowAsync(CancellationToken.None));

    [RelayCommand]
    private async Task StopAsync()
    {
        if (!_dialogs.Confirm(_loc["Settings_Sync_Stop"], _loc["Settings_Sync_Stop_Confirm"]))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _coordinator.DetachAsync(CancellationToken.None);
            Message = _loc["Settings_Sync_Stopped"];
        });
    }

    private async Task StartChosenAsync(SyncStart start, string? confirmKey)
    {
        if (_pendingFolder is not { } folder || IsBusy)
        {
            return;
        }

        if (confirmKey is not null && !_dialogs.Confirm(_loc["Settings_Sync_Confirm_Title"], string.Format(CultureInfo.CurrentCulture, _loc[confirmKey], start == SyncStart.UseFolder ? ThisPcSummary : FolderSummary)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _coordinator.AttachAsync(folder, start, CancellationToken.None);
            _pending = null;
            _pendingFolder = null;
            Message = _loc["Settings_Sync_Started"];
        });
    }

    private async Task RunAsync(Func<Task> work)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            Message = _loc["Settings_Sync_Error"];
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private string Describe(SyncSummary? summary) =>
        summary is null
            ? string.Empty
            : string.Format(CultureInfo.CurrentCulture, _loc["Settings_Sync_Summary"], summary.Collections, summary.ManualItems, summary.Renames, summary.IgnoredApps);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sync folder action failed")]
    private partial void LogFailed(Exception exception);
}
