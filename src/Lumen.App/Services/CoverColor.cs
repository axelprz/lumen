// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lumen.Core.Theming;

namespace Lumen.App.Services;

/// <summary>
/// Picks the accent color of an image (<see cref="AccentPicker"/>): the dominant vivid color, adjusted so text on it
/// keeps its contrast. Images are decoded tiny. Results are remembered per path, so a tile only pays once.
/// </summary>
public static class CoverColor
{
    private const int SampleWidth = 64;
    private static readonly ConcurrentDictionary<string, Rgb?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Lumen's default accent, used when an image has no usable color (white, grey, missing or broken).</summary>
    public static readonly Color DefaultColor = Color.FromRgb(0x4C, 0x8D, 0xFF);

    private static readonly SolidColorBrush _defaultBrush = Frozen(DefaultColor);

    /// <summary>A frozen brush with the default accent.</summary>
    public static Brush DefaultBrush => _defaultBrush;

    /// <summary>The calmer color used for glow and ring around art: the accent, softened (<see cref="AccentPicker.Soften"/>).</summary>
    public static Rgb? PickGlow(string path) => Pick(path) is { } accent ? AccentPicker.Soften(accent) : null;

    /// <summary>The accent of the image at <paramref name="path"/>, or null. Safe to call off the UI thread; never throws for a bad file.</summary>
    public static Rgb? Pick(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // The file's timestamp is part of the key: replacing an artwork override at the same path gives a new color.
        var key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}";
        if (_cache.TryGetValue(key, out var known))
        {
            return known;
        }

        var picked = Sample(path);
        _cache[key] = picked;
        return picked;
    }

    /// <summary>A frozen brush for <paramref name="color"/>.</summary>
    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Rgb? Sample(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = SampleWidth;
            image.EndInit();
            image.Freeze();

            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var stride = bgra.PixelWidth * 4;
            var pixels = new byte[stride * bgra.PixelHeight];
            bgra.CopyPixels(pixels, stride, 0);

            return AccentPicker.Pick(pixels) is { } picked ? AccentPicker.Fit(picked, AccentPicker.DarkBackground) : null;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null; // a broken image just keeps the default accent
        }
    }
}
