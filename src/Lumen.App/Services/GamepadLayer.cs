// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;

namespace Lumen.App.Services;

/// <summary>
/// Marks a panel that covers the page while visible (game detail, Discover apps, the welcome wizard). While one is
/// visible the gamepad only moves inside the highest one, so focus can't wander to controls hidden underneath.
/// </summary>
public static class GamepadLayer
{
    /// <summary>0 = not a layer; higher numbers cover lower ones.</summary>
    public static readonly DependencyProperty PriorityProperty = DependencyProperty.RegisterAttached(
        "Priority", typeof(int), typeof(GamepadLayer), new PropertyMetadata(0));

    public static int GetPriority(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)element.GetValue(PriorityProperty);
    }

    public static void SetPriority(DependencyObject element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PriorityProperty, value);
    }
}
