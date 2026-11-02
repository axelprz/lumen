// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Routines;

namespace Lumen.App.ViewModels;

/// <summary>Which block of the quick search list a result belongs to (also the order of the blocks).</summary>
public enum QuickSearchGroup
{
    Recent,
    PinnedRoutines,
    Games,
    Apps,
    Routines,
}

/// <summary>One line in the quick search list: a thumbnail (cover, icon or a routine glyph), title, details, action.</summary>
public sealed partial class QuickSearchResult(LibraryItem? item, string title, string detail, string actionText, QuickSearchGroup group, string groupTitle, string? routineId = null)
    : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(HasIcon))]
    private TileArt? _art;

    public LibraryItem? Item { get; } = item;

    public string Title { get; } = title;

    public string Detail { get; } = detail;

    public string ActionText { get; } = actionText;

    public string? RoutineId { get; } = routineId;

    public QuickSearchGroup Group { get; } = group;

    /// <summary>The heading of its block; results are rebuilt on every search, so it is written in the current language.</summary>
    public string GroupTitle { get; } = groupTitle;

    public bool IsRoutine => RoutineId is not null;

    public bool HasCover => Art is { IsIcon: false };

    public bool HasIcon => Art is { IsIcon: true };

    public string Initial => Title.Length == 0 ? "?" : StringInfo.GetNextTextElement(Title).ToUpper(CultureInfo.CurrentCulture);

    public override string ToString() => Title;
}

/// <summary>
/// The quick search box (PLAN-v1 §4.3): opened by the global shortcut or the tray. Empty, it offers recently played
/// games and pinned routines; typing searches games, apps and routines, shown in blocks. Enter runs the selected result
/// (play, open, install or run) and closes the box; Ctrl+Enter opens a game's detail in Lumen instead.
/// </summary>
public sealed partial class QuickSearchViewModel : ObservableObject
{
    /// <summary>Recently played games offered when the box opens empty.</summary>
    public const int RecentCount = 6;

    private readonly LibraryService _library;
    private readonly LaunchService _launcher;
    private readonly QuickSearch _search;
    private readonly LibraryViewModel _libraryView;
    private readonly ILocalizer _localizer;
    private readonly RoutineService _routineService;
    private readonly RoutinesViewModel _routines;
    private readonly ArtworkService _artwork;
    private readonly CollectionService _collections;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private QuickSearchResult? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public QuickSearchViewModel(LibraryService library, LaunchService launcher, QuickSearch search, LibraryViewModel libraryView, ILocalizer localizer, RoutineService routineService, RoutinesViewModel routines, ArtworkService artwork, CollectionService collections)
    {
        _routineService = routineService;
        _routines = routines;
        _library = library;
        _launcher = launcher;
        _search = search;
        _libraryView = libraryView;
        _localizer = localizer;
        _artwork = artwork;
        _collections = collections;
    }

    /// <summary>Raised after a result was started, so the window can close.</summary>
    public event EventHandler? Done;

    /// <summary>Raised by Ctrl+Enter on a game or app: show its detail in the main window.</summary>
    public event EventHandler<LibraryItem>? DetailRequested;

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

    /// <summary>Ctrl+Enter: the selected game's (or app's) detail in Lumen. Routines have no detail; they run as with Enter.</summary>
    [RelayCommand]
    private async Task OpenDetailAsync()
    {
        var result = Selected ?? Results.FirstOrDefault();
        if (result?.Item is { } item)
        {
            Done?.Invoke(this, EventArgs.Empty);
            DetailRequested?.Invoke(this, item);
        }
        else if (result is not null)
        {
            await RunAsync(result);
        }
    }

    private void Refresh()
    {
        Notice = Query.Trim().Length > 0 ? null : Notice;
        Results.Clear();
        if (Query.Trim().Length == 0)
        {
            AddSuggestions();
        }
        else
        {
            // Games first, then apps (each keeps the search order), then routines.
            var found = _search.Find(_library.Items, Query).Where(i => LaunchService.PrimaryAction(i.Primary) is not null).ToList();
            foreach (var item in found.Where(i => i.Kind != ItemKind.App))
            {
                Results.Add(Result(item, QuickSearchGroup.Games, "QuickSearch_GroupGames"));
            }

            foreach (var item in found.Where(i => i.Kind == ItemKind.App))
            {
                Results.Add(Result(item, QuickSearchGroup.Apps, "QuickSearch_GroupApps"));
            }

            foreach (var entry in RoutineLookup.Find(_routineService.Entries, Query, CultureInfo.CurrentUICulture))
            {
                Results.Add(RoutineResult(entry, QuickSearchGroup.Routines, "QuickSearch_GroupRoutines"));
            }
        }

        Selected = Results.FirstOrDefault();
        if (Results.Count == 0)
        {
            Notice = _localizer[Query.Trim().Length > 0 ? "QuickSearch_NoMatches" : "QuickSearch_Hint"];
        }

        foreach (var result in Results.Where(r => r.Item is not null).ToList())
        {
            _ = LoadArtAsync(result);
        }
    }

    // Empty box: what was played last and the pinned routines, so something starts without typing.
    private void AddSuggestions()
    {
        var recent = HomeFeed.Build(_library.Items, _library.FirstSeen, _collections.Favorites).ContinuePlaying
            .Where(i => LaunchService.PrimaryAction(i.Primary) is not null)
            .Take(RecentCount);
        foreach (var item in recent)
        {
            Results.Add(Result(item, QuickSearchGroup.Recent, "QuickSearch_GroupRecent"));
        }

        foreach (var entry in RoutineLookup.Pinned(_routineService.Entries, CultureInfo.CurrentUICulture))
        {
            Results.Add(RoutineResult(entry, QuickSearchGroup.PinnedRoutines, "QuickSearch_GroupPinned"));
        }
    }

    private async Task LoadArtAsync(QuickSearchResult result)
    {
        try
        {
            result.Art = await TileArt.LoadAsync(_artwork, result.Item!);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            // No picture: the initial stays.
        }
    }

    private QuickSearchResult RoutineResult(RoutineEntry entry, QuickSearchGroup group, string groupKey)
    {
        var needsReview = entry.State == RoutineTrustState.NeedsReview;
        var detail = needsReview
            ? $"{_localizer["QuickSearch_Routine"]} · {_localizer["Routine_State_NeedsReview"]}"
            : _localizer["QuickSearch_Routine"];
        return new QuickSearchResult(null, RoutineLookup.NameOf(entry, CultureInfo.CurrentUICulture), detail,
            _localizer[needsReview ? "Routine_Review" : "Routine_Run"], group, _localizer[groupKey], entry.Id);
    }

    private QuickSearchResult Result(LibraryItem item, QuickSearchGroup group, string groupKey)
    {
        var stores = string.Join(" · ", item.Entries.Select(_libraryView.StoreNameOf).Distinct());
        var detail = item.IsInstalled || item.Kind == ItemKind.App
            ? stores
            : $"{stores} · {_localizer["QuickSearch_NotInstalled"]}";
        var action = _localizer[!item.IsInstalled ? "Action_Install" : item.Kind == ItemKind.App ? "Action_Open" : "Action_Play"];
        return new QuickSearchResult(item, item.Title, detail, action, group, _localizer[groupKey]);
    }
}
