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

    /// <summary>Loads a registry snapshot fixture: { "LocalMachine": { "key\\path": { "value": "data" } } }.</summary>
    public FakeRegistry LoadFixture(string fixture)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Fixtures.Read(fixture));
        foreach (var hive in doc.RootElement.EnumerateObject())
        {
            var parsedHive = Enum.Parse<RegistryHive>(hive.Name);
            foreach (var key in hive.Value.EnumerateObject())
            {
                foreach (var value in key.Value.EnumerateObject())
                {
                    Set(parsedHive, key.Name, value.Name, value.Value.GetString() ?? string.Empty);
                }
            }
        }

        return this;
    }

    public string? GetString(RegistryHive hive, string keyPath, string valueName) =>
        _values.GetValueOrDefault(Key(hive, keyPath, valueName));

    public IReadOnlyList<string> GetSubKeyNames(RegistryHive hive, string keyPath)
    {
        var prefix = $"{hive}\\{keyPath.TrimEnd('\\')}\\";
        return _values.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\', '|')[0])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Key(RegistryHive hive, string keyPath, string valueName) => $"{hive}\\{keyPath.TrimEnd('\\')}|{valueName}";
}
