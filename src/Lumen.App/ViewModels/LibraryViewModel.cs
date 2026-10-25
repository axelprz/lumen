// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Microsoft.Extensions.Logging;

namespace Lumen.App.ViewModels;

/// <summary>Library page: shows cached entries immediately, then rescans in the background.</summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly LaunchService _launcher;
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
        ILocalizer localizer,
        ILogger<LibraryViewModel> logger)
    {
        _library = library;
        _launcher = launcher;
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

    private void Rebuild()
    {
        Items.Clear();
        foreach (var entry in _library.Entries)
        {
            var store = _storeNames.GetValueOrDefault(entry.Ref.Source, entry.Ref.Source);
            Items.Add(new LibraryTileViewModel(entry, store, _localizer, new AsyncRelayCommand(() => RunPrimaryActionAsync(entry))));
        }

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the cached library")]
    private partial void LogCacheFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Library scan failed")]
    private partial void LogScanFailed(Exception exception);
}
