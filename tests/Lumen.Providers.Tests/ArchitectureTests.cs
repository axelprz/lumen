// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Providers.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Providers_StayPure()
    {
        // Providers get OS access through Lumen.Core abstractions so tests can feed fixtures.
        string[] forbidden =
        [
            "PresentationCore",
            "PresentationFramework",
            "WindowsBase",
            "Microsoft.Win32.Registry",
            "Lumen.Platform.Windows",
            "Lumen",
        ];

        var referenced = ProvidersAssembly.Assembly.GetReferencedAssemblies().Select(a => a.Name);

        referenced.Intersect(forbidden).ShouldBeEmpty();
    }
}
