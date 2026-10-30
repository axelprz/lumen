// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lumen.App.Services;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

public partial class RoutinesView : UserControl
{
    public RoutinesView()
    {
        InitializeComponent();
    }

    // The page's blocks (title, intro, notice, the routines) arrive one after the other.
    private void OnLoaded(object sender, RoutedEventArgs e) => Motion.Stagger(PageBlocks);

    // View concern: Esc closes the preview or the editor (gamepad B too).
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is RoutinesViewModel { HasPanel: true } page && e.OriginalSource is not ComboBox)
        {
            page.ClosePanelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
