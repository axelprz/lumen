// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Routines;

public enum StepOutcome
{
    Done,

    /// <summary>Nothing to do (already running, nothing to close).</summary>
    Skipped,

    Failed,

    /// <summary>Refused by the safety rules (never runs).</summary>
    Blocked,

    /// <summary>Not run: an earlier step failed with stopOnError, or the run was cancelled.</summary>
    NotRun,
}

/// <summary>Why a step failed, for the UI to explain.</summary>
public enum StepProblem
{
    None,
    NotInLibrary,
    NotInstalled,
    NotFound,
    CouldNotStart,
    StillRunning,
    TimedOut,
}

public sealed record StepResult(int Index, StepOutcome Outcome, StepProblem Problem = StepProblem.None);

/// <summary>One run of a routine: what each step did and what it opened (for undo).</summary>
public sealed class RoutineRun(Routine routine, DateTimeOffset startedAt)
{
    private readonly List<StepResult> _steps = [];
    private readonly List<RunningProcess> _started = [];
    private readonly List<string> _folders = [];

    public Routine Routine { get; } = routine;

    public DateTimeOffset StartedAt { get; } = startedAt;

    public IReadOnlyList<StepResult> Steps => _steps;

    /// <summary>Programs this run started (id + start time).</summary>
    public IReadOnlyList<RunningProcess> Started => _started;

    /// <summary>Install folders of library items this run launched (store launchers don't report a process).</summary>
    public IReadOnlyList<string> LaunchedFolders => _folders;

    public bool Succeeded => _steps.All(s => s.Outcome is StepOutcome.Done or StepOutcome.Skipped);

    public bool CanUndo => _started.Count > 0 || _folders.Count > 0;

    internal void Add(StepResult step) => _steps.Add(step);

    internal void Opened(RunningProcess process) => _started.Add(process);

    internal void Opened(string folder) => _folders.Add(folder);
}

/// <summary>
/// Runs a reviewed routine (ARCHITECTURE §7): one step after another, each with a time limit. A failed step doesn't
/// stop the rest unless the routine says <c>stopOnError</c>. Every safety check is repeated right before acting.
/// Programs start as the current user without a shell (never elevated). Close asks politely first and only ends a
/// program when the routine says <c>force</c>. Undo closes only what this run opened.
/// </summary>
public sealed partial class RoutineRunner(
    IProcessControl processes,
    ILauncher launcher,
    LaunchService launchService,
    IFileSystem fileSystem,
    ILogger<RoutineRunner> logger,
    TimeProvider? time = null)
{
    public static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan UndoGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Runs every step of an approved review.</summary>
    /// <param name="findItem">The library item for a ref, or null.</param>
    /// <exception cref="InvalidOperationException">The review has a blocked step.</exception>
    public async Task<RoutineRun> RunAsync(RoutineReview review, Func<ItemRef, LibraryItem?> findItem, IProgress<StepResult>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(findItem);
        if (!review.CanRun)
        {
            throw new InvalidOperationException("A routine with a blocked step can't run.");
        }

        var routine = review.Routine;
        var run = new RoutineRun(routine, _time.GetUtcNow());
        LogRunning(routine.Id, routine.Actions.Count);
        var stop = false;
        for (var i = 0; i < routine.Actions.Count; i++)
        {
            StepResult result;
            if (stop || ct.IsCancellationRequested)
            {
                result = new StepResult(i, StepOutcome.NotRun);
            }
            else
            {
                result = await RunStepAsync(i, routine.Actions[i], routine, run, findItem, ct).ConfigureAwait(false);
                stop = routine.StopOnError && result.Outcome is StepOutcome.Failed or StepOutcome.Blocked;
            }

            run.Add(result);
            progress?.Report(result);
        }

        LogFinished(routine.Id, run.Succeeded);
        return run;
    }

    /// <summary>Closes what this run opened that is still running (asks first; never forces).</summary>
    /// <returns>How many programs were asked to close.</returns>
    public async Task<int> UndoAsync(RoutineRun run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        var targets = run.Started.Where(processes.IsRunning).ToList();
        foreach (var folder in run.LaunchedFolders)
        {
            // Store launchers start the game themselves; only what appeared in its folder after the run began counts.
            targets.AddRange(processes.FindInFolder(folder).Where(p => p.StartTime >= run.StartedAt));
        }

        targets = targets.DistinctBy(p => (p.Id, p.StartTime)).ToList();
        foreach (var process in targets)
        {
            processes.RequestClose(process);
        }

        await WaitUntilClosedAsync(targets, UndoGrace, ct).ConfigureAwait(false);
        LogUndone(run.Routine.Id, targets.Count);
        return targets.Count;
    }

    private async Task<StepResult> RunStepAsync(int index, RoutineAction action, Routine routine, RoutineRun run, Func<ItemRef, LibraryItem?> findItem, CancellationToken ct)
    {
        var limit = action.TimeoutMs is { } ms
            ? TimeSpan.FromMilliseconds(ms)
            : action is WaitAction wait ? TimeSpan.FromMilliseconds(wait.Milliseconds) + TimeSpan.FromSeconds(5) : DefaultStepTimeout;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try
        {
            return action switch
            {
                LaunchAction { Ref: { } itemRef } launch => await LaunchItemAsync(index, itemRef, launch, run, findItem, timeout.Token).ConfigureAwait(false),
                LaunchAction { Path: { } path } launch => LaunchProgram(index, launch, RoutineSafety.Expand(path, routine.Variables), routine, run),
                UrlAction url => await OpenUrlAsync(index, url, timeout.Token).ConfigureAwait(false),
                FolderAction folder => await OpenFolderAsync(index, RoutineSafety.Expand(folder.Path, routine.Variables), timeout.Token).ConfigureAwait(false),
                WaitAction pause => await WaitAsync(index, pause, timeout.Token).ConfigureAwait(false),
                CloseAction close => await CloseAsync(index, close, findItem, timeout.Token).ConfigureAwait(false),
                _ => new StepResult(index, StepOutcome.Failed, StepProblem.CouldNotStart),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new StepResult(index, StepOutcome.Failed, StepProblem.TimedOut);
        }
        catch (OperationCanceledException)
        {
            return new StepResult(index, StepOutcome.NotRun);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogStepFailed(ex, routine.Id, index);
            return new StepResult(index, StepOutcome.Failed, StepProblem.CouldNotStart);
        }
    }

    private async Task<StepResult> LaunchItemAsync(int index, ItemRef itemRef, LaunchAction launch, RoutineRun run, Func<ItemRef, LibraryItem?> findItem, CancellationToken ct)
    {
        if (findItem(itemRef) is not { } item)
        {
            return new StepResult(index, StepOutcome.Failed, StepProblem.NotInLibrary);
        }

        // The routine's own store first, else another installed copy of the same (merged) game.
        var entry = item.Entries.FirstOrDefault(e => e.Ref == itemRef && e.IsInstalled) ?? item.Entries.FirstOrDefault(e => e.IsInstalled);
        if (entry?.Launch is not { } spec)
        {
            return new StepResult(index, StepOutcome.Failed, StepProblem.NotInstalled);
        }

        if (spec.Kind is LaunchKind.Executable or LaunchKind.ProcessWithArgs && RoutineSafety.IsBlockedProgramName(Path.GetFileName(spec.Target)))
        {
            return new StepResult(index, StepOutcome.Blocked);
        }

        if (launch.SkipIfRunning && entry.InstallDirectory is { Length: > 0 } running && processes.FindInFolder(running).Count > 0)
        {
            return new StepResult(index, StepOutcome.Skipped);
        }

        if (await launchService.RunPrimaryActionAsync(entry, ct).ConfigureAwait(false) != LaunchOutcome.Started)
        {
            return new StepResult(index, StepOutcome.Failed, StepProblem.CouldNotStart);
        }

        if (entry.InstallDirectory is { Length: > 0 } folder)
        {
            run.Opened(folder);
        }

        return new StepResult(index, StepOutcome.Done);
    }

    private StepResult LaunchProgram(int index, LaunchAction launch, string path, Routine routine, RoutineRun run)
    {
        if (RoutineSafety.CheckProgram(path) != BlockedReason.None)
        {
            return new StepResult(index, StepOutcome.Blocked);
        }

        path = Path.GetFullPath(path);
        if (!fileSystem.FileExists(path))
        {
            return new StepResult(index, StepOutcome.Failed, StepProblem.NotFound);
        }

        // A renamed copy of a blocked program (cmd.exe saved as game.exe) still says what it is in its version info.
        if (RoutineSafety.IsBlockedProgramName(processes.OriginalFileName(path)))
        {
            return new StepResult(index, StepOutcome.Blocked);
        }

        string? workingDirectory = null;
        if (launch.WorkingDirectory is { } raw)
        {
            workingDirectory = RoutineSafety.Expand(raw, routine.Variables);
            if (!Path.IsPathFullyQualified(workingDirectory) || !fileSystem.DirectoryExists(workingDirectory))
            {
                return new StepResult(index, StepOutcome.Failed, StepProblem.NotFound);
            }
        }

        if (launch.SkipIfRunning && processes.FindByName(Path.GetFileName(path)).Any(p => string.Equals(p.ImagePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return new StepResult(index, StepOutcome.Skipped);
        }

        var arguments = launch.Arguments is null ? null : RoutineSafety.Expand(launch.Arguments, routine.Variables);
        run.Opened(processes.Start(path, arguments, workingDirectory ?? Path.GetDirectoryName(path)));
        return new StepResult(index, StepOutcome.Done);
    }

    private async Task<StepResult> OpenUrlAsync(int index, UrlAction action, CancellationToken ct)
    {
        if (!RoutineParser.IsWebUrl(action.Url))
        {
            return new StepResult(index, StepOutcome.Blocked);
        }

        await launcher.StartAsync(new LaunchSpec(LaunchKind.Uri, action.Url), ct).ConfigureAwait(false);
        return new StepResult(index, StepOutcome.Done);
    }

    // Opened with explorer.exe and only when it is a folder: a file path here would run the file.
    private async Task<StepResult> OpenFolderAsync(int index, string path, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"', StringComparison.Ordinal))
        {
            return new StepResult(index, StepOutcome.Blocked);
        }

        if (!fileSystem.DirectoryExists(path))
        {
            return new StepResult(index, StepOutcome.Failed, StepProblem.NotFound);
        }

        await launcher.StartAsync(new LaunchSpec(LaunchKind.Executable, "explorer.exe", $"\"{path}\""), ct).ConfigureAwait(false);
        return new StepResult(index, StepOutcome.Done);
    }

    private async Task<StepResult> WaitAsync(int index, WaitAction wait, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(wait.Milliseconds), _time, ct).ConfigureAwait(false);
        return new StepResult(index, StepOutcome.Done);
    }

    private async Task<StepResult> CloseAsync(int index, CloseAction close, Func<ItemRef, LibraryItem?> findItem, CancellationToken ct)
    {
        if (close.Processes.Any(RoutineSafety.IsProtectedProcess))
        {
            return new StepResult(index, StepOutcome.Blocked);
        }

        var targets = new List<RunningProcess>();
        foreach (var itemRef in close.Refs)
        {
            var folders = findItem(itemRef)?.Entries.Where(e => e.IsInstalled).Select(e => e.InstallDirectory).OfType<string>() ?? [];
            foreach (var folder in folders.Where(f => f.Length > 0))
            {
                targets.AddRange(processes.FindInFolder(folder));
            }
        }

        foreach (var name in close.Processes)
        {
            targets.AddRange(processes.FindByName(name));
        }

        // Never Windows' own or Lumen, even when found through a library item's folder.
        targets = targets.Where(p => !RoutineSafety.IsProtectedProcess(p.Name)).DistinctBy(p => (p.Id, p.StartTime)).ToList();
        if (targets.Count == 0)
        {
            return new StepResult(index, StepOutcome.Skipped);
        }

        foreach (var process in targets)
        {
            processes.RequestClose(process);
        }

        var left = await WaitUntilClosedAsync(targets, TimeSpan.FromMilliseconds(close.GraceMs), ct).ConfigureAwait(false);
        if (left.Count > 0 && close.Force)
        {
            foreach (var process in left)
            {
                processes.Kill(process);
            }

            left = await WaitUntilClosedAsync(left, TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }

        return left.Count == 0 ? new StepResult(index, StepOutcome.Done) : new StepResult(index, StepOutcome.Failed, StepProblem.StillRunning);
    }

    private async Task<List<RunningProcess>> WaitUntilClosedAsync(List<RunningProcess> targets, TimeSpan grace, CancellationToken ct)
    {
        var deadline = _time.GetUtcNow() + grace;
        var left = targets.Where(processes.IsRunning).ToList();
        while (left.Count > 0 && _time.GetUtcNow() < deadline)
        {
            await Task.Delay(PollInterval, _time, ct).ConfigureAwait(false);
            left = left.Where(processes.IsRunning).ToList();
        }

        return left;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Routine {Id}: running {Steps} steps")]
    private partial void LogRunning(string id, int steps);

    [LoggerMessage(Level = LogLevel.Information, Message = "Routine {Id}: finished (all steps ok: {Ok})")]
    private partial void LogFinished(string id, bool ok);

    [LoggerMessage(Level = LogLevel.Information, Message = "Routine {Id}: undo asked {Count} programs to close")]
    private partial void LogUndone(string id, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Routine {Id}: step {Index} failed")]
    private partial void LogStepFailed(Exception exception, string id, int index);
}
