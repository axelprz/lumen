// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;

namespace Lumen.Core.Localization;

/// <summary>
/// Turns the user's language preference (<c>config.json</c> → <c>language</c>: "system", "en" or "es")
/// into the UI culture Lumen should use.
/// </summary>
public static class LanguageResolver
{
    public const string SystemPreference = "system";
    public const string FallbackLanguage = "en";

    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en", "es"];

    /// <param name="preference">"system", a supported two-letter code, or null. Unknown values behave like "system".</param>
    /// <param name="osUiCulture">The operating system's UI culture.</param>
    public static CultureInfo Resolve(string? preference, CultureInfo osUiCulture)
    {
        ArgumentNullException.ThrowIfNull(osUiCulture);

        if (preference is not null && IsSupported(preference))
        {
            return CultureInfo.GetCultureInfo(preference.ToLowerInvariant());
        }

        // Keep the OS's regional variant (e.g. es-AR) when its language is supported;
        // resource lookup falls back to the neutral culture.
        return IsSupported(osUiCulture.TwoLetterISOLanguageName)
            ? osUiCulture
            : CultureInfo.GetCultureInfo(FallbackLanguage);
    }

    public static bool IsSupported(string language) =>
        SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);
}
