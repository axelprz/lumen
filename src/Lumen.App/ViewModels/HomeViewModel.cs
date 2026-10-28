// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumen.App.Services;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>A game in a Home row: its cover, and pointing at it features it in the hero.</summary>
public sealed partial class HomeTileViewModel(LibraryItem item, Func<LibraryItem, Task<TileArt?>> coverLoader) : ObservableObject
{
    private bool _coverRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private string? _iconPath;

    public LibraryItem Item { get; } = item;

    public bool HasIcon => IconPath is not null;

    public string Title => Item.Title;

    public bool HasCover => CoverPath is not null;

    public string Initial => Item.Title.Length == 0 ? "?" : StringInfo.GetNextTextElement(Item.Title).ToUpper(CultureInfo.CurrentCulture);

    // What screen readers announce for the row's items.
    public override string ToString() => Title;

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

/// <summary>
/// Home (PLAN-v1 §4.3): a hero with the featured game over its blurred background art (logo, Play, playtime, last
/// played) and the "Continue playing" row. Pointing at a game in the row features it; the accent follows the
/// featured game. Favorites and quick apps join in M4, pinned routines in M8.
/// </summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private static readonly TimeSpan FocusDelay = TimeSpan.FromMilliseconds(180);

    private readonly LibraryService _library;
    private readonly LaunchService _launcher;
    private readonly ArtworkService _artwork;
    private readonly AccentService _accent;
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

    public HomeViewModel(LibraryService library, LaunchService launcher, ArtworkService artwork, AccentService accent, ILocalizer localizer)
    {
        _library = library;
        _launcher = launcher;
        _artwork = artwork;
        _accent = accent;
        _localizer = localizer;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _library.Changed += (_, _) => _ui.Post(_ => Rebuild(), null);
        _localizer.CultureChanged += (_, _) => RaiseFeaturedTexts();
    }

    /// <summary>Raised when the user asks for a game's detail view (the shell shows it in the library).</summary>
    public event EventHandler<LibraryItem>? DetailRequested;

    public IReadOnlyList<HomeTileViewModel> ContinuePlaying { get; private set; } = [];

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

    private void Rebuild()
    {
        var feed = HomeFeed.Build(_library.Items, _library.FirstSeen);
        var previous = ContinuePlaying.ToDictionary(t => t.Item.Key);
        ContinuePlaying = feed.ContinuePlaying
            .Select(i => previous.TryGetValue(i.Key, out var tile) && ReferenceEquals(tile.Item, i) ? tile : new HomeTileViewModel(i, LoadCoverAsync))
            .ToList();
        OnPropertyChanged(nameof(ContinuePlaying));
        OnPropertyChanged(nameof(HasContinuePlaying));

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
