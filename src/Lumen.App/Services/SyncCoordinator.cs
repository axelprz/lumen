// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.IO;
using Lumen.App.ViewModels;
using Lumen.Core;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Core.Routines;
using Lumen.Core.Settings;
using Lumen.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Lumen.App.Services;

/// <summary>
/// Connects the sync engine (<see cref="SyncFolderService"/>) to the running app (PLAN-v1 §4.5): turns sync on and
/// off, moves the routines folder into <c>&lt;sync&gt;/routines</c> (and back), reloads the pages when something arrived from the
/// folder, and tells the engine when something changed on this PC. Lives on the UI thread; the engine's events arrive
/// from a background thread and are posted back.
/// </summary>
public sealed partial class SyncCoordinator
{
    private const int MaxTitlesShown = 3;

    private readonly SyncFolderService _sync;
    private readonly RoutineService _routines;
    private readonly PhysicalUserFiles _files;
    private readonly IFileSystem _fileSystem;
    private readonly LumenPaths _paths;
    private readonly ILocalSettings _settings;
    private readonly LibraryService _library;
    private readonly CollectionService _collections;
    private readonly LanguageSettingsViewModel _language;
    private readonly WindowSettingsViewModel _window;
    private readonly HotkeyService _hotkeys;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly SynchronizationContext _ui;

    public SyncCoordinator(
        SyncFolderService sync,
        RoutineService routines,
        PhysicalUserFiles files,
        IFileSystem fileSystem,
        LumenPaths paths,
        ILocalSettings settings,
        LibraryService library,
        CollectionService collections,
        LanguageSettingsViewModel language,
        WindowSettingsViewModel window,
        HotkeyService hotkeys,
        ILocalizer localizer,
        ILogger<SyncCoordinator> logger)
    {
        _sync = sync;
        _routines = routines;
        _files = files;
        _fileSystem = fileSystem;
        _paths = paths;
        _settings = settings;
        _library = library;
        _collections = collections;
        _language = language;
        _window = window;
        _hotkeys = hotkeys;
        _localizer = localizer;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        sync.Applied += (_, e) => _ui.Post(_ => _ = ApplyAsync(e), null);
        sync.RoutinesChanged += (_, _) => _ui.Post(_ => _ = ReloadRoutinesAsync(), null);
        sync.StatusChanged += (_, _) => _ui.Post(_ => _ = StatusChangedAsync(), null);
        sync.PassCompleted += (_, _) => _ui.Post(_ => Changed?.Invoke(this, EventArgs.Empty), null);

        // What changed on this PC goes to the folder after a short pause (the engine debounces).
        collections.Changed += (_, _) => sync.NotifyLocalChanged();
        library.Changed += (_, _) => sync.NotifyLocalChanged();
        localizer.CultureChanged += (_, _) => sync.NotifyLocalChanged();
        hotkeys.Changed += (_, _) => sync.NotifyLocalChanged();
        window.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WindowSettingsViewModel.CloseToTray))
            {
                sync.NotifyLocalChanged();
            }
        };
    }

    public SyncFolderService Service => _sync;

    /// <summary>"3 items came from the sync folder: …", until the next sync that brings something.</summary>
    public string? Notice { get; private set; }

    /// <summary>Status, last sync time or notice changed (UI thread).</summary>
    public event EventHandler? Changed;

    /// <summary>Something worth a balloon from the tray: title and message.</summary>
    public event Action<string, string>? Notify;

    /// <summary>At start-up: picks up the folder chosen before.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var folder = await _settings.GetAsync(LocalSettingKeys.SyncFolder, ct);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (SyncFolderRules.Check(folder) != SyncFolderProblem.None)
        {
            LogRefusedFolder(); // the saved value is not a folder Lumen may write in: sync stays off
            return;
        }

        _files.AllowFolder(folder);
        await _sync.ResumeAsync(ct);
        await UpdateRoutinesFolderAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<SyncPairing> InspectAsync(string folder, CancellationToken ct) => _sync.InspectAsync(folder, ct);

    /// <summary>Starts syncing with <paramref name="folder"/>; routines on this PC are copied there (see <see cref="RoutineFolderMigration"/>).</summary>
    public async Task AttachAsync(string folder, SyncStart? start, CancellationToken ct)
    {
        if (SyncFolderRules.Check(folder) != SyncFolderProblem.None)
        {
            throw new ArgumentException("Not a folder Lumen may sync with.", nameof(folder));
        }

        var previous = _sync.Folder;
        _files.AllowFolder(folder);
        try
        {
            if (previous is not null)
            {
                await CopyRoutinesAsync(Path.Combine(previous, SyncFolderService.RoutinesFolder), _paths.DefaultRoutinesDirectory, ct);
            }

            await _sync.AttachAsync(folder, start, ct);
        }
        catch
        {
            if (_sync.Folder is null || !string.Equals(_sync.Folder, folder, StringComparison.OrdinalIgnoreCase))
            {
                _files.ForgetFolder(folder);
            }

            throw;
        }

        await CopyRoutinesAsync(_paths.DefaultRoutinesDirectory, Path.Combine(folder, SyncFolderService.RoutinesFolder), ct);
        if (previous is not null && !string.Equals(previous, folder, StringComparison.OrdinalIgnoreCase))
        {
            _files.ForgetFolder(previous);
        }

        // The status change may have switched the folder before the copy finished: load once more so the copied routines show.
        await UpdateRoutinesFolderAsync(ct);
        await _routines.LoadAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops syncing. Data here and in the folder stays; the folder's routines are copied back to this PC first.</summary>
    public async Task DetachAsync(CancellationToken ct)
    {
        if (_sync.Folder is not { } folder)
        {
            return;
        }

        await CopyRoutinesAsync(Path.Combine(folder, SyncFolderService.RoutinesFolder), _paths.DefaultRoutinesDirectory, ct);
        await _sync.DetachAsync(ct);
        await UpdateRoutinesFolderAsync(ct);
        _files.ForgetFolder(folder);
        Notice = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SyncNowAsync(CancellationToken ct)
    {
        await _sync.SyncNowAsync(ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task CopyRoutinesAsync(string from, string to, CancellationToken ct)
    {
        if (!_fileSystem.DirectoryExists(from))
        {
            return;
        }

        var result = await RoutineFolderMigration.CopyMissingAsync(from, to, _fileSystem, _files, ct);
        LogRoutinesCopied(result.Copied, result.KeptBoth, result.Skipped);
    }

    // Routines live in the sync folder while it is there, and in Lumen's own folder otherwise.
    private async Task UpdateRoutinesFolderAsync(CancellationToken ct)
    {
        var wanted = _sync.Status is SyncStatus.Active or SyncStatus.Failed && _sync.RoutinesPath is { } synced ? synced : _paths.DefaultRoutinesDirectory;
        if (!string.Equals(_routines.Folder, wanted, StringComparison.OrdinalIgnoreCase))
        {
            _routines.SetFolder(wanted);
            await _routines.LoadAsync(ct);
        }
    }

    private async Task StatusChangedAsync()
    {
        try
        {
            await UpdateRoutinesFolderAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRoutinesFailed(ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReloadRoutinesAsync()
    {
        try
        {
            await _routines.LoadAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRoutinesFailed(ex);
        }
    }

    // Data arrived from the folder: put it in front of the user without a restart.
    private async Task ApplyAsync(SyncAppliedEventArgs e)
    {
        try
        {
            if (e.ConfigChanged)
            {
                await _language.InitializeAsync(CancellationToken.None);
                await _window.InitializeAsync(CancellationToken.None);
                await _hotkeys.ReloadAsync(CancellationToken.None);
                await _library.ScanSourceAsync(ManualItem.Source, CancellationToken.None);
                await _library.ReloadUserChoicesAsync(CancellationToken.None);
            }

            if (e.CollectionsChanged)
            {
                await _collections.LoadAsync(CancellationToken.None);
            }

            if (e.ManualItemsReceived.Count > 0)
            {
                var titles = string.Join(", ", e.ManualItemsReceived.Take(MaxTitlesShown))
                    + (e.ManualItemsReceived.Count > MaxTitlesShown ? "…" : string.Empty);
                Notice = string.Format(CultureInfo.CurrentCulture, _localizer["Sync_ManualItemsReceived"], e.ManualItemsReceived.Count, titles);
                Notify?.Invoke(_localizer["Sync_NoticeTitle"], Notice);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogApplyFailed(ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The saved sync folder is not one Lumen may use; sync stays off")]
    private partial void LogRefusedFolder();

    [LoggerMessage(Level = LogLevel.Information, Message = "Routines copied: {Copied} copied, {KeptBoth} kept next to a different one, {Skipped} skipped")]
    private partial void LogRoutinesCopied(int copied, int keptBoth, int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not reload the routines after a sync change")]
    private partial void LogRoutinesFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not refresh the app after a sync change")]
    private partial void LogApplyFailed(Exception exception);
}
