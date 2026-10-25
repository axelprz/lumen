// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Security;
using Lumen.Core.Platform;
using Microsoft.Win32;
using CoreHive = Lumen.Core.Platform.RegistryHive;

namespace Lumen.Platform.Windows;

/// <summary>Opens keys read-only, always. Lumen never writes to a store's registry keys.</summary>
public sealed class WindowsRegistryReader : IRegistryReader
{
    public string? GetString(CoreHive hive, string keyPath, string valueName)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath, writable: false);
            return key?.GetValue(valueName) switch
            {
                string s => s,
                null => null,
                var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(CoreHive hive, string keyPath)
    {
        try
        {
            using var key = Root(hive).OpenSubKey(keyPath, writable: false);
            return key?.GetSubKeyNames() ?? [];
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static RegistryKey Root(CoreHive hive) => hive switch
    {
        CoreHive.CurrentUser => Registry.CurrentUser,
        CoreHive.LocalMachine => Registry.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, null),
    };
}
