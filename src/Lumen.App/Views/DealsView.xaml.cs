// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

public partial class DealsView : UserControl
{
    public DealsView()
    {
        InitializeComponent();
    }

    // When the page is shown the free games are asked for (once; the answer is reused for a while).
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DealsViewModel vm)
        {
            _ = vm.EnsureLoadedAsync();
        }
    }
}
