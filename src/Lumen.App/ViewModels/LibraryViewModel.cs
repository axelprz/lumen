// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;
using Lumen.Core.Settings;
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
    private readonly IMergeRuleStore _mergeRules;
    private readonly IItemOverrideStore _overrides;
    private readonly ArtworkService _artwork;
    private readonly AccentService _accent;
    private readonly DiscoverAppsViewModel _discover;
    private readonly CollectionService _collections;
    private readonly IDialogService _dialogs;
    private readonly LibraryFilter _filter;
    private readonly ILocalSettings _settings;
    private readonly ILauncher _shell;
    private readonly IFileSystem _fileSystem;
    private int _columnCount = 1;
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
        IMergeRuleStore mergeRules,
        IItemOverrideStore overrides,
        ArtworkService artwork,
        AccentService accent,
        DiscoverAppsViewModel discover,
        CollectionService collections,
        IDialogService dialogs,
        LibraryFilter filter,
        ILocalSettings settings,
        ILauncher shell,
        IFileSystem fileSystem,
        ILocalizer localizer,
        ILogger<LibraryViewModel> logger)
    {
        _filter = filter;
        _settings = settings;
        _shell = shell;
        _fileSystem = fileSystem;
        _library = library;
        _launcher = launcher;
        _manualItems = manualItems;
        _mergeRules = mergeRules;
        _overrides = overrides;
        _artwork = artwork;
        _accent = accent;
        _discover = discover;
        _collections = collections;
        _collections.Changed += (_, _) => OnCollectionsChanged();
        _dialogs = dialogs;
        _localizer = localizer;
        _logger = logger;
        _storeNames = providers.ToDictionary(p => p.Source, p => p.DisplayName, StringComparer.Ordinal);
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _library.Changed += (_, _) => _ui.Post(_ => Rebuild(), null);
        _localizer.CultureChanged += (_, _) => Rebuild();
        BuildFilterOptions();
    }

    /// <summary>"Discover apps": detected apps to add, shown over the library while open.</summary>
    public DiscoverAppsViewModel Discover => _discover;

    /// <summary>The tiles currently shown (after search, filters and sort).</summary>
    public ObservableCollection<LibraryTileViewModel> Items { get; } = [];

    /// <summary>Items laid out in rows of <see cref="ColumnCount"/> for the virtualized grid.</summary>
    public ObservableCollection<TileRow> Rows { get; } = [];

    /// <summary>Tiles per row; set by the view from its width.</summary>
    public int ColumnCount
    {
        get => _columnCount;
        set
        {
            var columns = Math.Max(1, value);
            if (SetProperty(ref _columnCount, columns))
            {
                RebuildRows();
            }
        }
    }

    /// <summary>"79 items", or "12 of 79" while filtering.</summary>
    public string CountText => FormatCount();

    /// <summary>The library itself is empty (not just filtered to nothing).</summary>
    public bool IsEmpty => !IsScanning && _allTiles.Count == 0;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>Called once after the main window is shown.</summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        await RestoreSortAsync();
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
            // Manual rescan: also refresh cached remote data (Steam's owned list).
            var reports = await _library.RefreshAsync(CancellationToken.None);
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

    private async Task UseStoreAlwaysAsync(LibraryItem item, LibraryEntry preferred)
    {
        await _mergeRules.SetPreferredAsync(preferred.Ref, item.Entries.Select(e => e.Ref), CancellationToken.None);
        await _library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    private async Task SeparateAsync(LibraryItem item)
    {
        await _mergeRules.SeparateAsync(item.Entries.Select(e => e.Ref), CancellationToken.None);
        await _library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    private async Task SetHiddenAsync(LibraryItem item, bool hidden)
    {
        await _overrides.SetHiddenAsync(item.Entries.Select(e => e.Ref), hidden, CancellationToken.None);
        await _library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    private string StoreNameOf(LibraryEntry entry) => SourceName(entry.Ref.Source);

    // Manual items and detected apps aren't stores: their names are translated.
    private string SourceName(string source) => source switch
    {
        ManualItem.Source => _localizer["Store_Manual"],
        "startmenu" => _localizer["Store_Windows"],
        "appsfolder" => _localizer["Store_MicrosoftStore"],
        _ => _storeNames.GetValueOrDefault(source, source),
    };

    private string Format(string key, string arg) => string.Format(CultureInfo.CurrentCulture, _localizer[key], arg);

    /// <summary>Context menu: per-store play/install and preference for merged games; edit/remove for manual items.</summary>
    private List<TileMenuItem> BuildMenu(LibraryItem item)
    {
        var menu = new List<TileMenuItem>();
        if (item.IsMerged)
        {
            foreach (var entry in item.Entries.Where(e => LaunchService.PrimaryAction(e) is not null))
            {
                var key = entry.IsInstalled ? "Tile_PlayWith" : "Tile_InstallWith";
                menu.Add(new(Format(key, StoreNameOf(entry)), new AsyncRelayCommand(() => RunPrimaryActionAsync(entry))));
            }

            menu.Add(new(string.Empty, null));
            foreach (var entry in item.Entries.Where(e => e != item.Primary && LaunchService.PrimaryAction(e) is not null))
            {
                menu.Add(new(Format("Tile_AlwaysUse", StoreNameOf(entry)), new AsyncRelayCommand(() => UseStoreAlwaysAsync(item, entry))));
            }

            menu.Add(new(_localizer["Tile_Separate"], new AsyncRelayCommand(() => SeparateAsync(item))));
        }

        // A manual item can be merged with a store's copy; edit/remove always target the manual entry.
        var manual = item.Entries.FirstOrDefault(e => e.Ref.Source == ManualItem.Source);
        if (manual is not null)
        {
            if (menu.Count > 0)
            {
                menu.Add(new(string.Empty, null));
            }

            menu.Add(new(_localizer["Tile_Edit"], new AsyncRelayCommand(() => EditManualItemAsync(manual))));
            menu.Add(new(_localizer["Tile_Remove"], new AsyncRelayCommand(() => RemoveManualItemAsync(manual))));
        }

        // Favorites: one click from the tile.
        if (menu.Count > 0)
        {
            menu.Add(new(string.Empty, null));
        }

        var favorite = _collections.IsFavorite(item);
        menu.Add(new(
            _localizer[favorite ? "Tile_RemoveFavorite" : "Tile_AddFavorite"],
            new AsyncRelayCommand(() => SetFavoriteAsync(item, !favorite))));

        // A detected app the user added: take it back out (it returns to "Discover apps").
        var detected = item.Entries.FirstOrDefault(e => AppCuration.IsCandidateSource(e.Ref.Source));
        if (detected is not null)
        {
            if (menu.Count > 0)
            {
                menu.Add(new(string.Empty, null));
            }

            menu.Add(new(_localizer["Tile_RemoveApp"], new AsyncRelayCommand(() => _discover.RemoveFromLibraryAsync(detected))));
        }

        // Every item: pick a custom cover; restore it if one is set.
        if (menu.Count > 0)
        {
            menu.Add(new(string.Empty, null));
        }

        menu.Add(new(_localizer["Tile_ChangeCover"], new AsyncRelayCommand(() => ChangeCoverAsync(item))));
        if (item.Entries.Any(e => _artwork.HasOverride(e.Ref, ArtworkKind.Cover)))
        {
            menu.Add(new(_localizer["Tile_RestoreCover"], new RelayCommand(() => RestoreCover(item))));
        }

        menu.Add(new(string.Empty, null));
        menu.Add(item.IsHidden
            ? new(_localizer["Tile_Unhide"], new AsyncRelayCommand(() => SetHiddenAsync(item, hidden: false)))
            : new(_localizer["Tile_Hide"], new AsyncRelayCommand(() => SetHiddenAsync(item, hidden: true))));
        return menu;
    }

    private async Task ChangeCoverAsync(LibraryItem item)
    {
        var file = _dialogs.PickFile(_localizer["Artwork_ImageFilter"]);
        if (file is null)
        {
            return;
        }

        Notice = null;
        if (!await _artwork.SetOverrideAsync(item.Key, ArtworkKind.Cover, file, CancellationToken.None))
        {
            Notice = _localizer["Artwork_InvalidImage"];
            return;
        }

        RefreshTiles(t => t.Item == item);
    }

    private void RestoreCover(LibraryItem item)
    {
        foreach (var entry in item.Entries)
        {
            _artwork.RemoveOverride(entry.Ref, ArtworkKind.Cover);
        }

        RefreshTiles(t => t.Item == item);
    }

    /// <summary>Reloads every cover (after the cache was cleared or a new artwork source was configured).</summary>
    public void ReloadArtwork() => RefreshTiles(_ => true);

    /// <summary>
    /// Replaces matching tiles in place (new cover request, new menu) without rebuilding the grid, so the
    /// scroll position and the rest of the tiles stay as they are.
    /// </summary>
    private void RefreshTiles(Func<LibraryTileViewModel, bool> match)
    {
        for (var i = 0; i < _allTiles.Count; i++)
        {
            if (match(_allTiles[i]))
            {
                _allTiles[i] = CreateTile(_allTiles[i].Item);
            }
        }

        // Swap the visible ones in place too (same item, new tile).
        var current = TilesByItem();
        for (var i = 0; i < Items.Count; i++)
        {
            var fresh = current[Items[i].Item];
            if (!ReferenceEquals(fresh, Items[i]))
            {
                Items[i] = fresh;
            }
        }

        for (var r = 0; r < Rows.Count; r++)
        {
            var start = r * _columnCount;
            var updated = Items.Skip(start).Take(_columnCount).ToList();
            if (!updated.SequenceEqual(Rows[r].Tiles))
            {
                Rows[r] = new TileRow(updated);
            }
        }
    }

    private LibraryTileViewModel CreateTile(LibraryItem item)
    {
        var primary = item.Primary;
        var stores = string.Join(" · ", item.Entries.Select(StoreNameOf).Distinct());
        var play = new AsyncRelayCommand(() => RunPrimaryActionAsync(primary), () => LaunchService.PrimaryAction(primary) is not null);
        return new LibraryTileViewModel(item, stores, _localizer, play, BuildMenu(item), LoadCoverAsync, new AsyncRelayCommand(() => OpenDetailAsync(item)))
        {
            IsFavorite = _collections.IsFavorite(item),
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail))]
    private GameDetailViewModel? _detail;

    /// <summary>True while a game's detail view covers the grid.</summary>
    public bool HasDetail => Detail is not null;

    /// <summary>Raised when the detail view closes (Back, Esc, or its game left the library).</summary>
    public event EventHandler? DetailClosed;

    /// <summary>Opens a game's detail view (also used by Home).</summary>
    public Task ShowDetailAsync(LibraryItem item) => OpenDetailAsync(item);

    partial void OnDetailChanged(GameDetailViewModel? oldValue, GameDetailViewModel? newValue)
    {
        if (oldValue is not null && newValue is null)
        {
            DetailClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task OpenDetailAsync(LibraryItem item)
    {
        var detail = new GameDetailViewModel(
            item,
            new DetailServices(_launcher, _shell, _artwork, _mergeRules, _overrides, _collections, _library, _dialogs, _fileSystem, _localizer, StoreNameOf),
            () => Detail = null);
        detail.ArtworkChanged += (_, _) => RefreshTiles(t => t.Item.Entries.Any(e => detail.Item.Entries.Contains(e)));
        Detail = detail;
        await detail.LoadArtworkAsync();
        if (ReferenceEquals(Detail, detail))
        {
            await _accent.ApplyFromImageAsync(detail.HeroPath ?? detail.CoverPath);
        }
    }

    /// <summary>After the library changes, keep the open detail on the same game (it may now be merged/split).</summary>
    private void SyncDetail()
    {
        if (Detail is null)
        {
            return;
        }

        var refs = Detail.Item.Entries.Select(e => e.Ref).ToHashSet();
        var match = _library.Items.FirstOrDefault(i => i.Primary.Ref == Detail.Item.Primary.Ref)
            ?? _library.Items.FirstOrDefault(i => i.Entries.Any(e => refs.Contains(e.Ref)));
        if (match is null)
        {
            Detail = null;
        }
        else if (!ReferenceEquals(match, Detail.Item))
        {
            _ = OpenDetailAsync(match);
        }
    }

    private void Rebuild()
    {
        // Keep the tile of every item that didn't change (cover stays loaded, nothing flickers); only new or
        // changed items get a new tile. Matters for automatic rescans, e.g. a game finishing its install.
        var previous = new Dictionary<ItemRef, LibraryTileViewModel>();
        foreach (var tile in _allTiles)
        {
            previous.TryAdd(tile.Item.Key, tile);
        }

        _allTiles.Clear();
        foreach (var item in _library.Items)
        {
            _allTiles.Add(previous.TryGetValue(item.Key, out var old) && SameItem(old.Item, item) ? old : CreateTile(item));
        }

        BuildFilterOptions(); // stores present may have changed; selections are kept
        if (ShowHidden && HiddenCount == 0)
        {
            ShowHidden = false; // the last hidden item was shown again: back to the library (applies the filter)
        }
        else
        {
            ApplyFilter();
        }

        SyncDetail();
    }

    private static bool SameItem(LibraryItem a, LibraryItem b) =>
        a.Primary.Ref == b.Primary.Ref
        && a.Title == b.Title
        && a.IsHidden == b.IsHidden
        && EntryContentComparer.SameList(a.Entries, b.Entries);

    /// <summary>
    /// Lays the visible tiles out in rows, replacing only rows whose tiles changed and adding/removing rows at the
    /// end, so the virtualized list keeps its scroll position.
    /// </summary>
    private void RebuildRows()
    {
        var chunks = Items.Chunk(_columnCount).ToList();
        for (var i = 0; i < chunks.Count; i++)
        {
            if (i < Rows.Count)
            {
                if (!Rows[i].Tiles.SequenceEqual(chunks[i]))
                {
                    Rows[i] = new TileRow(chunks[i]);
                }
            }
            else
            {
                Rows.Add(new TileRow(chunks[i]));
            }
        }

        while (Rows.Count > chunks.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }

    private Task<TileArt?> LoadCoverAsync(LibraryItem item) => TileArt.LoadAsync(_artwork, item);

    private async Task SetFavoriteAsync(LibraryItem item, bool favorite)
    {
        Notice = null;
        try
        {
            await _collections.SetFavoriteAsync(item, favorite, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCollectionFailed(ex);
            Notice = _localizer["Collections_SaveFailed"];
        }
    }

    /// <summary>
    /// Collections changed: refresh only the tiles whose favorite star (and menu) changed, the collection filter's
    /// options and the open detail view.
    /// </summary>
    private void OnCollectionsChanged()
    {
        RefreshTiles(t => t.IsFavorite != _collections.IsFavorite(t.Item));
        BuildFilterOptions();
        ApplyFilter();
        Detail?.RefreshCollections();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not change a collection")]
    private partial void LogCollectionFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not save a manual item")]
    private partial void LogManualSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the cached library")]
    private partial void LogCacheFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Library scan failed")]
    private partial void LogScanFailed(Exception exception);
}
