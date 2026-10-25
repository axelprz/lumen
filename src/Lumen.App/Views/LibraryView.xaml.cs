// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
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
