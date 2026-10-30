// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Localization;

namespace Lumen.Core.Tests.Localization;

public sealed class FormatCultureTests
{
    private static readonly CultureInfo Argentina = CultureInfo.GetCultureInfo("es-AR");

    [Fact]
    public void SameLanguageAsWindows_KeepsWindowsRegionalFormat()
    {
        FormatCulture.For(CultureInfo.GetCultureInfo("es"), Argentina).ShouldBe(Argentina);
    }

    [Fact]
    public void OtherLanguage_UsesThatLanguagesFormat()
    {
        var format = FormatCulture.For(CultureInfo.GetCultureInfo("en"), Argentina);

        format.TwoLetterISOLanguageName.ShouldBe("en");
        format.IsNeutralCulture.ShouldBeFalse();
        7.2.ToString("0.0", format).ShouldBe("7.2");
    }

    [Fact]
    public void SpanishOnAnEnglishWindows_UsesSpanishDecimals()
    {
        7.2.ToString("0.0", FormatCulture.For(CultureInfo.GetCultureInfo("es"), CultureInfo.GetCultureInfo("en-US"))).ShouldBe("7,2");
    }
}
