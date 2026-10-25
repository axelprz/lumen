// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Providers.Tests.Fakes;

/// <summary>In-memory registry snapshot.</summary>
internal sealed class FakeRegistry : IRegistryReader
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public FakeRegistry Set(RegistryHive hive, string keyPath, string valueName, string value)
    {
        _values[Key(hive, keyPath, valueName)] = value;
        return this;
    }

    public string? GetString(RegistryHive hive, string keyPath, string valueName) =>
        _values.GetValueOrDefault(Key(hive, keyPath, valueName));

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        var prefix = $"{hive}\\{keyPath.TrimEnd('\\')}\\";
        return _values.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0])
            .Where(name => name.Length > 0 && !name.Contains('|', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Key(RegistryHive hive, string keyPath, string valueName) => $"{hive}\\{keyPath.TrimEnd('\\')}|{valueName}";
}
