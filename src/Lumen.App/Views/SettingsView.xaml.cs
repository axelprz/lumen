// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    // View concern only: PasswordBox.Password cannot be data-bound, so the click hands it to the command
    // and clears the box right away so the key does not linger in the UI.
    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Steam: var steam })
        {
            return;
        }

        var key = ApiKeyBox.Password;
        ApiKeyBox.Clear();
        await steam.SaveAndTestCommand.ExecuteAsync(key);
    }

    // Same view concern for the SteamGridDB key.
    private async void OnSgdbSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Artwork: var artwork })
        {
            return;
        }

        var key = SgdbKeyBox.Password;
        SgdbKeyBox.Clear();
        await artwork.SaveAndTestCommand.ExecuteAsync(key);
    }
}
