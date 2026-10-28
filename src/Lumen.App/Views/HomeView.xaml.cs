// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lumen.App.Converters;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

/// <summary>View concerns only: crossfading the blurred background, lazy covers, pointing at a tile, wheel scrolling.</summary>
public partial class HomeView : UserControl
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(500));
    private static readonly ImagePathConverter Images = new();
    private HomeViewModel? _vm;
    private bool _showingA;

    public HomeView()
    {
        InitializeComponent();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelChanged;
        }

        _vm = DataContext as HomeViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            ShowBackground(_vm.BackgroundPath);
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.BackgroundPath))
        {
            ShowBackground(_vm?.BackgroundPath);
        }
    }

    // The new image goes on the hidden layer and fades in while the visible one fades out.
    private void ShowBackground(string? path)
    {
        var incoming = _showingA ? BackgroundB : BackgroundA;
        var outgoing = _showingA ? BackgroundA : BackgroundB;
        _showingA = !_showingA;

        incoming.Source = Images.Convert(path!, typeof(ImageSource), "640", CultureInfo.InvariantCulture) as ImageSource;
        incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(incoming.Source is null ? 0 : 1, FadeDuration));
        outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(0, FadeDuration));
    }

    private void OnTileLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeTileViewModel tile })
        {
            _ = tile.EnsureCoverAsync();
        }
    }

    private void OnQuickAppLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: QuickAppViewModel app })
        {
            _ = app.EnsureIconAsync();
        }
    }

    private void OnTilePointed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HomeTileViewModel tile } && _vm is not null)
        {
            _ = _vm.PreviewAsync(tile.Item);
        }
    }

    // The row has no visible scrollbar: over it, the wheel scrolls it sideways (keyboard focus also brings tiles
    // into view). When the row fits, the wheel goes on to scroll the page.
    private void OnRowMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer row || row.ScrollableWidth <= 0)
        {
            return;
        }

        e.Handled = true;
        row.ScrollToHorizontalOffset(row.HorizontalOffset - e.Delta);
    }
}
