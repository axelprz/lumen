// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Library;

public enum LaunchOutcome
{
    Started,

    /// <summary>The entry has no launch (or install) action.</summary>
    NotLaunchable,
    Failed,
}

/// <summary>What the primary action of an entry did (for a short on-screen notice).</summary>
public sealed class LaunchEventArgs(LibraryEntry entry, bool installing, LaunchOutcome outcome) : EventArgs
{
    public LibraryEntry Entry { get; } = entry;

    /// <summary>True when the action handed the entry to its store's installer instead of playing it.</summary>
    public bool Installing { get; } = installing;

    public LaunchOutcome Outcome { get; } = outcome;
}

/// <summary>
/// Plays installed entries and hands not-installed ones to their store's installer. Games it plays are handed to the
/// <see cref="PlaytimeTracker"/> (PLAN-v1 §4.4: Lumen tracks what it launches).
/// </summary>
public sealed partial class LaunchService(ILauncher launcher, ILogger<LaunchService> logger, PlaytimeTracker? tracker = null)
{
    /// <summary>The action the primary button performs: launch when installed, install otherwise.</summary>
    public static LaunchSpec? PrimaryAction(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsInstalled ? entry.Launch : entry.Install;
    }

    /// <summary>Raised after a start or a failed start (not for entries with nothing to launch). May come from any thread.</summary>
    public event EventHandler<LaunchEventArgs>? Launched;

    public async Task<LaunchOutcome> RunPrimaryActionAsync(LibraryEntry entry, CancellationToken ct)
    {
        var spec = PrimaryAction(entry);
        if (spec is null)
        {
            return LaunchOutcome.NotLaunchable;
        }

        try
        {
            var pid = await launcher.StartAsync(spec, ct).ConfigureAwait(false);
            LogStarted(entry.Ref.ToString(), entry.IsInstalled ? "launch" : "install", pid);
            if (entry.IsInstalled)
            {
                tracker?.Track(entry);
            }

            Launched?.Invoke(this, new LaunchEventArgs(entry, !entry.IsInstalled, LaunchOutcome.Started));
            return LaunchOutcome.Started;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex, entry.Ref.ToString());
            Launched?.Invoke(this, new LaunchEventArgs(entry, !entry.IsInstalled, LaunchOutcome.Failed));
            return LaunchOutcome.Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started {Ref} ({Action}), pid {Pid}")]
    private partial void LogStarted(string @ref, string action, int? pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start {Ref}")]
    private partial void LogFailed(Exception exception, string @ref);
}
