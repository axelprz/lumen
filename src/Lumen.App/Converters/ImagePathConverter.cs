// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Lumen.App.Converters;

/// <summary>
/// Local image path → frozen bitmap decoded at display size (ConverterParameter = width in pixels), so a 600×900
/// cover costs ~200 px of memory, not 600. A broken file yields no image instead of an exception.
/// </summary>
[ValueConversion(typeof(string), typeof(BitmapImage))]
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad; // read now, don't keep the file locked
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (parameter is string width && int.TryParse(width, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pixels))
            {
                image.DecodePixelWidth = pixels;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
