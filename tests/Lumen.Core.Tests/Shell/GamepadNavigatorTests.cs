// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Shell;

namespace Lumen.Core.Tests.Shell;

public sealed class GamepadNavigatorTests
{
    private readonly GamepadNavigator _nav = new();

    [Fact]
    public void Buttons_FireOncePerPress()
    {
        _nav.Update(new(GamepadButtons.A), Ms(0)).ShouldBe([GamepadCommand.Activate]);
        _nav.Update(new(GamepadButtons.A), Ms(500)).ShouldBeEmpty(); // held
        _nav.Update(GamepadState.Released, Ms(600)).ShouldBeEmpty();
        _nav.Update(new(GamepadButtons.A), Ms(700)).ShouldBe([GamepadCommand.Activate]);
    }

    [Theory]
    [InlineData(GamepadButtons.B, GamepadCommand.Back)]
    [InlineData(GamepadButtons.Y, GamepadCommand.Search)]
    [InlineData(GamepadButtons.LeftShoulder, GamepadCommand.PreviousSection)]
    [InlineData(GamepadButtons.RightShoulder, GamepadCommand.NextSection)]
    public void ButtonMeanings(GamepadButtons button, GamepadCommand command) =>
        _nav.Update(new(button), Ms(0)).ShouldBe([command]);

    [Fact]
    public void HeldDirection_RepeatsAfterADelay()
    {
        var down = new GamepadState(GamepadButtons.DPadDown);

        _nav.Update(down, Ms(0)).ShouldBe([GamepadCommand.Down]);
        _nav.Update(down, Ms(300)).ShouldBeEmpty();
        _nav.Update(down, Ms(400)).ShouldBe([GamepadCommand.Down]);
        _nav.Update(down, Ms(450)).ShouldBeEmpty();
        _nav.Update(down, Ms(510)).ShouldBe([GamepadCommand.Down]);
    }

    [Theory]
    [InlineData(0.0, 0.9, GamepadCommand.Up)]
    [InlineData(0.0, -0.9, GamepadCommand.Down)]
    [InlineData(-0.9, 0.2, GamepadCommand.Left)]
    [InlineData(0.8, -0.3, GamepadCommand.Right)]
    public void Stick_UsesItsMainAxis(double x, double y, GamepadCommand expected) =>
        _nav.Update(new(GamepadButtons.None, x, y), Ms(0)).ShouldBe([expected]);

    [Fact]
    public void SmallStickMoves_AreIgnored() =>
        _nav.Update(new(GamepadButtons.None, 0.3, -0.4), Ms(0)).ShouldBeEmpty();

    [Fact]
    public void ChangingDirection_FiresTheNewOneRightAway()
    {
        _nav.Update(new(GamepadButtons.DPadDown), Ms(0));

        _nav.Update(new(GamepadButtons.DPadRight), Ms(50)).ShouldBe([GamepadCommand.Right]);
    }

    [Fact]
    public void DirectionAndButton_InTheSameReading()
    {
        _nav.Update(new(GamepadButtons.DPadUp | GamepadButtons.A), Ms(0)).ShouldBe([GamepadCommand.Up, GamepadCommand.Activate]);
    }

    [Fact]
    public void Reset_IgnoresWhatWasAlreadyHeld()
    {
        var held = new GamepadState(GamepadButtons.A | GamepadButtons.DPadDown);
        _nav.Reset(held);

        _nav.Update(held, Ms(0)).ShouldBeEmpty();
        _nav.Update(held, Ms(5000)).ShouldBeEmpty(); // no repeat from a press made before
        _nav.Update(GamepadState.Released, Ms(5100)).ShouldBeEmpty();
        _nav.Update(new(GamepadButtons.DPadDown), Ms(5200)).ShouldBe([GamepadCommand.Down]);
    }

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
}
