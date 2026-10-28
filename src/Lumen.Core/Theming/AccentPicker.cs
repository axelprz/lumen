// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Theming;

/// <summary>An opaque sRGB color.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>WCAG 2 relative luminance (0 = black, 1 = white).</summary>
    public double Luminance => (0.2126 * Linear(R)) + (0.7152 * Linear(G)) + (0.0722 * Linear(B));

    /// <summary>WCAG 2 contrast ratio between two colors (1 to 21).</summary>
    public static double Contrast(Rgb a, Rgb b)
    {
        var (la, lb) = (a.Luminance, b.Luminance);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        if (d < 1e-9)
        {
            return (0, 0, l);
        }

        var s = d / (1 - Math.Abs((2 * l) - 1));
        double h;
        if (max == r)
        {
            h = ((g - b) / d) % 6;
        }
        else if (max == g)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        h *= 60;
        return (h < 0 ? h + 360 : h, Math.Clamp(s, 0, 1), l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs((2 * l) - 1)) * s;
        var x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        var m = l - (c / 2);
        var (r, g, b) = (h % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}

/// <summary>
/// Picks an accent color from artwork and makes it safe to use: the most "characteristic" color (common and
/// saturated, not near-black/white/grey), then adjusted so white text on it and it on the dark background stay
/// readable (WCAG contrast). Pure: works on raw pixels so it is testable without images.
/// </summary>
public static class AccentPicker
{
    /// <summary>Lumen's dark background (<c>Lumen.Color.Background</c>).</summary>
    public static readonly Rgb DarkBackground = new(0x0F, 0x11, 0x15);

    /// <summary>White text on accent buttons: WCAG AA for normal text.</summary>
    public const double MinTextContrast = 4.5;

    /// <summary>Accent borders and highlights on the background: WCAG AA for UI components.</summary>
    public const double MinBackgroundContrast = 3.0;

    private static readonly Rgb White = new(255, 255, 255);

    /// <param name="bgra">Pixels as B, G, R, A bytes (WPF's Bgra32), usually a small downscaled image.</param>
    /// <returns>The dominant saturated color, or null when the image is (nearly) colorless.</returns>
    public static Rgb? Pick(ReadOnlySpan<byte> bgra)
    {
        // Quantize to 4 bits per channel: 4096 buckets, averaging the real colors inside each.
        var count = new int[4096];
        var sumR = new long[4096];
        var sumG = new long[4096];
        var sumB = new long[4096];
        var total = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128)
            {
                continue; // transparent (logos, icons)
            }

            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            count[key]++;
            sumR[key] += r;
            sumG[key] += g;
            sumB[key] += b;
            total++;
        }

        if (total == 0)
        {
            return null;
        }

        var minCount = Math.Max(1, total / 200); // ignore specks under 0.5% of the image
        Rgb? best = null;
        var bestScore = 0.0;
        for (var key = 0; key < count.Length; key++)
        {
            var n = count[key];
            if (n < minCount)
            {
                continue;
            }

            var color = new Rgb((byte)(sumR[key] / n), (byte)(sumG[key] / n), (byte)(sumB[key] / n));
            var (_, _, l) = color.ToHsl();

            // Chroma (max - min), not HSL saturation: HSL calls pale tints like (250, 250, 180) "saturated".
            var chroma = (Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B))) / 255.0;
            if (chroma < 0.15 || l < 0.12 || l > 0.8)
            {
                continue;
            }

            var score = n * chroma * chroma * (1 - Math.Abs(l - 0.5));
            if (score > bestScore)
            {
                bestScore = score;
                best = color;
            }
        }

        return best;
    }

    /// <summary>
    /// Keeps hue and saturation and moves lightness until white text on the color reaches
    /// <see cref="MinTextContrast"/> and the color on <paramref name="background"/> reaches
    /// <see cref="MinBackgroundContrast"/>. When both can't hold (a light background), text readability wins.
    /// </summary>
    public static Rgb Fit(Rgb color, Rgb background)
    {
        var maxLuminance = ((White.Luminance + 0.05) / MinTextContrast) - 0.05;
        var minLuminance = (MinBackgroundContrast * (background.Luminance + 0.05)) - 0.05;
        var lum = color.Luminance;
        if (lum <= maxLuminance && (lum >= minLuminance || minLuminance > maxLuminance))
        {
            return color;
        }

        var target = lum > maxLuminance ? maxLuminance : Math.Min(minLuminance, maxLuminance);
        var darken = lum > target;
        var (h, s, l) = color.ToHsl();
        double lo = darken ? 0 : l, hi = darken ? l : 1;

        // Luminance grows with HSL lightness: binary search for the lightness closest to the target on the safe side.
        var result = color;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            var candidate = Rgb.FromHsl(h, s, mid);
            var ok = darken ? candidate.Luminance <= target : candidate.Luminance >= target;
            if (ok)
            {
                result = candidate;
                if (darken)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            else if (darken)
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        return result;
    }
}
