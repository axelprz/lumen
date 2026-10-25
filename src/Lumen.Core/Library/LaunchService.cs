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

/// <summary>Plays installed entries and hands not-installed ones to their store's installer.</summary>
public sealed partial class LaunchService(ILauncher launcher, ILogger<LaunchService> logger)
{
    /// <summary>The action the primary button performs: launch when installed, install otherwise.</summary>
    public static LaunchSpec? PrimaryAction(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsInstalled ? entry.Launch : entry.Install;
    }

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
            return LaunchOutcome.Started;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex, entry.Ref.ToString());
            return LaunchOutcome.Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started {Ref} ({Action}), pid {Pid}")]
    private partial void LogStarted(string @ref, string action, int? pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start {Ref}")]
    private partial void LogFailed(Exception exception, string @ref);
}
