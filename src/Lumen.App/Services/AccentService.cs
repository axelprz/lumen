// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Lumen.Core.Theming;

namespace Lumen.App.Services;

/// <summary>
/// Dynamic accent (PLAN-v1 §4.3): takes the accent color from a game's artwork and fades the app's accent brush to
/// it, keeping text contrast safe (<see cref="AccentPicker"/>). Images are decoded small, off the UI thread.
/// Call from the UI thread.
/// </summary>
public sealed class AccentService
{
    private const string BrushKey = "Lumen.Brush.Accent";
    private const string ColorKey = "Lumen.Color.Accent";
    private const int SampleWidth = 64;
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(450));

    private readonly Color _default;
    private readonly SolidColorBrush _brush;
    private int _version;

    public AccentService()
    {
        var resources = Application.Current.Resources;
        _default = resources[ColorKey] is Color color ? color : Color.FromRgb(0x4C, 0x8D, 0xFF);

        // The theme's brush is frozen (read-only); swap in one copy that can be animated. Everything references it
        // with DynamicResource, so this replaces it everywhere.
        _brush = new SolidColorBrush(_default);
        resources[BrushKey] = _brush;
        Current = _default;
    }

    /// <summary>The accent currently applied (or being faded to).</summary>
    public Color Current { get; private set; }

    /// <summary>Fades to the accent of the image at <paramref name="imagePath"/>, or back to the default when it has none.</summary>
    public async Task ApplyFromImageAsync(string? imagePath)
    {
        var version = ++_version;
        var picked = imagePath is null ? null : await Task.Run(() => PickFrom(imagePath)).ConfigureAwait(true);
        if (version != _version)
        {
            return; // a newer request won (the user moved on to another game)
        }

        FadeTo(picked is { } rgb ? Color.FromRgb(rgb.R, rgb.G, rgb.B) : _default);
    }

    /// <summary>Back to Lumen's default accent.</summary>
    public void Reset()
    {
        _version++;
        FadeTo(_default);
    }

    private void FadeTo(Color color)
    {
        if (color == Current)
        {
            return;
        }

        Current = color;
        var animation = new ColorAnimation(color, FadeDuration) { EasingFunction = new QuadraticEase() };
        _brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static Rgb? PickFrom(string path)
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
