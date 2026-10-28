// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Theming;

namespace Lumen.Core.Tests.Theming;

public sealed class AccentPickerTests
{
    private static readonly Rgb White = new(255, 255, 255);

    [Fact]
    public void Pick_FindsTheSaturatedColor_OverALargerGreyArea()
    {
        var pixels = Image((new Rgb(90, 90, 90), 700), (new Rgb(200, 40, 40), 300));

        var picked = AccentPicker.Pick(pixels).ShouldNotBeNull();

        picked.ToHsl().H.ShouldBeLessThan(10);
    }

    [Fact]
    public void Pick_PrefersTheMoreCommonOfTwoSaturatedColors()
    {
        var pixels = Image((new Rgb(30, 120, 220), 600), (new Rgb(220, 160, 30), 400));

        var (h, _, _) = AccentPicker.Pick(pixels).ShouldNotBeNull().ToHsl();

        h.ShouldBeInRange(200, 220);
    }

    [Fact]
    public void Pick_ReturnsNull_ForColorlessOrTransparentImages()
    {
        AccentPicker.Pick(Image((new Rgb(20, 20, 20), 500), (new Rgb(240, 240, 240), 500))).ShouldBeNull();
        AccentPicker.Pick(Image((new Rgb(200, 40, 40), 100)).Select((b, i) => i % 4 == 3 ? (byte)0 : b).ToArray()).ShouldBeNull();
        AccentPicker.Pick([]).ShouldBeNull();
    }

    [Fact]
    public void Pick_IgnoresPaleTints()
    {
        AccentPicker.Pick(Image((new Rgb(251, 252, 181), 800), (new Rgb(60, 60, 60), 200))).ShouldBeNull();
    }

    [Fact]
    public void Pick_IgnoresSpecks()
    {
        var pixels = Image((new Rgb(128, 128, 128), 1000), (new Rgb(0, 200, 0), 2));

        AccentPicker.Pick(pixels).ShouldBeNull();
    }

    [Theory]
    [InlineData(255, 230, 80)] // too light for white text
    [InlineData(10, 20, 60)] // too dark on the dark background
    [InlineData(76, 141, 255)] // Lumen's default accent
    [InlineData(0, 255, 255)]
    public void Fit_MakesWhiteTextAndBackgroundContrastSafe(byte r, byte g, byte b)
    {
        var fitted = AccentPicker.Fit(new Rgb(r, g, b), AccentPicker.DarkBackground);

        Rgb.Contrast(White, fitted).ShouldBeGreaterThanOrEqualTo(AccentPicker.MinTextContrast);
        Rgb.Contrast(fitted, AccentPicker.DarkBackground).ShouldBeGreaterThanOrEqualTo(AccentPicker.MinBackgroundContrast);
    }

    [Fact]
    public void Fit_KeepsTheHue_AndLeavesSafeColorsAlone()
    {
        var safe = new Rgb(200, 40, 40);
        AccentPicker.Fit(safe, AccentPicker.DarkBackground).ShouldBe(safe);

        var fitted = AccentPicker.Fit(new Rgb(255, 230, 80), AccentPicker.DarkBackground);
        Math.Abs(fitted.ToHsl().H - new Rgb(255, 230, 80).ToHsl().H).ShouldBeLessThan(3);
    }

    [Fact]
    public void HslRoundTrip_IsClose()
    {
        foreach (var color in new[] { new Rgb(200, 40, 40), new Rgb(30, 120, 220), new Rgb(12, 200, 99) })
        {
            var (h, s, l) = color.ToHsl();
            var back = Rgb.FromHsl(h, s, l);
            Math.Abs(back.R - color.R).ShouldBeLessThanOrEqualTo(1);
            Math.Abs(back.G - color.G).ShouldBeLessThanOrEqualTo(1);
            Math.Abs(back.B - color.B).ShouldBeLessThanOrEqualTo(1);
        }
    }

    private static byte[] Image(params (Rgb Color, int Pixels)[] parts)
    {
        var bytes = new List<byte>();
        foreach (var (color, n) in parts)
        {
            for (var i = 0; i < n; i++)
            {
                bytes.AddRange([color.B, color.G, color.R, 255]);
            }
        }

        return bytes.ToArray();
    }
}
