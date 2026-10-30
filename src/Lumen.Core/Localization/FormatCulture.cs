// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;

namespace Lumen.Core.Localization;

/// <summary>Which format numbers and dates use once the user picks a language in Lumen.</summary>
public static class FormatCulture
{
    /// <summary>
    /// Windows' own regional format when it is the same language (Spanish keeps es-AR's), otherwise that language's
    /// usual one (English shows 7.2 and English dates even on a PC set to Argentina).
    /// </summary>
    public static CultureInfo For(CultureInfo language, CultureInfo windowsFormat)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(windowsFormat);
        if (string.Equals(windowsFormat.TwoLetterISOLanguageName, language.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))
        {
            return windowsFormat;
        }

        return language.IsNeutralCulture ? CultureInfo.CreateSpecificCulture(language.Name) : language;
    }
}
