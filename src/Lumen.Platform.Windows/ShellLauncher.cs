// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Diagnostics;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

/// <summary>Starts launch specs through the Windows shell, as the current user. Never uses the "runas" verb.</summary>
public sealed class ShellLauncher : ILauncher
{
    public Task<int?> StartAsync(LaunchSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();

        var startInfo = spec.Kind switch
        {
            LaunchKind.Uri => new ProcessStartInfo(spec.Target),
            LaunchKind.Executable or LaunchKind.ProcessWithArgs => new ProcessStartInfo(spec.Target, spec.Arguments ?? string.Empty)
            {
                WorkingDirectory = spec.WorkingDirectory ?? Path.GetDirectoryName(spec.Target) ?? string.Empty,
            },
            LaunchKind.ShellAppsFolder => new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{spec.Target}"),
            _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, "Unknown launch kind."),
        };
        startInfo.UseShellExecute = true;

        using var process = Process.Start(startInfo);
        return Task.FromResult<int?>(process?.Id);
    }
}
