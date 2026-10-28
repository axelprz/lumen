// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Lumen.App.ViewModels;
using Lumen.Core.Shell;
using Microsoft.Extensions.Logging;

namespace Lumen.App.Services;

/// <summary>
/// Gamepad navigation (PLAN-v1 §4.3), only while a Lumen window is in front. The D-pad/left stick, A and B act as the
/// arrow keys, Enter (Space for checkboxes, opening for dropdowns) and Esc, delivered through WPF's own input
/// pipeline to Lumen's focused element only. So every screen that works with the keyboard works with a gamepad, with
/// the same focus highlight. Y opens quick search; LB/RB switch the sidebar section. Polls ~60 times a second, and
/// only while a gamepad is connected and Lumen is the active app.
/// </summary>
public sealed partial class GamepadService(IGamepadReader reader, ShellViewModel shell, ILogger<GamepadService> logger) : IDisposable
{
    private readonly GamepadNavigator _navigator = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private DispatcherTimer? _timer;
    private Window? _main;
    private Window? _search;
    private Action? _openSearch;
    private Action? _focusPage;

    /// <param name="focusPage">Moves focus into the current page (after LB/RB switched it).</param>
    public void Attach(Window main, Window search, Action openSearch, Action focusPage)
    {
        _main = main;
        _search = search;
        _openSearch = openSearch;
        _focusPage = focusPage;
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
                Handle(command, window);
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

    private void Handle(GamepadCommand command, Window window)
    {
        switch (command)
        {
            case GamepadCommand.Search:
                if (window != _search)
                {
                    _openSearch?.Invoke();
                }

                return;

            case GamepadCommand.PreviousSection or GamepadCommand.NextSection:
                if (window == _main && shell.SelectAdjacentSection(command == GamepadCommand.NextSection ? 1 : -1))
                {
                    _focusPage?.Invoke();
                }

                return;
        }

        // Nothing focused in this window yet: the first input just puts focus somewhere visible.
        if (Keyboard.FocusedElement is not DependencyObject focused || Window.GetWindow(focused) != window)
        {
            if (command != GamepadCommand.Back)
            {
                window.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                return;
            }

            focused = window;
        }

        switch (command)
        {
            case GamepadCommand.Up:
                SendKey(window, Key.Up);
                break;
            case GamepadCommand.Down:
                SendKey(window, Key.Down);
                break;
            case GamepadCommand.Left:
                SendKey(window, Key.Left);
                break;
            case GamepadCommand.Right:
                SendKey(window, Key.Right);
                break;
            case GamepadCommand.Back:
                SendKey(window, Key.Escape);
                break;
            case GamepadCommand.Activate when focused is ComboBox { IsDropDownOpen: false } combo:
                combo.IsDropDownOpen = true;
                break;
            case GamepadCommand.Activate when focused is ToggleButton:
                SendKey(window, Key.Space); // checkboxes toggle with Space, not Enter
                break;
            case GamepadCommand.Activate:
                SendKey(window, Key.Enter);
                break;
        }
    }

    // A key press through WPF's input pipeline (not Windows' SendInput): it can only reach Lumen's focused element,
    // and WPF treats it like the keyboard (focus moves, focus highlight, Enter/Esc bindings).
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Gamepad: {Polling}")]
    private partial void LogPollingCore(string polling);

    private void LogPolling(bool on) => LogPollingCore(on ? "listening" : "paused");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gamepad: could not read the controller")]
    private partial void LogReadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gamepad: {Command} failed")]
    private partial void LogCommandFailed(Exception exception, string command);
}
