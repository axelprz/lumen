// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Providers.Tests.Fakes;

internal static class Fixtures
{
    /// <summary>Reads tests/fixtures/{relativePath} (copied next to the test assembly).</summary>
    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
