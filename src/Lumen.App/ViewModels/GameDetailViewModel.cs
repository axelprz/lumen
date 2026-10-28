// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Localization;
using Lumen.Core.Platform;

namespace Lumen.App.ViewModels;

/// <summary>Everything the detail view needs; created by <see cref="LibraryViewModel"/>.</summary>
public sealed record DetailServices(
    LaunchService Launcher,
    ILauncher Shell,
    ArtworkService Artwork,
    IMergeRuleStore MergeRules,
    IItemOverrideStore Overrides,
    LibraryService Library,
    IDialogService Dialogs,
    IFileSystem FileSystem,
    ILocalizer Localizer,
    Func<LibraryEntry, string> StoreName);

/// <summary>One store a merged game can be played from, as shown in the detail view's selector.</summary>
public sealed record StoreChoice(LibraryEntry Entry, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Another library item the user can merge this one with.</summary>
public sealed record MergeCandidate(LibraryItem Item, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Detail view of one game or app: hero art and logo, Play/Install, which store to use (merged games), what Lumen
/// knows (stores, playtime, last played, folder) and actions (folder, store page, artwork, rename, hide, merge with
/// another item, separate stores).
/// </summary>
public sealed partial class GameDetailViewModel : ObservableObject
{
    private readonly DetailServices _s;
    private readonly Action _close;
    private bool _loadingChoices;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHero))]
    private string? _heroPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private string? _logoPath;

    [ObservableProperty]
    private string? _coverPath;

    [ObservableProperty]
    private StoreChoice? _selectedStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    [ObservableProperty]
    private bool _isMerging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MergeStatus))]
    private string _mergeSearch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MergeStatus))]
    private IReadOnlyList<MergeCandidate> _mergeCandidates = [];

    public GameDetailViewModel(LibraryItem item, DetailServices services, Action close)
    {
        Item = item;
        _s = services;
        _close = close;

        var loc = services.Localizer;
        StoreChoices = item.Entries
            .Select(e => new StoreChoice(e, e.IsInstalled ? $"{services.StoreName(e)} · {loc["Library_Installed"]}" : services.StoreName(e)))
            .ToList();
        _loadingChoices = true;
        SelectedStore = StoreChoices.First(c => c.Entry == item.Primary);
        _loadingChoices = false;

        StoresText = string.Join(", ", item.Entries.Select(services.StoreName).Distinct());
        var minutes = LibraryFilter.PlaytimeMinutes(item);
        PlaytimeText = minutes <= 0
            ? loc["Detail_NoPlaytime"]
            : minutes < 60
                ? string.Format(CultureInfo.CurrentCulture, loc["Detail_PlaytimeMinutes"], minutes)
                : string.Format(CultureInfo.CurrentCulture, loc["Detail_PlaytimeHours"], minutes / 60.0);
        var lastPlayed = LibraryFilter.LastPlayed(item);
        LastPlayedText = lastPlayed is null ? loc["Detail_NeverPlayed"] : lastPlayed.Value.LocalDateTime.ToString("D", CultureInfo.CurrentCulture);
        InstallPath = item.Entries.Select(e => e.InstallDirectory).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? loc["Detail_NotInstalledPath"];
    }

    public LibraryItem Item { get; }

    public string Title => Item.Title;

    public bool HasHero => HeroPath is not null;

    public bool HasLogo => LogoPath is not null;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public IReadOnlyList<StoreChoice> StoreChoices { get; }

    /// <summary>The store selector only makes sense when the game is owned on more than one store.</summary>
    public bool HasStoreChoice => StoreChoices.Count > 1;

    public LibraryEntry Selected => SelectedStore?.Entry ?? Item.Primary;

    public bool CanRun => LaunchService.PrimaryAction(Selected) is not null;

    public string ActionText => _s.Localizer[!CanRun ? "Action_Unavailable" : Selected.IsInstalled ? "Action_Play" : "Action_Install"];

    public string StoresText { get; }

    public string PlaytimeText { get; }

    public string LastPlayedText { get; }

    public string InstallPath { get; }

    public bool CanOpenFolder => Item.Entries.Any(e => EntryActions.OpenFolder(e, _s.FileSystem) is not null);

    public bool CanOpenStorePage => EntryActions.StorePage(Selected) is not null || Item.Entries.Any(e => EntryActions.StorePage(e) is not null);

    public bool IsMerged => Item.IsMerged;

    /// <summary>The installed copy "Uninstall" acts on: the selected store's if installed, else the first installed one.</summary>
    public LibraryEntry? UninstallTarget =>
        Item.Entries.OrderBy(e => e == Selected ? 0 : 1).FirstOrDefault(e => EntryActions.Uninstall(e) is not null);

    public bool CanUninstall => UninstallTarget is not null;

    public bool IsHidden => Item.IsHidden;

    public bool HasCustomTitle => Item.CustomTitle is not null;

    public string HideText => _s.Localizer[Item.IsHidden ? "Tile_Unhide" : "Tile_Hide"];

    public string RenameHint => string.Format(CultureInfo.CurrentCulture, _s.Localizer["Detail_RenameHint"], Item.OriginalTitle);

    /// <summary>Shown under the merge search box when there is no list to pick from.</summary>
    public string? MergeStatus =>
        MergeCandidates.Count > 0 ? null
        : string.IsNullOrWhiteSpace(MergeSearch) ? _s.Localizer["Detail_MergeTypeToSearch"]
        : _s.Localizer["Detail_MergeNoResults"];

    /// <summary>Most candidates listed at once; typing more narrows it down.</summary>
    public const int MaxMergeCandidates = 30;

    /// <summary>Loads hero, logo and cover (each from cache, or downloaded once).</summary>
    public async Task LoadArtworkAsync()
    {
        HeroPath = await _s.Artwork.GetForItemAsync(Item, ArtworkKind.Hero, CancellationToken.None);
        LogoPath = await _s.Artwork.GetForItemAsync(Item, ArtworkKind.Logo, CancellationToken.None);
        CoverPath = await _s.Artwork.GetForItemAsync(Item, ArtworkKind.Cover, CancellationToken.None);
    }

    /// <summary>Picking a store makes it the one Play uses for this game from now on (same as "Always use").</summary>
    partial void OnSelectedStoreChanged(StoreChoice? value)
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(CanOpenStorePage));
        OnPropertyChanged(nameof(CanUninstall));
        if (_loadingChoices || value is null || !Item.IsMerged || value.Entry == Item.Primary)
        {
            return;
        }

        _ = PreferAsync(value.Entry);
    }

    [RelayCommand]
    private void Close() => _close();

    [RelayCommand]
    private async Task PlayAsync()
    {
        Notice = null;
        if (await _s.Launcher.RunPrimaryActionAsync(Selected, CancellationToken.None) != LaunchOutcome.Started)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, _s.Localizer["Library_LaunchFailed"], Item.Title);
        }
    }

    [RelayCommand]
    private Task OpenFolderAsync()
    {
        var spec = Item.Entries.OrderBy(e => e == Selected ? 0 : 1).Select(e => EntryActions.OpenFolder(e, _s.FileSystem)).FirstOrDefault(s => s is not null);
        return spec is null ? Task.CompletedTask : _s.Shell.StartAsync(spec, CancellationToken.None);
    }

    [RelayCommand]
    private Task OpenStorePageAsync()
    {
        var spec = EntryActions.StorePage(Selected) ?? Item.Entries.Select(EntryActions.StorePage).FirstOrDefault(s => s is not null);
        return spec is null ? Task.CompletedTask : _s.Shell.StartAsync(spec, CancellationToken.None);
    }

    /// <summary>
    /// Hands uninstalling to the store or to Windows Settings and says what to do there. Lumen deletes nothing; the
    /// auto refresh notices when the game is gone.
    /// </summary>
    [RelayCommand]
    private async Task UninstallAsync()
    {
        Notice = null;
        var entry = UninstallTarget;
        if (entry is null || EntryActions.Uninstall(entry) is not { } action)
        {
            return;
        }

        var store = _s.StoreName(entry);
        try
        {
            await _s.Shell.StartAsync(action.Spec, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, _s.Localizer["Detail_UninstallFailed"], store);
            return;
        }

        Notice = action.Route switch
        {
            UninstallRoute.Store => string.Format(CultureInfo.CurrentCulture, _s.Localizer["Detail_UninstallStore"], store),
            UninstallRoute.StoreLibrary => string.Format(CultureInfo.CurrentCulture, _s.Localizer["Detail_UninstallStoreLibrary"], store, entry.Title),
            _ => string.Format(CultureInfo.CurrentCulture, _s.Localizer["Detail_UninstallWindows"], entry.Title),
        };
    }

    [RelayCommand]
    private Task ChangeCoverAsync() => ChangeArtworkAsync(ArtworkKind.Cover);

    [RelayCommand]
    private Task ChangeBackgroundAsync() => ChangeArtworkAsync(ArtworkKind.Hero);

    [RelayCommand]
    private Task ChangeLogoAsync() => ChangeArtworkAsync(ArtworkKind.Logo);

    [RelayCommand]
    private async Task RestoreArtworkAsync()
    {
        foreach (var kind in new[] { ArtworkKind.Cover, ArtworkKind.Hero, ArtworkKind.Logo })
        {
            foreach (var entry in Item.Entries)
            {
                _s.Artwork.RemoveOverride(entry.Ref, kind);
            }
        }

        await LoadArtworkAsync();
        ArtworkChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task SeparateAsync()
    {
        await _s.MergeRules.SeparateAsync(Item.Entries.Select(e => e.Ref), CancellationToken.None);
        await _s.Library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void StartRename()
    {
        IsMerging = false;
        RenameText = Item.Title;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    /// <summary>Saves the name on every entry of the item; the store's own name (or empty) clears it.</summary>
    [RelayCommand]
    private async Task SaveRenameAsync()
    {
        var title = ItemOverride.CleanTitle(RenameText);
        if (string.Equals(title, Item.OriginalTitle, StringComparison.Ordinal))
        {
            title = null;
        }

        IsRenaming = false;
        await SetTitleAsync(title);
    }

    [RelayCommand]
    private Task RestoreTitleAsync()
    {
        IsRenaming = false;
        return SetTitleAsync(null);
    }

    [RelayCommand]
    private async Task ToggleHiddenAsync()
    {
        await _s.Overrides.SetHiddenAsync(Item.Entries.Select(e => e.Ref), !Item.IsHidden, CancellationToken.None);
        await _s.Library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void StartMerge()
    {
        IsRenaming = false;
        MergeSearch = string.Empty;
        MergeCandidates = [];
        IsMerging = true;
    }

    [RelayCommand]
    private void CancelMerge() => IsMerging = false;

    partial void OnMergeSearchChanged(string value) => MergeCandidates = FindCandidates(value);

    /// <summary>
    /// Items of the same kind whose name (or original name) contains the search, ignoring case and accents.
    /// Hidden items are included: merging is also how a hidden duplicate gets folded into the visible one.
    /// </summary>
    public IReadOnlyList<MergeCandidate> FindCandidates(string search)
    {
        var text = search.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var mine = Item.Entries.Select(e => e.Ref).ToHashSet();
        return _s.Library.Items
            .Where(i => i.Kind == Item.Kind && !i.Entries.Any(e => mine.Contains(e.Ref)))
            .Where(i => compare.IndexOf(i.Title, text, options) >= 0 || compare.IndexOf(i.OriginalTitle, text, options) >= 0)
            .OrderBy(i => compare.IsPrefix(i.Title, text, options) ? 0 : 1)
            .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxMergeCandidates)
            .Select(i => new MergeCandidate(i, $"{i.Title} · {string.Join(", ", i.Entries.Select(_s.StoreName).Distinct())}"))
            .ToList();
    }

    /// <summary>
    /// Every entry of <paramref name="candidate"/> joins this item. The result keeps this item's name and visibility,
    /// so merging never hides or renames what the user is looking at.
    /// </summary>
    [RelayCommand]
    private async Task MergeWithAsync(MergeCandidate? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        IsMerging = false;
        var theirs = candidate.Item.Entries.Select(e => e.Ref).ToList();
        await _s.MergeRules.LinkAsync(theirs, Item.Primary.Ref, CancellationToken.None);
        await _s.Overrides.SetHiddenAsync(theirs, Item.IsHidden, CancellationToken.None);
        await _s.Overrides.SetTitleAsync(theirs, Item.CustomTitle, CancellationToken.None);
        await _s.Library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    private async Task SetTitleAsync(string? title)
    {
        await _s.Overrides.SetTitleAsync(Item.Entries.Select(e => e.Ref), title, CancellationToken.None);
        await _s.Library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    /// <summary>Raised after a custom image was set or removed, so the grid can refresh this game's tile.</summary>
    public event EventHandler? ArtworkChanged;

    private async Task PreferAsync(LibraryEntry preferred)
    {
        await _s.MergeRules.SetPreferredAsync(preferred.Ref, Item.Entries.Select(e => e.Ref), CancellationToken.None);
        await _s.Library.ReloadUserChoicesAsync(CancellationToken.None);
    }

    private async Task ChangeArtworkAsync(ArtworkKind kind)
    {
        var file = _s.Dialogs.PickFile(_s.Localizer["Artwork_ImageFilter"]);
        if (file is null)
        {
            return;
        }

        Notice = null;
        if (!await _s.Artwork.SetOverrideAsync(Item.Key, kind, file, CancellationToken.None))
        {
            Notice = _s.Localizer["Artwork_InvalidImage"];
            return;
        }

        await LoadArtworkAsync();
        ArtworkChanged?.Invoke(this, EventArgs.Empty);
    }
}
