// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lumen.App.Services;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    // The header, the filters and the grid arrive one after the other (the covers inside the grid stay still: it scrolls).
    private void OnLoaded(object sender, RoutedEventArgs e) => Motion.Stagger(PageBlocks);

    // View concern: Esc (or Backspace outside a text box) closes the detail view.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is LibraryViewModel { Detail: { } detail }
            && (e.Key == Key.Escape || (e.Key == Key.Back && e.OriginalSource is not TextBox)))
        {
            detail.CloseCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Tile width (168) + right margin (20).
    private const double TileSlot = 188;

    // Room the row keeps on its left for a pointed-at cover (see the row template's 12 px margin).
    private const double RowInset = 12;

    // How the grid's negative margin shifts the rows (RowsHost Margin="-20,...").
    private const double GridOffset = 20;

    // View concern: how many tiles fit in a row at the current width (scrollbar space reserved), then the rows are
    // centered and the header card takes exactly the tiles' width, so the page has no empty band on the right.
    private void OnGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not LibraryViewModel library || !e.WidthChanged)
        {
            return;
        }

        var available = e.NewSize.Width - SystemParameters.VerticalScrollBarWidth;
        var columns = Math.Max(1, (int)Math.Floor((available - RowInset) / TileSlot));
        library.ColumnCount = columns;

        // The row's tiles (its 12 px left margin comes on top), centered in the grid.
        var tilesWidth = columns * TileSlot;
        RowsHost.Tag = tilesWidth;

        // The covers' visible edges in page coordinates: the grid starts 20 px to the left, the row's margin and the
        // tile's 8 px padding come before the first cover, and the last tile ends with 4 px of margin and 8 of padding.
        var left = Math.Max(0, (available - RowInset - tilesWidth) / 2) - GridOffset + RowInset + 8;
        var width = tilesWidth - 4 - 16;
        foreach (var card in new FrameworkElement[] { HeaderCard, NoticeCard })
        {
            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.Width = Math.Max(0, width);
            card.Margin = new Thickness(Math.Max(0, left), card.Margin.Top, 0, card.Margin.Bottom);
        }
    }

    // View concern: a tile became visible (or was recycled into view), so its cover is wanted now.
    private async void OnTileLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTileViewModel tile })
        {
            await tile.EnsureCoverAsync();
        }
    }

    // View concern: the glow color comes from the cover and is worked out the first time a tile is pointed at or focused.
    private async void OnTileInteract(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTileViewModel tile })
        {
            await tile.EnsureGlowAsync();
        }
    }

    // View concern: the tile menu is created only when the user opens it (right click or the menu key).
    private void OnTileContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryTileViewModel { HasMenu: true } tile } element
            && FindResource("TileMenu") is ContextMenu menu)
        {
            menu.DataContext = tile;
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }

        e.Handled = true;
    }

    // View concern: translate a drag & drop into file paths / URL text for the view model.
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText)
            ? DragDropEffects.Link
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel library)
        {
            return;
        }

        IEnumerable<string> targets = e.Data.GetData(DataFormats.FileDrop) as string[]
            ?? (e.Data.GetData(DataFormats.UnicodeText) is string text
                ? text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : []);
        e.Handled = true;
        await library.AddFromDropAsync(targets);
    }
}
