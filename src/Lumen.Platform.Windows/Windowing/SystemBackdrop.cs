// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Lumen.Platform.Windows.Windowing;

/// <summary>
/// Windows 11 Mica backdrop for a window (22H2, build 22621, and later). Everything is best effort and purely
/// cosmetic: on older builds, or with "Transparency effects" off, callers keep the opaque palette.
/// </summary>
public static partial class SystemBackdrop
{
    private const int DwmwaSystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropMica = 2;
    private const int MinimumBuild = 22621;
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when this Windows build has the Mica backdrop API.</summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= MinimumBuild;

    /// <summary>
    /// True when the user allows transparency effects (Settings → Personalization → Colors). Defaults to true when
    /// the value is missing. Windows draws a flat color instead of Mica when it is off.
    /// </summary>
    public static bool TransparencyEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    /// <summary>Turns Mica on and lets it show through the whole client area.</summary>
    /// <returns>False when the build has no Mica or DWM refused it.</returns>
    public static bool TryApplyMica(nint windowHandle) => IsSupported && Set(windowHandle, BackdropMica) && ExtendFrame(windowHandle, -1);

    /// <summary>Back to a plain window.</summary>
    public static void Remove(nint windowHandle)
    {
        if (windowHandle == 0 || !IsSupported)
        {
            return;
        }

        Set(windowHandle, BackdropNone);
        ExtendFrame(windowHandle, 0);
    }

    private static bool Set(nint windowHandle, int type)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        return DwmSetWindowAttribute(windowHandle, DwmwaSystemBackdropType, ref type, sizeof(int)) == 0;
    }

    private static bool ExtendFrame(nint windowHandle, int margin)
    {
        var margins = new Margins { Left = margin, Right = margin, Top = margin, Bottom = margin };
        return DwmExtendFrameIntoClientArea(windowHandle, ref margins) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
}
