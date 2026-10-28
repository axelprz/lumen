// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>
/// "Start with Windows" (PLAN-v1 §4.7): Lumen's own entry in the current user's startup list. Never needs admin and
/// never touches any other program's entry.
/// </summary>
public interface IStartupRegistration
{
    bool IsEnabled();

    /// <param name="command">What Windows runs at sign-in (see <see cref="Shell.StartupOptions.StartWithWindowsCommand"/>).</param>
    void Enable(string command);

    void Disable();
}
