// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows.Input;
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
    private readonly Func<LibraryItem, Task<string?>> _coverLoader;
    private bool _coverRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private string? _coverPath;

    public LibraryTileViewModel(
        LibraryItem item,
        string storeNames,
        ILocalizer localizer,
        ICommand primaryCommand,
        IReadOnlyList<TileMenuItem> menu,
        Func<LibraryItem, Task<string?>> coverLoader,
        ICommand openDetailCommand)
    {
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

    /// <summary>Called when the tile becomes visible; loads (or downloads once) the cover. Safe to call repeatedly.</summary>
    public async Task EnsureCoverAsync()
    {
        if (_coverRequested)
        {
            return;
        }

        _coverRequested = true;
        CoverPath = await _coverLoader(Item);
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
}
