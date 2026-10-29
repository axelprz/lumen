// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Globalization;
using Lumen.Core.Localization;
using Lumen.Core.Routines;

namespace Lumen.App.ViewModels;

/// <summary>One step as a line of plain language for the preview and the editor.</summary>
/// <param name="Note">What to pay attention to (outside the library, closes programs, not on this PC…), or null.</param>
/// <param name="IsHighlighted">Flagged for a closer look (ARCHITECTURE §7): programs outside the library and closing.</param>
public sealed record StepLine(int Number, string Text, string? Note, bool IsHighlighted, bool IsBlocked);

/// <summary>Turns reviewed steps into translated sentences ("Open Hollow Knight", "Wait 5 seconds", …).</summary>
public static class RoutineText
{
    public static IReadOnlyList<StepLine> Lines(RoutineReview review, ILocalizer loc) =>
        review.Steps.Select(s => Line(s, loc)).ToList();

    public static StepLine Line(StepReview step, ILocalizer loc)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(loc);
        // A label never hides what the step really does.
        var text = step.Action.Label is { Length: > 0 } label ? $"{label} — {Describe(step, loc)}" : Describe(step, loc);

        var notes = new List<string>();
        if (step.IsBlocked)
        {
            notes.Add(loc["Routine_Blocked_" + step.Blocked]);
        }

        if (step.Notes.HasFlag(StepNotes.OutsideLibrary))
        {
            notes.Add(loc["Routine_Note_OutsideLibrary"]);
        }

        if (step.Notes.HasFlag(StepNotes.ForcesClose))
        {
            notes.Add(loc["Routine_Note_ForcesClose"]);
        }
        else if (step.Notes.HasFlag(StepNotes.Closes))
        {
            notes.Add(loc["Routine_Note_Closes"]);
        }

        if (step.Notes.HasFlag(StepNotes.NotInLibrary))
        {
            notes.Add(loc["Routine_Note_NotInLibrary"]);
        }

        if (step.Notes.HasFlag(StepNotes.NotFound))
        {
            notes.Add(loc["Routine_Note_NotFound"]);
        }

        var highlighted = step.Notes.HasFlag(StepNotes.OutsideLibrary) || step.Notes.HasFlag(StepNotes.Closes);
        return new StepLine(step.Index + 1, text, notes.Count == 0 ? null : string.Join(" · ", notes), highlighted, step.IsBlocked);
    }

    private static string Describe(StepReview step, ILocalizer loc) => step.Action switch
    {
        LaunchAction { Ref: not null } => Format(loc, "Routine_Step_LaunchItem", step.Target),
        LaunchAction { Arguments: { Length: > 0 } args } => Format(loc, "Routine_Step_LaunchProgramArgs", step.Target, args),
        LaunchAction => Format(loc, "Routine_Step_LaunchProgram", step.Target),
        UrlAction => Format(loc, "Routine_Step_Url", step.Target),
        FolderAction => Format(loc, "Routine_Step_Folder", step.Target),
        WaitAction wait => Format(loc, "Routine_Step_Wait", (wait.Milliseconds / 1000.0).ToString("0.#", CultureInfo.CurrentCulture)),
        CloseAction => Format(loc, "Routine_Step_Close", step.Target),
        _ => step.Action.GetType().Name,
    };

    private static string Format(ILocalizer loc, string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, loc[key], values);
}
