// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Shell;

/// <summary>A control's bounds on screen (window coordinates, Y grows downward).</summary>
public readonly record struct NavRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);
}

/// <summary>
/// Gamepad focus moves (PLAN-v1 §4.3), the way TV and console launchers do it: from the focused control, go to the
/// nearest control that lies entirely in the pressed direction. Distance along the direction counts once, sideways
/// offset counts double, so the pick stays in the same row or column when there is one. Works across rows, panels
/// and the sidebar, unlike keyboard arrow navigation, which stops at list and row edges.
/// </summary>
public static class SpatialNavigation
{
    // Controls that touch or overlap by a couple of pixels still count as being "beyond" the focused one.
    private const double Tolerance = 2;

    /// <returns>The index of the control to focus, or null when nothing lies that way.</returns>
    public static int? FindNext(NavRect from, IReadOnlyList<NavRect> candidates, GamepadCommand direction)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        int? best = null;
        var bestScore = double.MaxValue;
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c == from || Score(from, c, direction) is not { } score || score >= bestScore)
            {
                continue;
            }

            best = i;
            bestScore = score;
        }

        return best;
    }

    /// <summary>Where focus starts: the top-most control, then the left-most among those on that line.</summary>
    public static int? FindStart(IReadOnlyList<NavRect> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        int? best = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (best is not { } b)
            {
                best = i;
                continue;
            }

            var (c, current) = (candidates[i], candidates[b]);
            var sameLine = c.Y < current.Bottom && current.Y < c.Bottom;
            if (sameLine ? c.X < current.X : c.Y < current.Y)
            {
                best = i;
            }
        }

        return best;
    }

    private static double? Score(NavRect from, NavRect c, GamepadCommand direction)
    {
        double along, sideways, misalignment;
        switch (direction)
        {
            case GamepadCommand.Down when c.Y >= from.Bottom - Tolerance:
                along = c.Y - from.Bottom;
                sideways = Gap(from.X, from.Right, c.X, c.Right);
                misalignment = Math.Abs(c.CenterX - from.CenterX);
                break;
            case GamepadCommand.Up when c.Bottom <= from.Y + Tolerance:
                along = from.Y - c.Bottom;
                sideways = Gap(from.X, from.Right, c.X, c.Right);
                misalignment = Math.Abs(c.CenterX - from.CenterX);
                break;
            case GamepadCommand.Right when c.X >= from.Right - Tolerance:
                along = c.X - from.Right;
                sideways = Gap(from.Y, from.Bottom, c.Y, c.Bottom);
                misalignment = Math.Abs(c.CenterY - from.CenterY);
                break;
            case GamepadCommand.Left when c.Right <= from.X + Tolerance:
                along = from.X - c.Right;
                sideways = Gap(from.Y, from.Bottom, c.Y, c.Bottom);
                misalignment = Math.Abs(c.CenterY - from.CenterY);
                break;
            default:
                return null;
        }

        return Math.Max(along, 0) + (2 * sideways) + (0.05 * misalignment);
    }

    // Distance between two ranges on one axis; 0 when they overlap.
    private static double Gap(double start1, double end1, double start2, double end2) =>
        end1 < start2 ? start2 - end1 : end2 < start1 ? start1 - end2 : 0;
}
