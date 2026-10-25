// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

public enum RegistryHive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>
/// Read-only access to the Windows registry (security rule: never write to a store's keys).
/// Lets providers be tested with in-memory snapshots.
/// </summary>
public interface IRegistryReader
{
    /// <summary>Returns the value as a string, or null when the key/value does not exist or cannot be read.</summary>
    string? GetString(RegistryHive hive, string keyPath, string valueName);

    /// <summary>Names of the direct subkeys, or an empty list when the key does not exist.</summary>
    IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath);
}
