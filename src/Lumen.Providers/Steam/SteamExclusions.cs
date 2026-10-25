// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;

namespace Lumen.Providers.Steam;

/// <summary>Steam app ids to hide (redistributables, runtimes), loaded from <c>Data/steam-excluded-apps.json</c>.</summary>
public static class SteamExclusions
{
    private const string ResourceName = "Lumen.Providers.Data.steam-excluded-apps.json";

    private static readonly Lazy<IReadOnlySet<string>> _embedded = new(LoadEmbedded);

    public static IReadOnlySet<string> Embedded => _embedded.Value;

    public static IReadOnlySet<string> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("appIds").EnumerateArray()
            .Select(e => e.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlySet<string> LoadEmbedded()
    {
        using var stream = typeof(SteamExclusions).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
