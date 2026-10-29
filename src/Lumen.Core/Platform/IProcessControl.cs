// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>A running process. Id + start time identify it (ids are reused by Windows).</summary>
public sealed record RunningProcess(int Id, DateTimeOffset StartTime, string Name, string? ImagePath);

/// <summary>
/// What routines need from the OS (PLAN-v1 §4.5): start a program as the current user (never elevated: a program
/// that demands admin fails instead of prompting), find running processes, ask them to close, and end them.
/// Processes of other users or that need admin are invisible or refuse; that's expected.
/// </summary>
public interface IProcessControl
{
    /// <summary>Starts an <c>.exe</c> directly (no shell), returning the new process.</summary>
    RunningProcess Start(string path, string? arguments, string? workingDirectory);

    /// <summary>Running processes with this image name ("discord.exe"), case-insensitive.</summary>
    IReadOnlyList<RunningProcess> FindByName(string imageName);

    /// <summary>Running processes whose image lives in this folder or below it.</summary>
    IReadOnlyList<RunningProcess> FindInFolder(string folder);

    /// <summary>True when this exact process (id + start time) is still running.</summary>
    bool IsRunning(RunningProcess process);

    /// <summary>Asks it to close like the window's X button would. False when it has no window to ask.</summary>
    bool RequestClose(RunningProcess process);

    /// <summary>Ends it right away (unsaved work is lost). False when Windows refuses.</summary>
    bool Kill(RunningProcess process);

    /// <summary>The program's original file name from its version info ("Cmd.Exe"), or null; catches renamed copies.</summary>
    string? OriginalFileName(string path);
}
