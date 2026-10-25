// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Providers;

/// <summary>Marker type for referencing this assembly (DI scanning, architecture tests). Providers arrive in M1.</summary>
public static class ProvidersAssembly
{
    public static System.Reflection.Assembly Assembly => typeof(ProvidersAssembly).Assembly;
}
