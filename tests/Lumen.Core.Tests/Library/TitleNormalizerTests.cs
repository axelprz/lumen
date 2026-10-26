// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Tests.Library;

public sealed class TitleNormalizerTests
{
    private readonly TitleNormalizer _normalizer = new(["game of the year edition", "goty edition", "goty", "definitive edition", "deluxe edition", "for windows 10"]);

    [Theory]
    [InlineData("The Witcher® 3: Wild Hunt", "the witcher 3 wild hunt")]
    [InlineData("The Witcher 3: Wild Hunt - Game of the Year Edition", "the witcher 3 wild hunt")]
    [InlineData("THE WITCHER III WILD HUNT GOTY", "the witcher 3 wild hunt")]
    [InlineData("Assassin's Creed® II", "assassins creed 2")]
    [InlineData("Assassin’s Creed 2", "assassins creed 2")]
    [InlineData("Pokémon™ Trading", "pokemon trading")]
    [InlineData("Ori & the Blind Forest: Definitive Edition", "ori and the blind forest")]
    [InlineData("Ori and the Blind Forest", "ori and the blind forest")]
    [InlineData("  Spaced   —   Out!!  ", "spaced out")]
    [InlineData("Final Fantasy XIV", "final fantasy 14")]
    [InlineData("Sample Game Deluxe Edition GOTY", "sample game")]
    [InlineData("Forza Horizon 5 for Windows 10", "forza horizon 5")]
    public void Normalize(string title, string expected)
    {
        _normalizer.Normalize(title).ShouldBe(expected);
    }

    [Fact]
    public void NeverRemovesTheWholeTitle()
    {
        _normalizer.Normalize("GOTY").ShouldBe("goty");
    }

    [Fact]
    public void RemasteredIsNotAnEditionSuffix()
    {
        // "StarCraft" and "StarCraft: Remastered" can be different products: keep them apart.
        _normalizer.Normalize("StarCraft: Remastered").ShouldNotBe(_normalizer.Normalize("StarCraft"));
    }

    [Fact]
    public void SingleLetterI_IsNotTreatedAsRomanNumeral()
    {
        _normalizer.Normalize("I Am Bread").ShouldBe("i am bread");
    }
}
