// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using Lumen.App.Services;

namespace Lumen.App.Views;

public partial class CollectionsView : UserControl
{
    public CollectionsView()
    {
        InitializeComponent();
    }

    // The page's blocks (title, intro, the new-collection row, the list) arrive one after the other.
    private void OnLoaded(object sender, RoutedEventArgs e) => Motion.Stagger(PageBlocks);
}
