// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Notifications;
using Lumen.Core.Updates;

namespace Lumen.App.ViewModels;

/// <summary>One line of the bell's list, written in the current language when the list is built.</summary>
public sealed record NotificationRow(string Key, NotificationKind Kind, string Glyph, string Title, string Message, string TimeText);

/// <summary>
/// The bell in the top bar (PLAN-v1 M12): collects updates, store problems, what arrived from the sync folder and games
/// that could not start (owner, 2026-11-02), in memory only. This class connects each source to the
/// <see cref="NotificationCenter"/>; events from background threads are posted to the UI thread before touching anything.
/// </summary>
public sealed partial class NotificationsViewModel : ObservableObject
{
    private const string UpdateKey = "update";
    private const string SyncKey = "sync";

    private readonly NotificationCenter _center;
    private readonly ILocalizer _loc;
    private readonly UpdateService _updates;
    private readonly LibraryService _library;
    private readonly SyncCoordinator _sync;
    private readonly SynchronizationContext _ui;
    private readonly HashSet<string> _storesWithProblem = [];
    private string? _updateSignature;

    [ObservableProperty]
    private bool _isOpen;

    public NotificationsViewModel(
        NotificationCenter center,
        ILocalizer loc,
        UpdateService updates,
        LibraryService library,
        LaunchService launcher,
        SyncCoordinator sync)
    {
        _center = center;
        _loc = loc;
        _updates = updates;
        _library = library;
        _sync = sync;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        center.Changed += (_, _) => _ui.Post(_ => Refresh(), null);
        loc.CultureChanged += (_, _) => _ui.Post(_ => Refresh(), null);
        updates.Changed += (_, _) => _ui.Post(_ => OnUpdateChanged(), null);
        library.FullScanCompleted += (_, _) => _ui.Post(_ => OnScanCompleted(), null);
        launcher.Launched += (_, e) => OnLaunched(e);
        sync.Notify += OnSyncNotice;
    }

    /// <summary>The user chose an entry: the shell opens the page that explains it.</summary>
    public event EventHandler<NotificationKind>? OpenRequested;

    public ObservableCollection<NotificationRow> Rows { get; } = [];

    public bool HasItems => Rows.Count > 0;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The badge on the bell: the number, or "9+" when there are many.</summary>
    public string CountText => Rows.Count > 9 ? "9+" : Rows.Count.ToString(CultureInfo.CurrentCulture);

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    [RelayCommand]
    private void Open(NotificationRow? row)
    {
        if (row is null)
        {
            return;
        }

        IsOpen = false;
        OpenRequested?.Invoke(this, row.Kind);
    }

    [RelayCommand]
    private void Dismiss(NotificationRow? row)
    {
        if (row is not null)
        {
            _center.Remove(row.Key);
        }
    }

    [RelayCommand]
    private void ClearAll() => _center.Clear();

    private void Refresh()
    {
        Rows.Clear();
        foreach (var n in _center.Items)
        {
            Rows.Add(new NotificationRow(
                n.Key,
                n.Kind,
                n.Kind switch
                {
                    NotificationKind.Update => "", // download
                    NotificationKind.StoreProblem => "", // warning
                    NotificationKind.Sync => "", // sync
                    _ => "", // error badge
                },
                n.Title(),
                n.Message(),
                n.At.LocalDateTime.ToString("t", CultureInfo.CurrentCulture)));
        }

        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    // A new version is on offer or ready: one entry that follows its state; gone when up to date.
    private void OnUpdateChanged()
    {
        var version = _updates.Offer?.Version;
        switch (_updates.State)
        {
            case UpdateState.Available or UpdateState.ReadyToRestart when version is not null:
                var ready = _updates.State == UpdateState.ReadyToRestart;
                var signature = (ready ? "ready:" : "available:") + version;
                if (signature == _updateSignature && _center.Contains(UpdateKey))
                {
                    return;
                }

                _updateSignature = signature;
                _center.Post(
                    UpdateKey,
                    NotificationKind.Update,
                    () => _loc["Update_NoticeTitle"],
                    () => string.Format(CultureInfo.CurrentCulture, _loc[ready ? "Settings_Updates_Ready" : "Update_NoticeMessage"], version));
                break;
            case UpdateState.UpToDate or UpdateState.Idle or UpdateState.NotInstalled:
                _updateSignature = null;
                _center.Remove(UpdateKey);
                break;
        }
    }

    // After every full scan: a store with a warning or error gets one entry (until the user dismisses it or it recovers).
    private void OnScanCompleted()
    {
        if (_library.LastFullScan is not { } reports)
        {
            return;
        }

        foreach (var store in StoreProblems.From(reports))
        {
            var key = StoreProblems.KeyFor(store.Source);
            if (!store.HasProblem)
            {
                _storesWithProblem.Remove(store.Source);
                _center.Remove(key);
            }
            else if (_storesWithProblem.Add(store.Source))
            {
                var name = store.DisplayName;
                _center.Post(
                    key,
                    NotificationKind.StoreProblem,
                    () => string.Format(CultureInfo.CurrentCulture, _loc["Notify_StoreProblem_Title"], name),
                    () => _loc["Notify_StoreProblem_Message"]);
            }
        }
    }

    private void OnLaunched(LaunchEventArgs e)
    {
        if (e.Outcome != LaunchOutcome.Failed)
        {
            return;
        }

        var title = e.Entry.Title;
        _center.Post(
            "launch:" + e.Entry.Ref,
            NotificationKind.Launch,
            () => _loc["Notify_Launch_Title"],
            () => string.Format(CultureInfo.CurrentCulture, _loc["Library_LaunchFailed"], title));
    }

    private void OnSyncNotice(string title, string message) =>
        _center.Post(SyncKey, NotificationKind.Sync, () => _loc["Sync_NoticeTitle"], () => _sync.Notice ?? message);
}
