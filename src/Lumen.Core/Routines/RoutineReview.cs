// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;

namespace Lumen.Core.Routines;

/// <summary>What the preview should point out about a step.</summary>
[Flags]
public enum StepNotes
{
    None = 0,

    /// <summary>Opens a program that isn't in the library (highlighted, ARCHITECTURE §7).</summary>
    OutsideLibrary = 1,

    /// <summary>Closes programs (highlighted).</summary>
    Closes = 2,

    /// <summary>Ends programs that don't close in time; unsaved work is lost.</summary>
    ForcesClose = 4,

    /// <summary>The library item isn't on this PC's library.</summary>
    NotInLibrary = 8,

    /// <summary>The program or folder doesn't exist on this PC.</summary>
    NotFound = 16,
}

/// <summary>One step as the preview shows it: what it does, with paths and variables already filled in.</summary>
/// <param name="Target">The expanded path or address; for library items, the item's title when known.</param>
public sealed record StepReview(int Index, RoutineAction Action, string? Target, StepNotes Notes, BlockedReason Blocked)
{
    public bool IsBlocked => Blocked != BlockedReason.None;
}

/// <summary>
/// The full list of steps for the trust preview (PLAN-v1 §4.5): every action in order, with programs outside the
/// library and close actions flagged, and blocked steps marked. A routine with a blocked step can't be approved.
/// </summary>
public sealed record RoutineReview(Routine Routine, IReadOnlyList<StepReview> Steps)
{
    public bool CanRun => Steps.All(s => !s.IsBlocked);

    /// <param name="findItem">The library item for a ref on this PC, or null.</param>
    public static RoutineReview Create(Routine routine, Func<ItemRef, LibraryItem?> findItem, Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        ArgumentNullException.ThrowIfNull(routine);
        ArgumentNullException.ThrowIfNull(findItem);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(directoryExists);

        var steps = routine.Actions.Select((action, i) => action switch
        {
            LaunchAction { Ref: { } itemRef } => findItem(itemRef) is { } item
                ? new StepReview(i, action, item.Title, StepNotes.None, LaunchesBlockedProgram(item) ? BlockedReason.ScriptHost : BlockedReason.None)
                : new StepReview(i, action, itemRef.ToString(), StepNotes.NotInLibrary, BlockedReason.None),
            LaunchAction { Path: { } raw } => Program(i, action, RoutineSafety.Expand(raw, routine.Variables), fileExists),
            UrlAction url => new StepReview(i, action, url.Url, StepNotes.None, RoutineParser.IsWebUrl(url.Url) ? BlockedReason.None : BlockedReason.NotAWebPage),
            FolderAction folder => Folder(i, action, RoutineSafety.Expand(folder.Path, routine.Variables), directoryExists),
            CloseAction close => Close(i, close, findItem),
            _ => new StepReview(i, action, null, StepNotes.None, BlockedReason.None),
        }).ToList();

        return new RoutineReview(routine, steps);
    }

    // A library app can be a terminal (e.g. the Start menu's PowerShell shortcut): routines can't open those either.
    private static bool LaunchesBlockedProgram(LibraryItem item) =>
        item.Primary.Launch is { Kind: LaunchKind.Executable or LaunchKind.ProcessWithArgs } spec
        && RoutineSafety.IsBlockedProgramName(Path.GetFileName(spec.Target));

    private static StepReview Program(int i, RoutineAction action, string path, Func<string, bool> fileExists)
    {
        var blocked = RoutineSafety.CheckProgram(path);
        var notes = StepNotes.OutsideLibrary;
        if (blocked == BlockedReason.None && !fileExists(path))
        {
            notes |= StepNotes.NotFound;
        }

        return new StepReview(i, action, path, notes, blocked);
    }

    private static StepReview Folder(int i, RoutineAction action, string path, Func<string, bool> directoryExists)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            return new StepReview(i, action, path, StepNotes.None, BlockedReason.NotAbsolute);
        }

        return new StepReview(i, action, path, directoryExists(path) ? StepNotes.None : StepNotes.NotFound, BlockedReason.None);
    }

    private static StepReview Close(int i, CloseAction close, Func<ItemRef, LibraryItem?> findItem)
    {
        var notes = StepNotes.Closes | (close.Force ? StepNotes.ForcesClose : StepNotes.None);
        var names = close.Refs.Select(r => findItem(r)?.Title ?? r.ToString()).Concat(close.Processes).ToList();
        if (close.Refs.Any(r => findItem(r) is null))
        {
            notes |= StepNotes.NotInLibrary;
        }

        var blocked = close.Processes.Any(RoutineSafety.IsProtectedProcess) ? BlockedReason.ProtectedProcess : BlockedReason.None;
        return new StepReview(i, close, string.Join(", ", names), notes, blocked);
    }
}
