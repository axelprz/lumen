// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core;

namespace Lumen.Core.Tests;

public sealed class ArchitectureTests
{
    private static readonly string[] _forbiddenForCore =
    [
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
        "System.Windows.Forms",
        "Microsoft.Win32.Registry",
        "Lumen.Data",
        "Lumen.Providers",
        "Lumen.Platform.Windows",
        "Lumen",
    ];

    [Fact]
    public void Core_HasNoWindowsOrUpperLayerReferences()
    {
        var referenced = typeof(LumenPaths).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        referenced.Intersect(_forbiddenForCore).ShouldBeEmpty();
    }
}
