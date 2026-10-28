// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>
/// Resolves Windows "indirect strings" such as <c>@%SystemRoot%\system32\shell32.dll,-22020</c>, which is how the
/// Start menu shows localized names for shortcuts stored with English file names.
/// </summary>
public interface IIndirectStrings
{
    /// <returns>The text in the Windows display language, or null when it can't be resolved.</returns>
    string? Resolve(string indirect);
}
