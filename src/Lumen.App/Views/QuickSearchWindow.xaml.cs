// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Lumen.App.ViewModels;

namespace Lumen.App.Views;

/// <summary>
/// The small search box over any app. Keys are view concerns: ↑/↓ move the selection, Enter runs it, Esc or
/// clicking elsewhere hides the box. The window is reused, never closed until Lumen exits.
/// </summary>
public partial class QuickSearchWindow : Window
{
    private readonly QuickSearchViewModel _viewModel;
    private bool _exiting;

    public QuickSearchWindow(QuickSearchViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Done += (_, _) => Hide();
        Deactivated += (_, _) => Hide();
        ResultList.MouseLeftButtonUp += (_, _) => _viewModel.RunCommand.Execute(null);
    }

    /// <summary>Opens the box empty, centered in the upper part of the main screen, ready to type.</summary>
    public void Open()
    {
        _viewModel.Reset();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.22;
        Show();
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>Lets the window really close (Lumen is exiting).</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true; // Alt+F4 just hides it
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _viewModel.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.RunCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
        }

        base.OnPreviewKeyDown(e);
    }
}
