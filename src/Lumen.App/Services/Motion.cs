// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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

        // Hidden now, animated once the new page has been built and laid out: a heavy page (the Library) would
        // otherwise eat the whole animation while it loads, and appear all at once.
        page.Opacity = 0;
        slide.Y = 12;
        var version = ++_pageVersion;
        _ = page.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (version != _pageVersion)
            {
                return; // another page change took over
            }

            To(page, UIElement.OpacityProperty, 1, Normal);
            To(slide, TranslateTransform.YProperty, 0, Normal);
        });
    }

    private static int _pageVersion;

    /// <summary>Gap between one block and the next in <see cref="Stagger"/>.</summary>
    private static readonly TimeSpan StaggerStep = TimeSpan.FromMilliseconds(50);

    private const int StaggerMaxBlocks = 5;
    private const double StaggerSlide = 14;

    /// <summary>
    /// A page's blocks arriving one after the other: each visible child of <paramref name="panel"/> (the first few) fades
    /// in while sliding up, 50 ms apart. A handful of big blocks, never the items of a long list. Nothing happens when animations are off.
    /// </summary>
    public static void Stagger(Panel panel)
    {
        if (!Enabled)
        {
            return;
        }

        // The blocks are hidden at once and start moving when the page has finished loading (see PageIn).
        var blocks = new List<(UIElement Child, TranslateTransform Slide)>();
        foreach (UIElement child in panel.Children)
        {
            if (child.Visibility != Visibility.Visible || child.RenderTransform != Transform.Identity)
            {
                continue;
            }

            if (blocks.Count >= StaggerMaxBlocks)
            {
                break;
            }

            var slide = new TranslateTransform(0, StaggerSlide);
            child.RenderTransform = slide;
            child.Opacity = 0;
            blocks.Add((child, slide));
        }

        _ = panel.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                EnterIn(blocks[i].Child, blocks[i].Slide, TimeSpan.FromTicks(StaggerStep.Ticks * i));
            }
        });
    }

    /// <summary>A block whose content just changed (the Home hero): its new content fades in. Nothing happens when animations are off.</summary>
    public static void FadeIn(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Enabled)
        {
            element.Opacity = 1;
            return;
        }

        element.Opacity = 0;
        To(element, UIElement.OpacityProperty, 1, Normal);
    }

    private static void EnterIn(UIElement child, TranslateTransform slide, TimeSpan delay)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(1, Normal) { BeginTime = delay, EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            // Back to a plain element: no leftover transform or animation on a block that may be scrolled a lot.
            child.BeginAnimation(UIElement.OpacityProperty, null);
            child.Opacity = 1;
            child.RenderTransform = Transform.Identity;
        };
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Normal) { BeginTime = delay, EasingFunction = ease });
        child.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>
    /// A panel that covers the page (a game's detail, the routine editor, Discover apps) appearing: fades in while
    /// sliding up a little, once it has been laid out. Nothing happens when animations are off.
    /// </summary>
    public static void PanelIn(UIElement panel, Duration? duration = null)
    {
        if (!Enabled)
        {
            return;
        }

        if (panel.RenderTransform is not TranslateTransform { IsFrozen: false } slide)
        {
            slide = new TranslateTransform();
            panel.RenderTransform = slide;
        }

        slide.BeginAnimation(TranslateTransform.YProperty, null);
        panel.BeginAnimation(UIElement.OpacityProperty, null);
        panel.Opacity = 0;
        slide.Y = 16;
        _ = panel.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            To(panel, UIElement.OpacityProperty, 1, duration ?? Normal);
            To(slide, TranslateTransform.YProperty, 0, duration ?? Normal);
        });
    }

    /// <summary>Set to true on a covering panel: every time it becomes visible it comes in with <see cref="PanelIn"/>.</summary>
    public static readonly DependencyProperty ShowInProperty =
        DependencyProperty.RegisterAttached("ShowIn", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnShowInChanged));

    public static bool GetShowIn(DependencyObject element) => (bool)element.GetValue(ShowInProperty);

    public static void SetShowIn(DependencyObject element, bool value) => element.SetValue(ShowInProperty, value);

    private static void OnShowInChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnShowInVisibleChanged;
        if ((bool)e.NewValue)
        {
            element.IsVisibleChanged += OnShowInVisibleChanged;
        }
    }

    private static void OnShowInVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is UIElement element)
        {
            PanelIn(element);
        }
    }

    /// <summary>One way of the ambient "breathing" (a full cycle is twice this). Ambience, not feedback, so slower than the interaction rule.</summary>
    public static readonly Duration Ambient = new(TimeSpan.FromSeconds(4));

    /// <summary>Lowest opacity an ambient light fades to while breathing (it goes back up to 1).</summary>
    private const double AmbientLow = 0.55;

    /// <summary>Ambient lights only need a few frames a second: keeps the idle cost near zero.</summary>
    private const int AmbientFrameRate = 20;

    /// <summary>
    /// Set on an ambient light (the window's background glows) to make it breathe slowly; the value is its phase offset
    /// in seconds, a negative value turns it off. It runs only while the element is visible and its window is not minimized,
    /// and stays still at full strength when Windows' "Animation effects" is off.
    /// </summary>
    public static readonly DependencyProperty BreatheProperty =
        DependencyProperty.RegisterAttached("Breathe", typeof(double), typeof(Motion), new PropertyMetadata(-1.0, OnBreatheChanged));

    public static double GetBreathe(DependencyObject element) => (double)element.GetValue(BreatheProperty);

    public static void SetBreathe(DependencyObject element, double value) => element.SetValue(BreatheProperty, value);

    private static readonly DependencyProperty BreathingProperty =
        DependencyProperty.RegisterAttached("Breathing", typeof(bool), typeof(Motion), new PropertyMetadata(false));

    private static void OnBreatheChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnBreatheVisibleChanged;
        element.Loaded -= OnBreatheLoaded;
        if ((double)e.NewValue >= 0)
        {
            element.IsVisibleChanged += OnBreatheVisibleChanged;
            element.Loaded += OnBreatheLoaded;
        }

        UpdateBreath(element);
    }

    private static void OnBreatheLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (Window.GetWindow(element) is { } window)
        {
            // Minimized windows draw nothing: stop the clock instead of ticking for nobody.
            window.StateChanged -= OnBreatheWindowStateChanged;
            window.StateChanged += OnBreatheWindowStateChanged;
        }

        UpdateBreath(element);
    }

    private static void OnBreatheVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateBreath((FrameworkElement)sender);

    private static void OnBreatheWindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            foreach (var element in Breathers(window))
            {
                UpdateBreath(element);
            }
        }
    }

    private static IEnumerable<FrameworkElement> Breathers(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && GetBreathe(element) >= 0)
            {
                yield return element;
            }

            // Ambient lights sit near the top of the window: a shallow walk is enough.
            if (child is Panel or Decorator or ContentPresenter)
            {
                foreach (var nested in Breathers(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static void UpdateBreath(FrameworkElement element)
    {
        var offset = GetBreathe(element);
        var minimized = Window.GetWindow(element)?.WindowState == WindowState.Minimized;
        var run = offset >= 0 && Enabled && element.IsVisible && !minimized;
        if (run == (bool)element.GetValue(BreathingProperty))
        {
            return;
        }

        element.SetValue(BreathingProperty, run);
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!run)
        {
            return;
        }

        var breath = new DoubleAnimation(1, AmbientLow, Ambient)
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            BeginTime = TimeSpan.FromSeconds(-offset), // start part-way through, so two lights are out of step
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(breath, AmbientFrameRate);
        element.BeginAnimation(UIElement.OpacityProperty, breath);
    }

    /// <summary>
    /// Bind to the "is open" state of a covering panel (a game's detail, Discover apps, the routine editor) instead of
    /// binding its Visibility; start it Collapsed in XAML. Opening shows it and plays <see cref="PanelIn"/>. Closing
    /// hides it at once (focus, gamepad layers and everything else behave as before) and leaves a still picture of it
    /// that fades and slides away (Fast). Nothing animates when "Animation effects" is off.
    /// </summary>
    public static readonly DependencyProperty ShownProperty =
        DependencyProperty.RegisterAttached("Shown", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnShownChanged));

    public static bool GetShown(DependencyObject element) => (bool)element.GetValue(ShownProperty);

    public static void SetShown(DependencyObject element, bool value) => element.SetValue(ShownProperty, value);

    private const double OutSlide = 12;

    private static void OnShownChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.Visibility = Visibility.Visible;
            PanelIn(element);
            return;
        }

        if (Enabled && element.IsVisible && element.ActualWidth >= 1 && element.ActualHeight >= 1 && VisualTreeHelper.GetParent(element) is Panel parent)
        {
            LeaveGhost(element, parent);
        }

        element.Visibility = Visibility.Collapsed;
    }

    // A still picture of the closing panel, in the same place, that fades out and is then removed. It can't be
    // focused or clicked. Rendered at 96 DPI: it only lives for a fraction of a second.
    private static void LeaveGhost(FrameworkElement element, Panel parent)
    {
        var width = element.ActualWidth;
        var height = element.ActualHeight;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var brush = new VisualBrush(element)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, width, height),
                Stretch = Stretch.Fill,
            };
            context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();

        var slide = new TranslateTransform();
        var ghost = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            Width = width,
            Height = height,
            Margin = element.Margin,
            HorizontalAlignment = element.HorizontalAlignment == HorizontalAlignment.Stretch ? HorizontalAlignment.Left : element.HorizontalAlignment,
            VerticalAlignment = element.VerticalAlignment == VerticalAlignment.Stretch ? VerticalAlignment.Top : element.VerticalAlignment,
            IsHitTestVisible = false,
            Focusable = false,
            RenderTransform = slide,
        };
        Grid.SetRow(ghost, Grid.GetRow(element));
        Grid.SetColumn(ghost, Grid.GetColumn(element));
        Grid.SetRowSpan(ghost, Grid.GetRowSpan(element));
        Grid.SetColumnSpan(ghost, Grid.GetColumnSpan(element));
        Panel.SetZIndex(ghost, Panel.GetZIndex(element));
        parent.Children.Add(ghost);

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, Fast) { EasingFunction = ease };
        fade.Completed += (_, _) => parent.Children.Remove(ghost);
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(OutSlide, Fast) { EasingFunction = ease });
        ghost.BeginAnimation(UIElement.OpacityProperty, fade);
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
