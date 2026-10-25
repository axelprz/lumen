// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Providers;

/// <summary>One program from "Apps &amp; features" (the Windows Uninstall registry keys).</summary>
internal sealed record UninstallEntry(
    string KeyName,
    string? DisplayName,
    string? InstallLocation,
    string? UninstallString,
    string? Publisher,
    string? DisplayIcon);

/// <summary>Reads the Uninstall keys (64-bit and 32-bit views of HKLM, plus HKCU). Read-only.</summary>
internal static class UninstallEntries
{
    public const string Key64 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public const string Key32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<UninstallEntry> ReadAll(IRegistryReader registry)
    {
        var result = new List<UninstallEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, root) in new[]
                 {
                     (RegistryHive.LocalMachine, Key64),
                     (RegistryHive.LocalMachine, Key32),
                     (RegistryHive.CurrentUser, Key64),
                 })
        {
            foreach (var name in registry.GetSubKeyNames(hive, root))
            {
                if (!seen.Add(name))
                {
                    continue;
                }

                var key = $@"{root}\{name}";
                string? Value(string value) => registry.GetString(hive, key, value);
                result.Add(new UninstallEntry(
                    name,
                    Value("DisplayName"),
                    Value("InstallLocation"),
                    Value("UninstallString"),
                    Value("Publisher"),
                    Value("DisplayIcon")));
            }
        }

        return result;
    }
}
