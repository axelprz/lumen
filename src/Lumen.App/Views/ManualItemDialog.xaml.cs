// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Interop;
using Lumen.App.ViewModels;
using Lumen.Platform.Windows.Windowing;

namespace Lumen.App.Views;

public partial class ManualItemDialog : Window
{
    public ManualItemDialog()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ManualItemEditorViewModel editor)
            {
                editor.CloseRequested += (_, saved) => DialogResult = saved;
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.TryApply(new WindowInteropHelper(this).Handle);
    }
}
