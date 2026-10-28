// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Lumen.App.Services;
using Lumen.App.ViewModels;
using Lumen.Platform.Windows.Windowing;

namespace Lumen.App.Views;

public partial class MainWindow : Window
{
    private readonly ImageCache _images;

    public MainWindow(ShellViewModel viewModel, ImageCache images)
    {
        _images = images;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Puts keyboard focus on the first control of the current page, once it has loaded.</summary>
    public void FocusPage() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PageHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.TryApply(new WindowInteropHelper(this).Handle);
        _images.Scale = VisualTreeHelper.GetDpi(this).DpiScaleX; // decode tiles at their real pixel size
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _images.Scale = newDpi.DpiScaleX;
    }
}
