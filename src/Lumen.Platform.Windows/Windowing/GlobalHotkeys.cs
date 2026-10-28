// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Runtime.InteropServices;
using Lumen.Core.Shell;

namespace Lumen.Platform.Windows.Windowing;

/// <summary>
/// System-wide shortcuts through <c>RegisterHotKey</c>: Windows tells the given window when the keys are pressed,
/// whatever app is in front. No keyboard hook, nothing else typed is seen, no admin. The window (a message-only
/// window owned by the app) receives <see cref="WmHotkey"/>.
/// </summary>
public static partial class GlobalHotkeys
{
    public const int WmHotkey = 0x0312;

    private const uint ModNoRepeat = 0x4000;

    /// <returns>False when another app already owns this shortcut (or the key is unknown).</returns>
    public static bool Register(IntPtr window, int id, Hotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        return VirtualKey(hotkey.Key) is { } vk && RegisterHotKey(window, id, (uint)hotkey.Modifiers | ModNoRepeat, vk);
    }

    public static void Unregister(IntPtr window, int id) => UnregisterHotKey(window, id);

    /// <summary>The Windows virtual-key code for a <see cref="Hotkey.Key"/> name.</summary>
    public static uint? VirtualKey(string key) => key switch
    {
        "Space" => 0x20,
        [var c] when c is >= 'A' and <= 'Z' or >= '0' and <= '9' => c, // VK_A.. and VK_0.. equal their ASCII codes
        ['F', .. var n] when int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24 => (uint)(0x70 + f - 1),
        _ => null,
    };

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);
}
