// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;
using Microsoft.Win32;

namespace Lumen.Platform.Windows;

/// <summary>
/// "Start with Windows" as the value <c>Lumen</c> under the current user's
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> key: per user, no admin, and it also shows up (and can be
/// turned off) in Task Manager → Startup apps. Only Lumen's own value is ever read, written or deleted.
/// </summary>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lumen";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public void Enable(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
