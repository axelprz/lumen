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

/// <summary>
/// Detail view of one game or app: hero art and logo, Play/Install, which store to use (merged games), what Lumen
/// knows (stores, playtime, last played, folder) and actions (folder, store page, artwork, separate stores).
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
        await _s.Library.ReloadMergeRulesAsync(CancellationToken.None);
    }

    /// <summary>Raised after a custom image was set or removed, so the grid can refresh this game's tile.</summary>
    public event EventHandler? ArtworkChanged;

    private async Task PreferAsync(LibraryEntry preferred)
    {
        await _s.MergeRules.SetPreferredAsync(preferred.Ref, Item.Entries.Select(e => e.Ref), CancellationToken.None);
        await _s.Library.ReloadMergeRulesAsync(CancellationToken.None);
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
