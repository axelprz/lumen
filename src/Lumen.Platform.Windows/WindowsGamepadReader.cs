// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Shell;
using Windows.Gaming.Input;
using WinGamepad = Windows.Gaming.Input.Gamepad;
using WinButtons = Windows.Gaming.Input.GamepadButtons;

namespace Lumen.Platform.Windows;

/// <summary>
/// Gamepads through Windows.Gaming.Input (Xbox controllers and anything Windows treats as a gamepad). Only reads;
/// never vibrates. Windows fills its gamepad list a moment after start-up, so connections are tracked from the
/// added/removed events. Several gamepads are merged: any of them can drive Lumen.
/// </summary>
public sealed class WindowsGamepadReader : IGamepadReader, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<WinGamepad> _pads = [];

    public WindowsGamepadReader()
    {
        WinGamepad.GamepadAdded += OnAdded;
        WinGamepad.GamepadRemoved += OnRemoved;
        foreach (var pad in WinGamepad.Gamepads)
        {
            OnAdded(null, pad);
        }
    }

    public event EventHandler? ConnectionChanged;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _pads.Count > 0;
            }
        }
    }

    public GamepadState Read()
    {
        WinGamepad[] pads;
        lock (_lock)
        {
            pads = [.. _pads];
        }

        var buttons = Core.Shell.GamepadButtons.None;
        double x = 0, y = 0;
        foreach (var pad in pads)
        {
            var reading = pad.GetCurrentReading();
            buttons |= Map(reading.Buttons);
            if (Math.Abs(reading.LeftThumbstickX) + Math.Abs(reading.LeftThumbstickY) > Math.Abs(x) + Math.Abs(y))
            {
                (x, y) = (reading.LeftThumbstickX, reading.LeftThumbstickY);
            }
        }

        return new GamepadState(buttons, x, y);
    }

    public void Dispose()
    {
        WinGamepad.GamepadAdded -= OnAdded;
        WinGamepad.GamepadRemoved -= OnRemoved;
    }

    private static Core.Shell.GamepadButtons Map(WinButtons b)
    {
        var result = Core.Shell.GamepadButtons.None;
        if (b.HasFlag(WinButtons.DPadUp))
        {
            result |= Core.Shell.GamepadButtons.DPadUp;
        }

        if (b.HasFlag(WinButtons.DPadDown))
        {
            result |= Core.Shell.GamepadButtons.DPadDown;
        }

        if (b.HasFlag(WinButtons.DPadLeft))
        {
            result |= Core.Shell.GamepadButtons.DPadLeft;
        }

        if (b.HasFlag(WinButtons.DPadRight))
        {
            result |= Core.Shell.GamepadButtons.DPadRight;
        }

        if (b.HasFlag(WinButtons.A))
        {
            result |= Core.Shell.GamepadButtons.A;
        }

        if (b.HasFlag(WinButtons.B))
        {
            result |= Core.Shell.GamepadButtons.B;
        }

        if (b.HasFlag(WinButtons.Y))
        {
            result |= Core.Shell.GamepadButtons.Y;
        }

        if (b.HasFlag(WinButtons.LeftShoulder))
        {
            result |= Core.Shell.GamepadButtons.LeftShoulder;
        }

        if (b.HasFlag(WinButtons.RightShoulder))
        {
            result |= Core.Shell.GamepadButtons.RightShoulder;
        }

        return result;
    }

    private void OnAdded(object? sender, WinGamepad pad)
    {
        lock (_lock)
        {
            if (_pads.Contains(pad))
            {
                return;
            }

            _pads.Add(pad);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoved(object? sender, WinGamepad pad)
    {
        lock (_lock)
        {
            _pads.Remove(pad);
        }

        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
