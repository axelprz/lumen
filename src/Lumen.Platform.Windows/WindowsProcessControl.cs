// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.ComponentModel;
using System.Diagnostics;
using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

/// <summary>
/// Process operations for routines. Programs start with <c>UseShellExecute = false</c> (CreateProcess as the current
/// user): a program that demands admin fails with "elevation required" instead of showing a UAC prompt, so routines
/// never elevate (CLAUDE.md rule 3). Processes Windows won't describe without admin are skipped.
/// </summary>
public sealed class WindowsProcessControl : IProcessControl
{
    public RunningProcess Start(string path, string? arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(path, arguments ?? string.Empty)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(path) ?? string.Empty,
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The program didn't start.");
        return Describe(process) ?? new RunningProcess(process.Id, DateTimeOffset.Now, Path.GetFileName(path), path);
    }

    public IReadOnlyList<RunningProcess> FindByName(string imageName)
    {
        var name = Path.GetFileNameWithoutExtension(imageName);
        return Collect(Process.GetProcessesByName(name));
    }

    public IReadOnlyList<RunningProcess> FindInFolder(string folder)
    {
        var prefix = folder.TrimEnd('\\', '/') + "\\";
        return Collect(Process.GetProcesses())
            .Where(p => p.ImagePath?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
    }

    public bool IsRunning(RunningProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            using var current = Process.GetProcessById(process.Id);
            return !current.HasExited && Math.Abs((new DateTimeOffset(current.StartTime) - process.StartTime).TotalSeconds) < 1;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false; // gone, or not ours to see
        }
    }

    public bool RequestClose(RunningProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return With(process, p => p.CloseMainWindow());
    }

    public bool Kill(RunningProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return With(process, p =>
        {
            p.Kill(entireProcessTree: false);
            return true;
        });
    }

    public string? OriginalFileName(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).OriginalFilename;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    // Acts only when the id still belongs to the same process (Windows reuses ids).
    private bool With(RunningProcess target, Func<Process, bool> action)
    {
        if (!IsRunning(target))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(target.Id);
            return action(process);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static List<RunningProcess> Collect(Process[] all)
    {
        var result = new List<RunningProcess>(all.Length);
        foreach (var process in all)
        {
            using (process)
            {
                if (Describe(process) is { } described)
                {
                    result.Add(described);
                }
            }
        }

        return result;
    }

    private static RunningProcess? Describe(Process process)
    {
        try
        {
            var path = WindowsProcessList.ImagePathOf(process.Id);
            if (path is null)
            {
                return null; // another user's or protected process
            }

            return new RunningProcess(process.Id, new DateTimeOffset(process.StartTime), Path.GetFileName(path), path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null; // another user's or protected process
        }
    }
}
