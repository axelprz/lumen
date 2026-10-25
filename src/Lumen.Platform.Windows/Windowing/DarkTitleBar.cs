// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Runtime.InteropServices;

namespace Lumen.Platform.Windows.Windowing;

/// <summary>Asks DWM to draw a window's native title bar in dark mode (Windows 10 2004+ / Windows 11).</summary>
public static partial class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <returns>True when DWM accepted the attribute; false on older builds (purely cosmetic).</returns>
    public static bool TryApply(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        var enabled = 1;
        return DwmSetWindowAttribute(windowHandle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) == 0;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
