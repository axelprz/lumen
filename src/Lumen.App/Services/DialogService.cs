// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using Lumen.App.ViewModels;
using Lumen.App.Views;
using Microsoft.Win32;

namespace Lumen.App.Services;

/// <summary>Opens windows and system dialogs for view models, so they stay free of view types.</summary>
public interface IDialogService
{
    /// <returns>True when the user saved.</returns>
    bool ShowManualItemEditor(ManualItemEditorViewModel editor);

    bool Confirm(string title, string message);

    /// <returns>The chosen file, or null when cancelled. Shortcuts are returned as .lnk, not resolved.</returns>
    string? PickFile(string filter);

    /// <returns>The chosen folder, or null when cancelled.</returns>
    string? PickFolder();

    /// <returns>Where to save, or null when cancelled.</returns>
    string? PickSaveFile(string fileName, string filter);
}

public sealed class DialogService : IDialogService
{
    public bool ShowManualItemEditor(ManualItemEditorViewModel editor)
    {
        var dialog = new ManualItemDialog { DataContext = editor, Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public string? PickFile(string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true,
            DereferenceLinks = false,
            Multiselect = false,
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Multiselect = false };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    public string? PickSaveFile(string fileName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = fileName, Filter = filter, AddExtension = true, OverwritePrompt = true };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FileName : null;
    }
}
