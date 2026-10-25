// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>Library page: shows cached entries immediately, then rescans in the background.</summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    // Upper bound for one drag & drop, so dropping a whole folder's contents can't open dozens of dialogs.
    private const int MaxDroppedItems = 10;

    private readonly LibraryService _library;
    private readonly LaunchService _launcher;
    private readonly IManualItemStore _manualItems;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly IReadOnlyDictionary<string, string> _storeNames;
    private readonly SynchronizationContext _ui;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public LibraryViewModel(
        LibraryService library,
        LaunchService launcher,
        IEnumerable<ILibraryProvider> providers,
        IManualItemStore manualItems,
        IDialogService dialogs,
        ILocalizer localizer,
        ILogger<LibraryViewModel> logger)
    {
        _library = library;
        _launcher = launcher;
        _manualItems = manualItems;
        _dialogs = dialogs;
        _localizer = localizer;
        _logger = logger;
        _storeNames = providers.ToDictionary(p => p.Source, p => p.DisplayName, StringComparer.Ordinal);
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _library.Changed += (_, _) => _ui.Post(_ => Rebuild(), null);
        _localizer.CultureChanged += (_, _) => Rebuild();
    }

    public ObservableCollection<LibraryTileViewModel> Items { get; } = [];

    public string CountText => Items.Count == 1
        ? _localizer["Library_CountOne"]
        : string.Format(_localizer.Culture, _localizer["Library_Count"], Items.Count);

    public bool IsEmpty => !IsScanning && Items.Count == 0;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>Called once after the main window is shown.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            await _library.LoadCachedAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheFailed(ex);
        }

        await RescanAsync();
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        IsScanning = true;
        Notice = null;
        try
        {
            var reports = await _library.ScanAsync(CancellationToken.None);
            var withProblems = reports.Where(r => r.Problems.Any()).Select(r => r.DisplayName).ToList();
            if (withProblems.Count > 0)
            {
                Notice = string.Format(_localizer.Culture, _localizer["Library_ScanProblems"], string.Join(", ", withProblems));
            }
        }
        catch (Exception ex)
        {
            LogScanFailed(ex);
            Notice = _localizer["Library_ScanFailed"];
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task RunPrimaryActionAsync(LibraryEntry entry)
    {
        Notice = null;
        var outcome = await _launcher.RunPrimaryActionAsync(entry, CancellationToken.None);
        if (outcome != LaunchOutcome.Started)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, _localizer["Library_LaunchFailed"], entry.Title);
        }
    }

    /// <summary>Opens the add dialog, optionally prefilled with a dropped/pasted file or URL.</summary>
    [RelayCommand]
    private async Task AddManualItemAsync(string? prefillTarget)
    {
        var editor = new ManualItemEditorViewModel(_dialogs, _localizer, prefillTarget: prefillTarget);
        if (_dialogs.ShowManualItemEditor(editor) && editor.Result is { } item)
        {
            await SaveManualItemAsync(item);
        }
    }

    /// <summary>Files or URLs dropped on the library: each opens the add dialog so the user confirms it.</summary>
    public async Task AddFromDropAsync(IEnumerable<string> targets)
    {
        foreach (var target in targets.Where(t => !string.IsNullOrWhiteSpace(t)).Take(MaxDroppedItems))
        {
            await AddManualItemAsync(target);
        }
    }

    private async Task EditManualItemAsync(LibraryEntry entry)
    {
        var existing = (await _manualItems.GetAllAsync(CancellationToken.None)).FirstOrDefault(i => i.Ref == entry.Ref);
        if (existing is null)
        {
            return;
        }

        var editor = new ManualItemEditorViewModel(_dialogs, _localizer, existing);
        if (_dialogs.ShowManualItemEditor(editor) && editor.Result is { } item)
        {
            await SaveManualItemAsync(item);
        }
    }

    private async Task RemoveManualItemAsync(LibraryEntry entry)
    {
        var message = string.Format(CultureInfo.CurrentCulture, _localizer["Manual_RemoveConfirm"], entry.Title);
        if (!_dialogs.Confirm(_localizer["Manual_RemoveConfirmTitle"], message) || !Guid.TryParse(entry.Ref.ExternalId, out var id))
        {
            return;
        }

        await _manualItems.RemoveAsync(id, CancellationToken.None);
        await _library.ScanSourceAsync(ManualItem.Source, CancellationToken.None);
    }

    private async Task SaveManualItemAsync(ManualItem item)
    {
        try
        {
            await _manualItems.SaveAsync(item, CancellationToken.None);
            await _library.ScanSourceAsync(ManualItem.Source, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogManualSaveFailed(ex);
            Notice = _localizer["Manual_SaveFailed"];
        }
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var entry in _library.Entries)
        {
            var isManual = entry.Ref.Source == ManualItem.Source;
            var store = isManual
                ? _localizer["Store_Manual"]
                : _storeNames.GetValueOrDefault(entry.Ref.Source, entry.Ref.Source);
            var commands = new TileCommands(
                new AsyncRelayCommand(() => RunPrimaryActionAsync(entry), () => LaunchService.PrimaryAction(entry) is not null),
                isManual ? new AsyncRelayCommand(() => EditManualItemAsync(entry)) : null,
                isManual ? new AsyncRelayCommand(() => RemoveManualItemAsync(entry)) : null);
            Items.Add(new LibraryTileViewModel(entry, store, _localizer, commands));
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save a manual item")]
    private partial void LogManualSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the cached library")]
    private partial void LogCacheFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Library scan failed")]
    private partial void LogScanFailed(Exception exception);
}
