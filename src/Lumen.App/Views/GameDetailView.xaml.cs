// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Lumen.App.Views;

public partial class GameDetailView : UserControl
{
    public GameDetailView()
    {
        InitializeComponent();
    }

    // View concern: the "More" button opens its menu on a normal click, right under the button.
    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.DataContext = DataContext;
            menu.PlacementTarget = MoreButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    // View concern: when the rename or merge box appears, put the cursor in it (rename text selected).
    private void OnEditorVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && box.IsVisible)
        {
            box.Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            });
        }
    }
}
