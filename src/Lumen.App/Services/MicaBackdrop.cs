// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lumen.Platform.Windows.Windowing;
using Microsoft.Win32;

namespace Lumen.App.Services;

/// <summary>
/// Windows 11 Mica behind the main window (PLAN-v1 §4.9). The window and the sidebar are painted with translucent
/// versions of the dark palette so Mica tints them; where Mica is not available (older Windows, "Transparency effects"
/// off) the palette stays opaque, exactly as before. Re-evaluated when the user changes the Windows setting.
/// Cosmetic only: nothing here throws to the caller. Create and use on the UI thread.
/// </summary>
public sealed class MicaBackdrop : IDisposable
{
    private const string BackdropKey = "Lumen.Brush.Backdrop";
    private const string SidebarKey = "Lumen.Brush.SidebarBackdrop";
    private const byte BackdropAlpha = 0xD9;
    private const byte SidebarAlpha = 0xC7;

    private readonly Window _window;
    private readonly Color _backdrop;
    private readonly Color _sidebar;
    private bool _active;
    private bool _disposed;

    public MicaBackdrop(Window window)
    {
        _window = window;
        var resources = Application.Current.Resources;
        _backdrop = resources["Lumen.Color.Background"] is Color background ? background : Color.FromRgb(0x0F, 0x11, 0x15);
        _sidebar = resources["Lumen.Color.Sidebar"] is Color sidebar ? sidebar : Color.FromRgb(0x15, 0x18, 0x1E);
    }

    /// <summary>True while Mica is showing.</summary>
    public bool IsActive => _active;

    /// <summary>Applies Mica when this PC can show it and listens for the Windows setting changing. Call once the window has a handle.</summary>
    public void Start()
    {
        Refresh();
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Raised on another thread: hop to the UI thread before touching the window or the resources.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            _ = _window.Dispatcher.BeginInvoke(Refresh);
        }
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var wanted = SystemBackdrop.IsSupported && SystemBackdrop.TransparencyEnabled();
        if (wanted == _active)
        {
            return;
        }

        try
        {
            if (wanted ? TurnOn() : TurnOff())
            {
                _active = wanted;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            TurnOff(); // cosmetic: stay on the opaque palette
        }
    }

    private bool TurnOn()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is null || !SystemBackdrop.TryApplyMica(handle))
        {
            return false;
        }

        // Without this WPF paints the window's surface black before Mica can show through.
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        SetBrushes(BackdropAlpha, SidebarAlpha);
        return true;
    }

    private bool TurnOff()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        SystemBackdrop.Remove(handle);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = _backdrop;
        }

        SetBrushes(0xFF, 0xFF);
        return true;
    }

    private void SetBrushes(byte backdropAlpha, byte sidebarAlpha)
    {
        var resources = Application.Current.Resources;
        resources[BackdropKey] = Frozen(_backdrop, backdropAlpha);
        resources[SidebarKey] = Frozen(_sidebar, sidebarAlpha);
    }

    private static SolidColorBrush Frozen(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
