// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Text.Json;
using Lumen.Core.Library;

namespace Lumen.Providers;

/// <summary>Loads <c>Data/title-cleanup.json</c> into a <see cref="TitleNormalizer"/>.</summary>
public static class TitleCleanup
{
    private const string ResourceName = "Lumen.Providers.Data.title-cleanup.json";

    public static TitleNormalizer CreateNormalizer()
    {
        using var stream = typeof(TitleCleanup).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var doc = JsonDocument.Parse(stream);
        var suffixes = doc.RootElement.GetProperty("editionSuffixes").EnumerateArray().Select(e => e.GetString()!).ToList();
        return new TitleNormalizer(suffixes);
    }
}
