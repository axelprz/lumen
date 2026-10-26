// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Providers.Tests;

public sealed class TitleCleanupTests
{
    [Theory]
    [InlineData("The Witcher 3: Wild Hunt - Game of the Year Edition", "The Witcher® 3: Wild Hunt")]
    [InlineData("DOOM Eternal Deluxe Edition", "DOOM Eternal")]
    [InlineData("Forza Horizon 5 for Windows 10", "Forza Horizon 5")]
    [InlineData("Sample Quest - Definitive Edition", "Sample Quest")]
    public void EmbeddedSuffixes_MatchCommonStoreVariants(string a, string b)
    {
        var normalizer = TitleCleanup.CreateNormalizer();

        normalizer.Normalize(a).ShouldBe(normalizer.Normalize(b));
    }

    [Theory]
    [InlineData("StarCraft", "StarCraft: Remastered")]
    [InlineData("Resident Evil 2", "Resident Evil 3")]
    public void DifferentProducts_StayDifferent(string a, string b)
    {
        var normalizer = TitleCleanup.CreateNormalizer();

        normalizer.Normalize(a).ShouldNotBe(normalizer.Normalize(b));
    }
}
