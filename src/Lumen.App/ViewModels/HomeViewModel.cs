// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>A game in a Home row: its cover, and pointing at it features it in the hero.</summary>
public sealed partial class HomeTileViewModel(LibraryItem item, Func<LibraryItem, Task<TileArt?>> coverLoader, ImageCache images) : ObservableObject
{
    public const double CoverWidth = 150;

    public const double IconWidth = 96;

    private readonly ImageCache _images = images;
    private bool _coverRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(CoverImage))]
    private string? _coverPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(IconImage))]
    private string? _iconPath;

    public LibraryItem Item { get; } = item;

    public ImageSource? CoverImage => Cached(CoverPath, CoverWidth, nameof(CoverImage));

    public ImageSource? IconImage => Cached(IconPath, IconWidth, nameof(IconImage));

    public bool HasIcon => IconPath is not null;

    public string Title => Item.Title;

    public bool HasCover => CoverPath is not null;

    public string Initial => Item.Title.Length == 0 ? "?" : StringInfo.GetNextTextElement(Item.Title).ToUpper(CultureInfo.CurrentCulture);

    // What screen readers announce for the row's items.
    public override string ToString() => Title;
    // Images come from the shared in-memory cache: instant when already decoded, otherwise decoded off the UI
    // thread and announced when ready. The tile never holds the bitmap itself, so memory stays bounded.
    private ImageSource? Cached(string? path, double width, string property)
    {
        if (path is null)
        {
            return null;
        }

        if (_images.TryGet(path, width) is { } hit)
        {
            return hit;
        }

        _ = LoadThenNotifyAsync(path, width, property);
        return null;
    }

    private async Task LoadThenNotifyAsync(string path, double width, string property)
    {
        if (await _images.LoadAsync(path, width) is not null)
        {
            OnPropertyChanged(property);
        }
    }


    public async Task EnsureCoverAsync()
    {
        if (_coverRequested)
        {
            return;
        }

        _coverRequested = true;
        var art = await coverLoader(Item);
        if (art is { IsIcon: true })
        {
            IconPath = art.Path;
        }
        else
        {
            CoverPath = art?.Path;
        }
    }
}

/// <summary>An app in Home's "Quick apps" dock: its icon; a click opens it.</summary>
public sealed partial class QuickAppViewModel(LibraryItem item, ArtworkService artwork) : ObservableObject
{
    private bool _iconRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private string? _iconPath;

    public LibraryItem Item { get; } = item;

    public string Title => Item.Title;

    public bool HasIcon => IconPath is not null;

    public string Initial => Item.Title.Length == 0 ? "?" : StringInfo.GetNextTextElement(Item.Title).ToUpper(CultureInfo.CurrentCulture);

    public override string ToString() => Title;

    public async Task EnsureIconAsync()
    {
        if (_iconRequested)
        {
            return;
        }

        _iconRequested = true;
        try
        {
            IconPath = await artwork.GetForItemAsync(Item, ArtworkKind.Icon, CancellationToken.None)
                ?? await artwork.GetForItemAsync(Item, ArtworkKind.Cover, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Decoration: the initial stays.
        }
    }
}

/// <summary>
/// Home (PLAN-v1 §4.3): a hero with the featured game over its blurred background art (logo, Play, playtime, last
/// played), the "Continue playing" and "Favorites" rows and the "Quick apps" dock. Pointing at a game in a row
/// features it; the accent follows the featured game. Pinned routines join in M8.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private static readonly TimeSpan FocusDelay = TimeSpan.FromMilliseconds(180);

    private readonly LibraryService _library;
    private readonly LaunchService _launcher;
    private readonly ArtworkService _artwork;
    private readonly AccentService _accent;
    private readonly CollectionService _collections;
    private readonly ImageCache _images;
    private readonly ILocalizer _localizer;
    private readonly SynchronizationContext _ui;
    private LibraryItem? _defaultFeatured;
    private int _featureVersion;
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFeatured), nameof(HasNoGames))]
    private LibraryItem? _featured;

    [ObservableProperty]
    private string? _backgroundPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private string? _logoPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string? _notice;

    public HomeViewModel(LibraryService library, LaunchService launcher, ArtworkService artwork, AccentService accent, CollectionService collections, ImageCache images, ILocalizer localizer)
    {
        _images = images;
        _library = library;
        _launcher = launcher;
        _artwork = artwork;
        _accent = accent;
        _collections = collections;
        _localizer = localizer;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _library.Changed += (_, _) => _ui.Post(_ => Rebuild(), null);
        _collections.Changed += (_, _) => _ui.Post(_ => Rebuild(), null);
        _localizer.CultureChanged += (_, _) => RaiseFeaturedTexts();
    }

    /// <summary>Raised when the user asks for a game's detail view (the shell shows it in the library).</summary>
    public event EventHandler<LibraryItem>? DetailRequested;

    public IReadOnlyList<HomeTileViewModel> ContinuePlaying { get; private set; } = [];

    public IReadOnlyList<HomeTileViewModel> Favorites { get; private set; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    public IReadOnlyList<QuickAppViewModel> QuickApps { get; private set; } = [];

    public bool HasQuickApps => QuickApps.Count > 0;

    public bool HasContinuePlaying => ContinuePlaying.Count > 0;

    public bool HasFeatured => Featured is not null;

    /// <summary>Nothing to show at all: no installed or played games yet.</summary>
    public bool HasNoGames => Featured is null;

    public bool HasLogo => LogoPath is not null;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public string FeaturedTitle => Featured?.Title ?? string.Empty;

    public string ActionText => Featured is null ? string.Empty
        : _localizer[LaunchService.PrimaryAction(Featured.Primary) is null ? "Action_Unavailable" : Featured.Primary.IsInstalled ? "Action_Play" : "Action_Install"];

    public bool CanRun => Featured is not null && LaunchService.PrimaryAction(Featured.Primary) is not null;

    /// <summary>"12.5 h played · Last played: Monday, 26 October 2026", leaving out what isn't known.</summary>
    public string FeaturedFacts
    {
        get
        {
            if (Featured is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            var minutes = LibraryFilter.PlaytimeMinutes(Featured);
            if (minutes > 0)
            {
                parts.Add(minutes < 60
                    ? string.Format(CultureInfo.CurrentCulture, _localizer["Home_PlayedMinutes"], minutes)
                    : string.Format(CultureInfo.CurrentCulture, _localizer["Home_PlayedHours"], minutes / 60.0));
            }

            if (LibraryFilter.LastPlayed(Featured) is { } last)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, _localizer["Home_LastPlayed"], last.LocalDateTime.ToString("D", CultureInfo.CurrentCulture)));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The shell calls this when Home becomes (or stops being) the visible page.</summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        if (active)
        {
            if (ContinuePlaying.Count == 0 && Featured is null)
            {
                Rebuild();
            }

            _ = _accent.ApplyFromImageAsync(BackgroundPath);
        }
    }

    /// <summary>Pointing at (or focusing) a game in a row features it after a short pause, so sweeping across doesn't flicker.</summary>
    public async Task PreviewAsync(LibraryItem item)
    {
        var version = ++_featureVersion;
        await Task.Delay(FocusDelay);
        if (version == _featureVersion && !ReferenceEquals(item, Featured))
        {
            await FeatureAsync(item, version);
        }
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        Notice = null;
        if (Featured is { } item && await _launcher.RunPrimaryActionAsync(item.Primary, CancellationToken.None) != LaunchOutcome.Started)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, _localizer["Library_LaunchFailed"], item.Title);
        }
    }

    [RelayCommand]
    private void OpenDetail(LibraryItem? item)
    {
        if ((item ?? Featured) is { } target)
        {
            DetailRequested?.Invoke(this, target);
        }
    }

    /// <summary>Opens an app from the Quick apps dock.</summary>
    [RelayCommand]
    private async Task OpenAppAsync(QuickAppViewModel? app)
    {
        Notice = null;
        if (app is not null && await _launcher.RunPrimaryActionAsync(app.Item.Primary, CancellationToken.None) != LaunchOutcome.Started)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, _localizer["Library_LaunchFailed"], app.Title);
        }
    }

    private void Rebuild()
    {
        var feed = HomeFeed.Build(_library.Items, _library.FirstSeen, _collections.Favorites);

        // Reuse tiles of unchanged items so their covers stay loaded.
        var previous = new Dictionary<ItemRef, HomeTileViewModel>();
        foreach (var tile in ContinuePlaying.Concat(Favorites))
        {
            previous.TryAdd(tile.Item.Key, tile);
        }

        HomeTileViewModel Tile(LibraryItem item) =>
            previous.TryGetValue(item.Key, out var tile) && ReferenceEquals(tile.Item, item) ? tile : new HomeTileViewModel(item, LoadCoverAsync, _images);

        ContinuePlaying = feed.ContinuePlaying.Select(Tile).ToList();
        Favorites = feed.Favorites.Select(Tile).ToList();
        var previousApps = QuickApps.ToDictionary(a => a.Item.Key);
        QuickApps = feed.QuickApps
            .Select(i => previousApps.TryGetValue(i.Key, out var app) && ReferenceEquals(app.Item, i) ? app : new QuickAppViewModel(i, _artwork))
            .ToList();
        OnPropertyChanged(nameof(ContinuePlaying));
        OnPropertyChanged(nameof(HasContinuePlaying));
        OnPropertyChanged(nameof(Favorites));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(QuickApps));
        OnPropertyChanged(nameof(HasQuickApps));

        // Keep the game the user is pointing at if it's still there (fresh copy); otherwise show the default.
        _defaultFeatured = feed.Featured;
        var keep = Featured is null ? null : _library.Items.FirstOrDefault(i => i.Key == Featured.Key && !i.IsHidden);
        _ = FeatureAsync(keep ?? _defaultFeatured, ++_featureVersion);
    }

    private async Task FeatureAsync(LibraryItem? item, int version)
    {
        Featured = item;
        RaiseFeaturedTexts();
        if (item is null)
        {
            BackgroundPath = null;
            LogoPath = null;
            return;
        }

        var hero = await LoadAsync(item, ArtworkKind.Hero) ?? await LoadAsync(item, ArtworkKind.Cover);
        var logo = await LoadAsync(item, ArtworkKind.Logo);
        if (version != _featureVersion)
        {
            return;
        }

        BackgroundPath = hero;
        LogoPath = logo;
        if (_isActive)
        {
            await _accent.ApplyFromImageAsync(hero);
        }
    }

    private void RaiseFeaturedTexts()
    {
        OnPropertyChanged(nameof(FeaturedTitle));
        OnPropertyChanged(nameof(FeaturedFacts));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(CanRun));
    }

    private Task<TileArt?> LoadCoverAsync(LibraryItem item) => TileArt.LoadAsync(_artwork, item);

    // Artwork is decoration: a failure just means no image.
    private async Task<string?> LoadAsync(LibraryItem item, ArtworkKind kind)
    {
        try
        {
            return await _artwork.GetForItemAsync(item, kind, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
