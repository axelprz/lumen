// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

public partial class ArtworkSettingsCard : UserControl
{
    public ArtworkSettingsCard()
    {
        InitializeComponent();
    }

    // View concern only: PasswordBox.Password cannot be data-bound, so the click hands it to the command
    // and clears the box right away so the key does not linger in the UI.
    private async void OnSgdbSaveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ArtworkSettingsViewModel vm)
        {
            return;
        }

        var key = SgdbKeyBox.Password;
        SgdbKeyBox.Clear();
        await vm.SaveAndTestCommand.ExecuteAsync(key);
    }
}
