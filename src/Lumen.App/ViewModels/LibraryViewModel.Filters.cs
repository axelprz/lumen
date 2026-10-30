// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.Core.Library;
using Lumen.Core.Settings;

namespace Lumen.App.ViewModels;

/// <summary>One choice in a filter dropdown.</summary>
public sealed record FilterOption<T>(string Label, T Value)
{
    // What screen readers (UI Automation) and the closed ComboBox announce: the visible label only.
    public override string ToString() => Label;
}

/// <summary>Search, filters and sort for the library page.</summary>
public sealed partial class LibraryViewModel
{
    private const string SortSettingKey = "library.sort";

    private readonly List<LibraryTileViewModel> _allTiles = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private FilterOption<KindFilter>? _selectedKind;

    [ObservableProperty]
    private FilterOption<string?>? _selectedStore;

    [ObservableProperty]
    private FilterOption<InstallFilter>? _selectedInstall;

    [ObservableProperty]
    private FilterOption<LibrarySort>? _selectedSort;

    [ObservableProperty]
    private FilterOption<string?>? _selectedCollection;

    /// <summary>"All collections", Favorites and the user's collections.</summary>
    public IReadOnlyList<FilterOption<string?>> CollectionOptions { get; private set; } = [];

    /// <summary>Shows one collection (Collections page → Open). Other filters are cleared so the whole collection shows.</summary>
    public void ShowCollection(string id)
    {
        _suspendFilter = true;
        SearchText = string.Empty;
        SelectedKind = KindOptions[0];
        SelectedStore = StoreOptions[0];
        SelectedInstall = InstallOptions[0];
        SelectedCollection = CollectionOptions.FirstOrDefault(o => o.Value == id) ?? CollectionOptions[0];
        ShowHidden = false;
        Detail = null;
        _suspendFilter = false;
        ApplyFilter();
    }

    /// <summary>Showing only the hidden items (to find and unhide them) instead of the library.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HiddenToggleText))]
    private bool _showHidden;

    /// <summary>How many items the user hid.</summary>
    public int HiddenCount => _allTiles.Count(t => t.Item.IsHidden);

    /// <summary>Items in the library that are not hidden (the sidebar count).</summary>
    public int LibraryCount => _allTiles.Count(t => !t.Item.IsHidden);

    /// <summary>The "Hidden (n)" button only appears once something is hidden (or while viewing them).</summary>
    public bool HasHiddenToggle => ShowHidden || HiddenCount > 0;

    public string HiddenToggleText => ShowHidden
        ? _localizer["Library_BackFromHidden"]
        : string.Format(CultureInfo.CurrentCulture, _localizer["Library_ShowHidden"], HiddenCount);

    public IReadOnlyList<FilterOption<KindFilter>> KindOptions { get; private set; } = [];

    public IReadOnlyList<FilterOption<InstallFilter>> InstallOptions { get; private set; } = [];

    public IReadOnlyList<FilterOption<LibrarySort>> SortOptions { get; private set; } = [];

    /// <summary>"All stores" plus every store present in the library.</summary>
    public IReadOnlyList<FilterOption<string?>> StoreOptions { get; private set; } = [];

    public bool IsFiltering => CurrentQuery().IsFiltering;

    /// <summary>The library (or the hidden list) has items but none match the current search/filters.</summary>
    public bool HasNoMatches => IsFiltering && ViewTotal > 0 && Items.Count == 0;

    /// <summary>Items in the current view before search/filters: the library without hidden ones, or only the hidden.</summary>
    private int ViewTotal => _allTiles.Count(t => t.Item.IsHidden == ShowHidden);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedKindChanged(FilterOption<KindFilter>? value) => ApplyFilter();

    partial void OnSelectedStoreChanged(FilterOption<string?>? value) => ApplyFilter();

    partial void OnSelectedInstallChanged(FilterOption<InstallFilter>? value) => ApplyFilter();

    partial void OnSelectedCollectionChanged(FilterOption<string?>? value) => ApplyFilter();

    partial void OnShowHiddenChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private void ToggleHidden() => ShowHidden = !ShowHidden;

    partial void OnSelectedSortChanged(FilterOption<LibrarySort>? value)
    {
        ApplyFilter();

        // Remember only the user's choices: while the options are being (re)built the default selection must
        // not overwrite what was saved. A convenience, so failures are ignored.
        if (value is not null && !_suspendFilter)
        {
            _ = _settings.SetAsync(SortSettingKey, value.Value.ToString(), CancellationToken.None);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suspendFilter = true;
        SearchText = string.Empty;
        SelectedKind = KindOptions[0];
        SelectedStore = StoreOptions[0];
        SelectedInstall = InstallOptions[0];
        SelectedCollection = CollectionOptions[0];
        _suspendFilter = false;
        ApplyFilter();
    }

    private bool _suspendFilter;

    private void BuildFilterOptions()
    {
        var kind = SelectedKind?.Value ?? KindFilter.All;
        var install = SelectedInstall?.Value ?? InstallFilter.All;
        var sort = SelectedSort?.Value ?? LibrarySort.Name;
        var store = SelectedStore?.Value;
        var collection = SelectedCollection?.Value;

        _suspendFilter = true;
        KindOptions =
        [
            new(_localizer["Filter_AllTypes"], KindFilter.All),
            new(_localizer["Filter_Games"], KindFilter.Games),
            new(_localizer["Filter_Apps"], KindFilter.Apps),
        ];
        InstallOptions =
        [
            new(_localizer["Filter_AnyState"], InstallFilter.All),
            new(_localizer["Filter_Installed"], InstallFilter.Installed),
            new(_localizer["Filter_NotInstalled"], InstallFilter.NotInstalled),
        ];
        SortOptions =
        [
            new(_localizer["Sort_Name"], LibrarySort.Name),
            new(_localizer["Sort_LastPlayed"], LibrarySort.LastPlayed),
            new(_localizer["Sort_Playtime"], LibrarySort.Playtime),
            new(_localizer["Sort_RecentlyAdded"], LibrarySort.RecentlyAdded),
        ];
        StoreOptions = [new FilterOption<string?>(_localizer["Filter_AllStores"], null),
            .. _library.Items.SelectMany(i => i.Entries).Select(e => e.Ref.Source).Distinct()
                .Select(source => new FilterOption<string?>(SourceName(source), source))
                .OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase)];

        OnPropertyChanged(nameof(KindOptions));
        OnPropertyChanged(nameof(InstallOptions));
        OnPropertyChanged(nameof(SortOptions));
        CollectionOptions =
        [
            new FilterOption<string?>(_localizer["Filter_AllCollections"], null),
            .. _collections.All.Select(c => new FilterOption<string?>(CollectionsViewModel.DisplayName(c, _localizer), c.Id)),
        ];

        OnPropertyChanged(nameof(StoreOptions));
        OnPropertyChanged(nameof(CollectionOptions));
        SelectedKind = KindOptions.First(o => o.Value == kind);
        SelectedInstall = InstallOptions.First(o => o.Value == install);
        SelectedSort = SortOptions.First(o => o.Value == sort);
        SelectedStore = StoreOptions.FirstOrDefault(o => o.Value == store) ?? StoreOptions[0];
        SelectedCollection = CollectionOptions.FirstOrDefault(o => o.Value == collection) ?? CollectionOptions[0];
        _suspendFilter = false;
    }

    private async Task RestoreSortAsync()
    {
        try
        {
            var saved = await _settings.GetAsync(SortSettingKey, CancellationToken.None);
            if (Enum.TryParse<LibrarySort>(saved, out var sort))
            {
                SelectedSort = SortOptions.FirstOrDefault(o => o.Value == sort) ?? SelectedSort;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCacheFailed(ex);
        }
    }

    private LibraryQuery CurrentQuery() => new(
        SearchText,
        SelectedKind?.Value ?? KindFilter.All,
        SelectedStore?.Value,
        SelectedInstall?.Value ?? InstallFilter.All,
        SelectedSort?.Value ?? LibrarySort.Name,
        ShowHidden,
        SelectedCollection?.Value);

    /// <summary>Shows the tiles that match the current query, in its order. Tiles are reused, so covers stay loaded.</summary>
    private void ApplyFilter()
    {
        if (_suspendFilter)
        {
            return;
        }

        var byItem = TilesByItem();
        var visible = _filter.Apply(_allTiles.Select(t => t.Item).ToList(), CurrentQuery(), _library.FirstSeen, _collections.All);
        Items.Clear();
        foreach (var item in visible)
        {
            Items.Add(byItem[item]);
        }

        RebuildRows();
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(LibraryCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFiltering));
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(HasHiddenToggle));
        OnPropertyChanged(nameof(HiddenToggleText));
    }

    // Items are matched by reference: two different items can never be "equal" here.
    private Dictionary<LibraryItem, LibraryTileViewModel> TilesByItem()
    {
        var map = new Dictionary<LibraryItem, LibraryTileViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var tile in _allTiles)
        {
            map[tile.Item] = tile;
        }

        return map;
    }

    private string FormatCount()
    {
        var total = ViewTotal;
        if (IsFiltering)
        {
            return string.Format(CultureInfo.CurrentCulture, _localizer["Library_CountFiltered"], Items.Count, total);
        }

        if (ShowHidden)
        {
            return string.Format(CultureInfo.CurrentCulture, _localizer["Library_CountHidden"], total);
        }

        return total == 1
            ? _localizer["Library_CountOne"]
            : string.Format(_localizer.Culture, _localizer["Library_Count"], total);
    }
}
