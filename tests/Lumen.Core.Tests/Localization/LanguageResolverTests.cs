// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Localization;

namespace Lumen.Core.Tests.Localization;

public sealed class LanguageResolverTests
{
    [Theory]
    [InlineData("system", "es-AR", "es-AR")]
    [InlineData("system", "en-GB", "en-GB")]
    [InlineData("system", "fr-FR", "en")]
    [InlineData("system", "ja-JP", "en")]
    [InlineData(null, "es-ES", "es-ES")]
    [InlineData("fr", "es-MX", "es-MX")]
    [InlineData("es", "en-US", "es")]
    [InlineData("ES", "en-US", "es")]
    [InlineData("en", "es-AR", "en")]
    public void Resolve_PicksExpectedCulture(string? preference, string os, string expected)
    {
        var culture = LanguageResolver.Resolve(preference, CultureInfo.GetCultureInfo(os));

        culture.Name.ShouldBe(expected);
    }

    [Fact]
    public void Resolve_InvariantOsCulture_FallsBackToEnglish()
    {
        LanguageResolver.Resolve("system", CultureInfo.InvariantCulture).Name.ShouldBe("en");
    }

    [Fact]
    public void SupportedLanguages_MatchConfigSchema()
    {
        // docs/schemas/config.schema.json → language: ["system", "en", "es"]
        LanguageResolver.SupportedLanguages.ShouldBe(["en", "es"], ignoreOrder: true);
    }
}
