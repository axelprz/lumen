// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Shell;

/// <summary>The gamepad buttons Lumen uses.</summary>
[Flags]
public enum GamepadButtons
{
    None = 0,
    DPadUp = 1,
    DPadDown = 2,
    DPadLeft = 4,
    DPadRight = 8,
    A = 16,
    B = 32,
    Y = 64,
    LeftShoulder = 128,
    RightShoulder = 256,
}

/// <summary>One reading of the connected gamepads (merged when there are several).</summary>
/// <param name="LeftX">Left stick, -1 (left) to 1 (right).</param>
/// <param name="LeftY">Left stick, -1 (down) to 1 (up).</param>
public readonly record struct GamepadState(GamepadButtons Buttons, double LeftX = 0, double LeftY = 0)
{
    public static GamepadState Released { get; } = new(GamepadButtons.None);
}

/// <summary>What a gamepad input means in Lumen (PLAN-v1 §4.3).</summary>
public enum GamepadCommand
{
    Up,
    Down,
    Left,
    Right,

    /// <summary>A: press the focused thing.</summary>
    Activate,

    /// <summary>B: go back / close.</summary>
    Back,

    /// <summary>Y: open quick search.</summary>
    Search,

    /// <summary>LB: previous sidebar section.</summary>
    PreviousSection,

    /// <summary>RB: next sidebar section.</summary>
    NextSection,
}

/// <summary>Reads the gamepads connected to this PC. Read-only; never vibrates or changes anything.</summary>
public interface IGamepadReader
{
    /// <summary>True while at least one gamepad is connected.</summary>
    bool IsConnected { get; }

    /// <summary>Raised (on any thread) when a gamepad is connected or disconnected.</summary>
    event EventHandler? ConnectionChanged;

    GamepadState Read();
}

/// <summary>
/// Turns gamepad readings into <see cref="GamepadCommand"/>s, like a keyboard would: buttons act when pressed (not
/// while held), and a held direction (D-pad or left stick) repeats after <see cref="RepeatDelay"/> every
/// <see cref="RepeatInterval"/>, so scrolling a long library doesn't need hundreds of presses. Only one direction is
/// active at a time; the stick needs to pass <see cref="StickThreshold"/> on its main axis.
/// </summary>
public sealed class GamepadNavigator
{
    public const double StickThreshold = 0.5;
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);

    private static readonly (GamepadButtons Button, GamepadCommand Command)[] Presses =
    [
        (GamepadButtons.A, GamepadCommand.Activate),
        (GamepadButtons.B, GamepadCommand.Back),
        (GamepadButtons.Y, GamepadCommand.Search),
        (GamepadButtons.LeftShoulder, GamepadCommand.PreviousSection),
        (GamepadButtons.RightShoulder, GamepadCommand.NextSection),
    ];

    private GamepadButtons _previous;
    private GamepadCommand? _direction;
    private TimeSpan _nextRepeat;

    /// <param name="now">A steadily increasing clock (e.g. a stopwatch).</param>
    public IReadOnlyList<GamepadCommand> Update(GamepadState state, TimeSpan now)
    {
        var commands = new List<GamepadCommand>(2);

        var direction = Direction(state);
        if (direction != _direction)
        {
            _direction = direction;
            if (direction is { } d)
            {
                commands.Add(d);
                _nextRepeat = now + RepeatDelay;
            }
        }
        else if (direction is { } held && now >= _nextRepeat)
        {
            commands.Add(held);
            _nextRepeat = now + RepeatInterval;
        }

        var pressed = state.Buttons & ~_previous;
        foreach (var (button, command) in Presses)
        {
            if (pressed.HasFlag(button))
            {
                commands.Add(command);
            }
        }

        _previous = state.Buttons;
        return commands;
    }

    /// <summary>Forgets held buttons (e.g. Lumen lost focus), so nothing fires from an old press.</summary>
    public void Reset(GamepadState current)
    {
        _previous = current.Buttons;
        _direction = Direction(current);
        _nextRepeat = TimeSpan.MaxValue; // a direction held across the reset doesn't repeat until released
    }

    private static GamepadCommand? Direction(GamepadState state)
    {
        var b = state.Buttons;
        if (b.HasFlag(GamepadButtons.DPadUp))
        {
            return GamepadCommand.Up;
        }

        if (b.HasFlag(GamepadButtons.DPadDown))
        {
            return GamepadCommand.Down;
        }

        if (b.HasFlag(GamepadButtons.DPadLeft))
        {
            return GamepadCommand.Left;
        }

        if (b.HasFlag(GamepadButtons.DPadRight))
        {
            return GamepadCommand.Right;
        }

        var (x, y) = (state.LeftX, state.LeftY);
        if (Math.Max(Math.Abs(x), Math.Abs(y)) < StickThreshold)
        {
            return null;
        }

        return Math.Abs(y) >= Math.Abs(x)
            ? y > 0 ? GamepadCommand.Up : GamepadCommand.Down
            : x > 0 ? GamepadCommand.Right : GamepadCommand.Left;
    }
}
