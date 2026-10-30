// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Routines;

namespace Lumen.App.ViewModels;

/// <summary>One line in the quick search list.</summary>
public sealed record QuickSearchResult(LibraryItem? Item, string Title, string Detail, string ActionText, string? RoutineId = null)
{
    public override string ToString() => Title;
}

/// <summary>
/// The quick search box (PLAN-v1 §4.3): opened by the global shortcut or the tray, it searches the whole library
/// (games and apps) as you type; Enter runs the selected result (play, open or install) and closes the box.
/// </summary>
public sealed partial class QuickSearchViewModel : ObservableObject
{
    private readonly LibraryService _library;
    private readonly LaunchService _launcher;
    private readonly QuickSearch _search;
    private readonly LibraryViewModel _libraryView;
    private readonly ILocalizer _localizer;
    private readonly RoutineService _routineService;
    private readonly RoutinesViewModel _routines;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private QuickSearchResult? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public QuickSearchViewModel(LibraryService library, LaunchService launcher, QuickSearch search, LibraryViewModel libraryView, ILocalizer localizer, RoutineService routineService, RoutinesViewModel routines)
    {
        _routineService = routineService;
        _routines = routines;
        _library = library;
        _launcher = launcher;
        _search = search;
        _libraryView = libraryView;
        _localizer = localizer;
    }

    /// <summary>Raised after a result was started, so the window can close.</summary>
    public event EventHandler? Done;

    public ObservableCollection<QuickSearchResult> Results { get; } = [];

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>Clears the box and lists recently played items. Call before showing it.</summary>
    public void Reset()
    {
        Notice = null;
        if (Query.Length == 0)
        {
            Refresh();
        }
        else
        {
            Query = string.Empty; // refreshes
        }
    }

    /// <summary>Moves the selection up (-1) or down (+1), wrapping around.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[((index + delta) % Results.Count + Results.Count) % Results.Count];
    }

    partial void OnQueryChanged(string value) => Refresh();

    [RelayCommand]
    private async Task RunAsync(QuickSearchResult? result)
    {
        result ??= Selected ?? Results.FirstOrDefault();
        if (result is null)
        {
            return;
        }

        if (result.RoutineId is { } routineId)
        {
            // The box closes first: a routine can take a while, and one that needs review opens the Routines page.
            Done?.Invoke(this, EventArgs.Empty);
            await _routines.StartFromShortcutAsync(routineId);
            return;
        }

        if (result.Item is { } item && await _launcher.RunPrimaryActionAsync(item.Primary, CancellationToken.None) == LaunchOutcome.Started)
        {
            Done?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Notice = string.Format(CultureInfo.CurrentCulture, _localizer["Library_LaunchFailed"], result.Title);
        }
    }

    private void Refresh()
    {
        Notice = Query.Trim().Length > 0 ? null : Notice;
        Results.Clear();
        foreach (var item in _search.Find(_library.Items, Query).Where(i => LaunchService.PrimaryAction(i.Primary) is not null))
        {
            Results.Add(Result(item));
        }

        foreach (var entry in RoutineLookup.Find(_routineService.Entries, Query, CultureInfo.CurrentUICulture))
        {
            Results.Add(RoutineResult(entry));
        }

        Selected = Results.FirstOrDefault();
        if (Results.Count == 0)
        {
            Notice = _localizer[Query.Trim().Length > 0 ? "QuickSearch_NoMatches" : "QuickSearch_Hint"];
        }
    }

    private QuickSearchResult RoutineResult(RoutineEntry entry)
    {
        var needsReview = entry.State == RoutineTrustState.NeedsReview;
        var detail = needsReview
            ? $"{_localizer["QuickSearch_Routine"]} · {_localizer["Routine_State_NeedsReview"]}"
            : _localizer["QuickSearch_Routine"];
        return new QuickSearchResult(null, RoutineLookup.NameOf(entry, CultureInfo.CurrentUICulture), detail, _localizer[needsReview ? "Routine_Review" : "Routine_Run"], entry.Id);
    }

    private QuickSearchResult Result(LibraryItem item)
    {
        var kind = _localizer[item.Kind == ItemKind.App ? "QuickSearch_App" : "QuickSearch_Game"];
        var stores = string.Join(" · ", item.Entries.Select(_libraryView.StoreNameOf).Distinct());
        var detail = item.IsInstalled || item.Kind == ItemKind.App
            ? $"{kind} · {stores}"
            : $"{kind} · {stores} · {_localizer["QuickSearch_NotInstalled"]}";
        var action = _localizer[!item.IsInstalled ? "Action_Install" : item.Kind == ItemKind.App ? "Action_Open" : "Action_Play"];
        return new QuickSearchResult(item, item.Title, detail, action);
    }
}
