// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using Lumen.App.ViewModels;
using Lumen.Core.Shell;

namespace Lumen.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    // While "Change" is recording, the next key combination becomes the quick search shortcut (Esc cancels).
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DataContext is SettingsViewModel { Window: { IsRecordingHotkey: true } window })
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                e.Handled = true; // wait for the actual key
                return;
            }

            e.Handled = true;
            if (key == Key.Escape)
            {
                window.CancelRecordingCommand.Execute(null);
                return;
            }

            _ = window.CaptureHotkeyAsync(Modifiers(), Hotkey.NormalizeKey(KeyName(key)));
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private static HotkeyModifiers Modifiers()
    {
        var pressed = Keyboard.Modifiers;
        var modifiers = HotkeyModifiers.None;
        if (pressed.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (pressed.HasFlag(ModifierKeys.Control))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (pressed.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (pressed.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z or >= Key.F1 and <= Key.F24 => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture),
        Key.Space => "Space",
        _ => null,
    };
}
