// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Lumen.App.Services;

/// <summary>
/// Lumen's motion rules (PLAN-v1 §4.9). Interactions last 250 ms at most; only a hero crossfade may be slower.
/// Every animation goes through here, so Windows' "Animation effects" switch (Settings → Accessibility → Visual
/// effects) turns all of it off: values then jump to their end state at once. Use from the UI thread.
/// </summary>
public static class Motion
{
    /// <summary>Hover and focus feedback.</summary>
    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(140));

    /// <summary>Page changes and panels. Never above 250 ms.</summary>
    public static readonly Duration Normal = new(TimeSpan.FromMilliseconds(220));

    /// <summary>Hero background and accent crossfades: slow on purpose, they are ambience, not feedback.</summary>
    public static readonly Duration Hero = new(TimeSpan.FromMilliseconds(450));

    /// <summary>The "scale up a little" of a pointed-at or focused tile.</summary>
    public const double TileScale = 1.04;

    /// <summary>False when the user turned off Windows' "Animation effects".</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    /// <summary>Animates <paramref name="property"/> to <paramref name="to"/>, or sets it straight away when animations are off.</summary>
    public static void To(IAnimatable target, DependencyProperty property, double to, Duration duration)
    {
        if (!Enabled)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, to);
            return;
        }

        target.BeginAnimation(property, new DoubleAnimation(to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>A page arriving: fades in while sliding up a few pixels. Nothing happens when animations are off.</summary>
    public static void PageIn(FrameworkElement page)
    {
        if (page.RenderTransform is not TranslateTransform { IsFrozen: false } slide)
        {
            slide = new TranslateTransform();
            page.RenderTransform = slide;
        }

        slide.BeginAnimation(TranslateTransform.YProperty, null);
        page.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Enabled)
        {
            slide.Y = 0;
            page.Opacity = 1;
            return;
        }

        page.Opacity = 0;
        slide.Y = 12;
        To(page, UIElement.OpacityProperty, 1, Normal);
        To(slide, TranslateTransform.YProperty, 0, Normal);
    }

    /// <summary>Hover/focus scale for an element (say, a tile's cover). 1 or less means "off".</summary>
    public static readonly DependencyProperty HoverScaleProperty =
        DependencyProperty.RegisterAttached("HoverScale", typeof(double), typeof(Motion), new PropertyMetadata(1.0, OnHoverScaleChanged));

    public static double GetHoverScale(DependencyObject element) => (double)element.GetValue(HoverScaleProperty);

    public static void SetHoverScale(DependencyObject element, double value) => element.SetValue(HoverScaleProperty, value);

    private static void OnHoverScaleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        element.IsKeyboardFocusWithinChanged -= OnFocusChanged;
        element.MouseEnter -= OnPointerChanged;
        element.MouseLeave -= OnPointerChanged;
        if ((double)e.NewValue <= 1)
        {
            return;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.MouseEnter += OnPointerChanged;
        element.MouseLeave += OnPointerChanged;
        element.IsKeyboardFocusWithinChanged += OnFocusChanged;
    }

    private static void OnPointerChanged(object sender, MouseEventArgs e) => UpdateScale(sender);

    private static void OnFocusChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateScale(sender);

    private static void UpdateScale(object sender)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        if (element.RenderTransform is not ScaleTransform { IsFrozen: false } scale)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransform = scale;
        }

        var raised = element.IsMouseOver || element.IsKeyboardFocusWithin;
        var target = raised ? GetHoverScale(element) : 1;
        To(scale, ScaleTransform.ScaleXProperty, target, Fast);
        To(scale, ScaleTransform.ScaleYProperty, target, Fast);
    }
}
