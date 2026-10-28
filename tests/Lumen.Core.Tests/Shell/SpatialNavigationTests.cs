// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Shell;

namespace Lumen.Core.Tests.Shell;

public sealed class SpatialNavigationTests
{
    // A Home-like screen: sidebar on the left, a Play button, a row of tiles, then a second row lower down.
    private static readonly NavRect SidebarHome = new(0, 100, 230, 40);
    private static readonly NavRect SidebarLibrary = new(0, 140, 230, 40);
    private static readonly NavRect Play = new(280, 300, 120, 40);
    private static readonly NavRect Tile1 = new(280, 400, 160, 240);
    private static readonly NavRect Tile2 = new(460, 400, 160, 240);
    private static readonly NavRect Tile3 = new(640, 400, 160, 240);
    private static readonly NavRect Fav1 = new(280, 700, 160, 240);
    private static readonly NavRect Fav2 = new(460, 700, 160, 240);

    private static readonly NavRect[] Screen = [SidebarHome, SidebarLibrary, Play, Tile1, Tile2, Tile3, Fav1, Fav2];

    [Theory]
    [InlineData(2, GamepadCommand.Down, 3)] // Play → first tile below it
    [InlineData(3, GamepadCommand.Right, 4)] // along the row
    [InlineData(4, GamepadCommand.Down, 7)] // tile 2 → the favorite right below, not the first one
    [InlineData(7, GamepadCommand.Up, 4)]
    [InlineData(3, GamepadCommand.Left, 1)] // out of the page into the sidebar, nearest item vertically
    [InlineData(0, GamepadCommand.Down, 1)] // within the sidebar
    [InlineData(1, GamepadCommand.Right, 2)] // sidebar → the page's nearest control
    public void MovesToTheNearestControlThatWay(int from, GamepadCommand direction, int expected) =>
        SpatialNavigation.FindNext(Screen[from], Screen, direction).ShouldBe(expected);

    [Theory]
    [InlineData(5, GamepadCommand.Right)] // last tile in the row
    [InlineData(0, GamepadCommand.Up)] // top of the sidebar
    [InlineData(6, GamepadCommand.Down)] // bottom row
    public void NothingThatWay_IsNull(int from, GamepadCommand direction) =>
        SpatialNavigation.FindNext(Screen[from], Screen, direction).ShouldBeNull();

    [Fact]
    public void SameRow_BeatsACloserControlFarOffToTheSide()
    {
        var from = new NavRect(0, 0, 100, 100);
        var sameRowFar = new NavRect(400, 0, 100, 100);
        var diagonalNear = new NavRect(120, 300, 100, 100);

        SpatialNavigation.FindNext(from, [sameRowFar, diagonalNear], GamepadCommand.Right).ShouldBe(0);
    }

    [Fact]
    public void ActionNames_AreNotDirections() =>
        SpatialNavigation.FindNext(Play, Screen, GamepadCommand.Activate).ShouldBeNull();

    [Fact]
    public void Start_IsTopMostThenLeftMost()
    {
        var right = new NavRect(500, 10, 100, 40);
        var left = new NavRect(100, 20, 100, 40); // same line, slightly lower
        var below = new NavRect(0, 200, 100, 40);

        SpatialNavigation.FindStart([right, below, left]).ShouldBe(2);
    }

    [Fact]
    public void Start_OfNothing_IsNull() => SpatialNavigation.FindStart([]).ShouldBeNull();
}
