// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Shell;

/// <summary>
/// Command-line options Lumen understands. Only one so far: <c>--tray</c>, used by "Start with Windows" so Lumen starts
/// in the notification area without opening its window. Unknown arguments are ignored.
/// </summary>
public sealed record StartupOptions(bool StartInTray)
{
    public const string TrayArgument = "--tray";

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return new StartupOptions(args.Any(a => string.Equals(a?.Trim(), TrayArgument, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The command Windows runs at sign-in: the quoted executable path plus <c>--tray</c>.</summary>
    public static string StartWithWindowsCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("A path can't contain quotes.", nameof(executablePath));
        }

        return $"\"{executablePath}\" {TrayArgument}";
    }
}
