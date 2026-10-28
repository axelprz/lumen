// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Lumen.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Core.Library;
using Lumen.Core.Localization;

namespace Lumen.App.ViewModels;

/// <summary>One entry of a tile's context menu. A null command draws a separator.</summary>
public sealed record TileMenuItem(string Header, ICommand? Command)
{
    public bool IsSeparator => Command is null;
}

/// <summary>
/// One cover in the library grid: a game or app, possibly owned on several stores (merged). Shows the cover art
/// once it is loaded (lazily, when the tile first appears), the title's initial until then or when there is none.
/// </summary>
public sealed partial class LibraryTileViewModel : ObservableObject
{
    /// <summary>On-screen sizes (device-independent pixels) the tile's images are decoded for.</summary>
    public const double CoverWidth = 168;

    public const double IconWidth = 112;

    private readonly Func<LibraryItem, Task<TileArt?>> _coverLoader;
    private readonly ImageCache _images;
    private bool _coverRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover), nameof(CoverImage))]
    private string? _coverPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon), nameof(IconImage))]
    private string? _iconPath;

    public LibraryTileViewModel(
        LibraryItem item,
        string storeNames,
        ILocalizer localizer,
        ICommand primaryCommand,
        IReadOnlyList<TileMenuItem> menu,
        Func<LibraryItem, Task<TileArt?>> coverLoader,
        ICommand openDetailCommand,
        ImageCache images)
    {
        _images = images;
        _coverLoader = coverLoader;
        OpenDetailCommand = openDetailCommand;
        Item = item;
        StoreName = storeNames;
        PrimaryCommand = primaryCommand;
        MenuItems = menu;
        CanRunPrimary = LaunchService.PrimaryAction(item.Primary) is not null;
        var playable = item.Primary.IsInstalled;
        ActionText = localizer[!CanRunPrimary ? "Action_Unavailable" : !playable ? "Action_Install" : item.Kind == ItemKind.App ? "Action_Open" : "Action_Play"];
        StateText = playable ? string.Empty : localizer[CanRunPrimary ? "Library_NotInstalled" : "Library_NotAvailable"];
        Initial = item.Title.Length == 0
            ? "?"
            : StringInfo.GetNextTextElement(item.Title).ToUpper(CultureInfo.CurrentCulture);
    }

    public LibraryItem Item { get; }

    /// <summary>The entry Play/Install uses.</summary>
    public LibraryEntry Entry => Item.Primary;

    public bool HasCover => CoverPath is not null;

    public ImageSource? CoverImage => Cached(CoverPath, CoverWidth, nameof(CoverImage));

    public ImageSource? IconImage => Cached(IconPath, IconWidth, nameof(IconImage));

    /// <summary>In Favorites: the tile shows a small star.</summary>
    public bool IsFavorite { get; init; }

    /// <summary>No cover, but an icon (apps, some Store games): shown centered.</summary>
    public bool HasIcon => IconPath is not null;

    /// <summary>Called when the tile becomes visible; loads (or downloads once) the cover. Safe to call repeatedly.</summary>
    public async Task EnsureCoverAsync()
    {
        if (_coverRequested)
        {
            return;
        }

        _coverRequested = true;
        var art = await _coverLoader(Item);
        if (art is { IsIcon: true })
        {
            IconPath = art.Path;
        }
        else
        {
            CoverPath = art?.Path;
        }
    }

    public string Title => Item.Title;

    /// <summary>True when the entry Play uses is installed (drives the greyed-out look).</summary>
    public bool IsInstalled => Item.Primary.IsInstalled;

    /// <summary>False when there is nothing to run (e.g. a manual item whose file is not on this PC).</summary>
    public bool CanRunPrimary { get; }

    /// <summary>Store badge text: one store, or several joined with " · " for a merged game.</summary>
    public string StoreName { get; }

    public string Initial { get; }

    /// <summary>"Play", "Install" or "Unavailable".</summary>
    public string ActionText { get; }

    public string StateText { get; }

    public ICommand PrimaryCommand { get; }

    /// <summary>Opens this game's detail view (click or Enter on the cover).</summary>
    public ICommand OpenDetailCommand { get; }

    public IReadOnlyList<TileMenuItem> MenuItems { get; }

    public bool HasMenu => MenuItems.Count > 0;

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
}
