// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Lumen.Core.Theming;

namespace Lumen.App.Converters;

/// <summary>The calmer version of a color used around art (<see cref="AccentPicker.Soften"/>), same rule as the Library glow.</summary>
public sealed class SoftenColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Color color)
        {
            return Colors.Transparent;
        }

        var soft = AccentPicker.Soften(new Rgb(color.R, color.G, color.B));
        return Color.FromArgb(color.A, soft.R, soft.G, soft.B);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
