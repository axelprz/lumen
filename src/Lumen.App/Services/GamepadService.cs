// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lumen.App.ViewModels;
using Lumen.Core.Shell;
using Microsoft.Extensions.Logging;

namespace Lumen.App.Services;

/// <summary>
/// Gamepad navigation (PLAN-v1 §4.3), only while a Lumen window is in front.
/// <list type="bullet">
/// <item>D-pad/left stick move focus spatially (<see cref="SpatialNavigation"/>): to the nearest control that way,
/// across rows, panels and the sidebar, scrolling when the next one isn't on screen yet. Only inside the top
/// <see cref="GamepadLayer"/> when one covers the page.</item>
/// <item>A presses the focused control (Enter; Space on checkboxes; opens dropdowns; selects sidebar entries).</item>
/// <item>B is Esc (closes the detail, inline editors, dropdowns, quick search).</item>
/// <item>Y opens quick search; LB/RB switch the sidebar section.</item>
/// </list>
/// Keys go through WPF's own input pipeline (never Windows' SendInput), so they can only reach Lumen. When a layer
/// closes, focus returns to where it was on the page. Polls ~60 times a second, only while a gamepad is connected and
/// Lumen is the active app.
/// </summary>
public sealed partial class GamepadService(IGamepadReader reader, ShellViewModel shell, ILogger<GamepadService> logger) : IDisposable
{
    private readonly GamepadNavigator _navigator = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherTimer? _timer;
    private Window? _main;
    private Window? _search;
    private Action? _openSearch;
    private Func<UIElement>? _page;
    private WeakReference<UIElement>? _pageFocus;

    /// <param name="page">The main window's page area (where focus starts, rather than the sidebar).</param>
    public void Attach(Window main, Window search, Action openSearch, Func<UIElement> page)
    {
        _main = main;
        _search = search;
        _openSearch = openSearch;
        _page = page;
        _timer = new DispatcherTimer(DispatcherPriority.Input, main.Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();

        foreach (var window in new[] { main, search })
        {
            window.Activated += (_, _) => UpdatePolling();
            window.Deactivated += (_, _) => UpdatePolling();
        }

        reader.ConnectionChanged += (_, _) => main.Dispatcher.BeginInvoke(UpdatePolling);
        UpdatePolling();
    }

    public void Dispose()
    {
        _timer?.Stop();
        (reader as IDisposable)?.Dispose();
    }

    private Window? ActiveWindow => _search is { IsActive: true } ? _search : _main is { IsActive: true } ? _main : null;

    private void UpdatePolling()
    {
        if (_timer is null)
        {
            return;
        }

        var poll = reader.IsConnected && ActiveWindow is not null;
        if (poll && !_timer.IsEnabled)
        {
            _navigator.Reset(SafeRead()); // a button held while switching to Lumen doesn't count
            _timer.Start();
            LogPolling(true);
        }
        else if (!poll && _timer.IsEnabled)
        {
            _timer.Stop();
            LogPolling(false);
        }
    }

    private void Tick()
    {
        if (ActiveWindow is not { } window)
        {
            UpdatePolling();
            return;
        }

        foreach (var command in _navigator.Update(SafeRead(), _clock.Elapsed))
        {
            try
            {
                if (window == _search)
                {
                    HandleInQuickSearch(command, window);
                }
                else
                {
                    HandleInMain(command, window);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                LogCommandFailed(ex, command.ToString()); // one odd screen state must not stop the gamepad
            }

            if (ActiveWindow != window)
            {
                break; // e.g. Y opened quick search: the rest of this reading isn't for the old window
            }
        }
    }

    private GamepadState SafeRead()
    {
        try
        {
            return reader.Read();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogReadFailed(ex);
            return GamepadState.Released;
        }
    }

    // The quick search box already handles ↑/↓/Enter/Esc itself.
    private static void HandleInQuickSearch(GamepadCommand command, Window window)
    {
        switch (command)
        {
            case GamepadCommand.Up or GamepadCommand.Left:
                SendKey(window, Key.Up);
                break;
            case GamepadCommand.Down or GamepadCommand.Right:
                SendKey(window, Key.Down);
                break;
            case GamepadCommand.Activate:
                SendKey(window, Key.Enter);
                break;
            case GamepadCommand.Back:
                SendKey(window, Key.Escape);
                break;
        }
    }

    private void HandleInMain(GamepadCommand command, Window window)
    {
        switch (command)
        {
            case GamepadCommand.Search:
                _openSearch?.Invoke();
                return;
            case GamepadCommand.PreviousSection or GamepadCommand.NextSection:
                if (shell.SelectAdjacentSection(command == GamepadCommand.NextSection ? 1 : -1))
                {
                    _pageFocus = null;
                    AfterLayout(window, () => FocusStart(window, Scope(window)));
                }

                return;
        }

        // An open dropdown list works like with the keyboard: arrows choose, Enter picks, Esc closes.
        if (Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true } or ComboBoxItem)
        {
            SendKey(window, command switch
            {
                GamepadCommand.Up => Key.Up,
                GamepadCommand.Down => Key.Down,
                GamepadCommand.Left => Key.Left,
                GamepadCommand.Right => Key.Right,
                GamepadCommand.Activate => Key.Enter,
                _ => Key.Escape,
            });
            return;
        }

        var scope = Scope(window);
        if (FocusedIn(window, scope) is not { } focused)
        {
            // Focus is lost or hidden (a page or panel just changed): the first input only puts it back.
            focused = RestoreFocus(window, scope);
            if (command != GamepadCommand.Back || focused is null)
            {
                return;
            }
        }

        switch (command)
        {
            case GamepadCommand.Up or GamepadCommand.Down or GamepadCommand.Left or GamepadCommand.Right:
                Move(window, scope, focused, command);
                break;
            case GamepadCommand.Back:
                SendKey(window, Key.Escape);
                AfterLayout(window, () => RestoreFocusIfLost(window));
                break;
            case GamepadCommand.Activate:
                Activate(window, focused);
                AfterLayout(window, () => RestoreFocusIfLost(window));
                break;
        }
    }

    private void Move(Window window, UIElement scope, UIElement focused, GamepadCommand direction)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var candidates = Candidates(scope);
            var rects = candidates.Select(c => Bounds(c, window)).ToList();
            if (SpatialNavigation.FindNext(Bounds(focused, window), rects, direction) is { } next)
            {
                Focus(window, candidates[next]);
                return;
            }

            // Nothing that way on screen: scroll the list a bit (virtualized rows appear) and look again.
            if (!Scroll(focused, direction))
            {
                return;
            }

            window.UpdateLayout();
        }
    }

    private static void Activate(Window window, UIElement focused)
    {
        switch (focused)
        {
            case ListBoxItem item:
                item.IsSelected = true; // sidebar entries
                break;
            case ComboBox combo:
                combo.IsDropDownOpen = true;
                break;
            case ToggleButton:
                SendKey(window, Key.Space); // checkboxes toggle with Space, not Enter
                break;
            default:
                SendKey(window, Key.Enter);
                break;
        }
    }

    // The highest visible layer (detail, Discover apps, welcome), else the whole window.
    private static UIElement Scope(Window window)
    {
        UIElement scope = window;
        var best = 0;
        Walk(window, element =>
        {
            var priority = GamepadLayer.GetPriority(element);
            if (priority > best)
            {
                best = priority;
                scope = element;
            }

            return true;
        });
        return scope;
    }

    private UIElement? FocusedIn(Window window, UIElement scope)
    {
        if (Keyboard.FocusedElement is not UIElement { IsVisible: true } focused
            || Window.GetWindow(focused) != window
            || (focused != scope && !scope.IsAncestorOf(focused))
            || focused is Window)
        {
            return null;
        }

        if (scope == window)
        {
            _pageFocus = new WeakReference<UIElement>(focused); // where to come back after a layer closes
        }

        return focused;
    }

    private UIElement? RestoreFocus(Window window, UIElement scope)
    {
        if (scope == window && _pageFocus?.TryGetTarget(out var previous) == true
            && previous.IsVisible && Window.GetWindow(previous) == window)
        {
            Focus(window, previous);
            return previous;
        }

        return FocusStart(window, scope);
    }

    private void RestoreFocusIfLost(Window window)
    {
        if (window.IsActive)
        {
            var scope = Scope(window);
            if (FocusedIn(window, scope) is null)
            {
                RestoreFocus(window, scope);
            }
        }
    }

    // Top-left control of the scope; in the main window the page comes before the sidebar.
    private UIElement? FocusStart(Window window, UIElement scope)
    {
        var candidates = scope == window && _page?.Invoke() is { } page ? Candidates(page) : [];
        if (candidates.Count == 0)
        {
            candidates = Candidates(scope);
        }

        if (SpatialNavigation.FindStart(candidates.Select(c => Bounds(c, window)).ToList()) is not { } start)
        {
            return null;
        }

        Focus(window, candidates[start]);
        return candidates[start];
    }

    private static List<UIElement> Candidates(UIElement root)
    {
        var result = new List<UIElement>();
        Walk(root, element =>
        {
            if (element != root && GamepadLayer.GetPriority(element) > 0)
            {
                return false; // a covering panel that isn't the current scope
            }

            if (element != root && IsNavigable(element))
            {
                result.Add(element);
            }

            return true;
        });
        return result;
    }

    private static bool IsNavigable(UIElement element) =>
        element is { Focusable: true, IsEnabled: true, IsVisible: true }
        && element.RenderSize is { Width: > 0, Height: > 0 }
        && element is not (Window or ScrollViewer or ListBox)
        && (element is not Control control || control.IsTabStop);

    // Visible elements only; visit returns false to skip an element's children.
    private static void Walk(UIElement root, Func<UIElement, bool> visit)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is UIElement element && (!element.IsVisible || !visit(element)))
            {
                continue;
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = count - 1; i >= 0; i--)
            {
                stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }
    }

    private static NavRect Bounds(UIElement element, Window window)
    {
        var rect = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        return new NavRect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static void Focus(Window window, UIElement element)
    {
        // A key event first makes WPF treat this as keyboard use, so the focus highlight shows.
        SendKeyUp(window, Key.F24);
        element.Focus();
        if (element is FrameworkElement fe)
        {
            fe.BringIntoView();
        }
    }

    private static bool Scroll(UIElement from, GamepadCommand direction)
    {
        for (DependencyObject? node = from; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not ScrollViewer viewer)
            {
                continue;
            }

            var (before, canScroll) = direction switch
            {
                GamepadCommand.Down => (viewer.VerticalOffset, viewer.VerticalOffset < viewer.ScrollableHeight),
                GamepadCommand.Up => (viewer.VerticalOffset, viewer.VerticalOffset > 0),
                GamepadCommand.Right => (viewer.HorizontalOffset, viewer.HorizontalOffset < viewer.ScrollableWidth),
                _ => (viewer.HorizontalOffset, viewer.HorizontalOffset > 0),
            };
            if (!canScroll)
            {
                continue; // try an outer scroller
            }

            switch (direction)
            {
                case GamepadCommand.Down:
                    viewer.LineDown();
                    break;
                case GamepadCommand.Up:
                    viewer.LineUp();
                    break;
                case GamepadCommand.Right:
                    viewer.LineRight();
                    break;
                default:
                    viewer.LineLeft();
                    break;
            }

            return true;
        }

        return false;
    }

    private static void AfterLayout(Window window, Action action) =>
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, action);

    // A key press through WPF's input pipeline (not Windows' SendInput): it can only reach Lumen's focused element,
    // and WPF treats it like the keyboard (Enter/Esc bindings, the detail view's Esc, quick search's arrows).
    private static void SendKey(Window window, Key key)
    {
        if (PresentationSource.FromVisual(window) is not { } source)
        {
            return;
        }

        var input = InputManager.Current;
        input.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        input.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
    }

    private static void SendKeyUp(Window window, Key key)
    {
        if (PresentationSource.FromVisual(window) is { } source)
        {
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Gamepad: {Polling}")]
    private partial void LogPollingCore(string polling);

    private void LogPolling(bool on) => LogPollingCore(on ? "listening" : "paused");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gamepad: could not read the controller")]
    private partial void LogReadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gamepad: {Command} failed")]
    private partial void LogCommandFailed(Exception exception, string command);
}
