// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
