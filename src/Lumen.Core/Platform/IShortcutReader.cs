// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>What a Windows shortcut (.lnk) points to.</summary>
/// <param name="TargetPath">The program or file it opens; null for shortcuts to non-file targets (e.g. Control Panel items).</param>
/// <param name="IconIndex">Which icon inside <paramref name="IconPath"/> (files hold several).</param>
public sealed record ShortcutTarget(string? TargetPath, string? Arguments, string? IconPath, int IconIndex = 0);

/// <summary>Reads .lnk files. Read-only: never resolves (which can rewrite the file) and never runs anything.</summary>
public interface IShortcutReader
{
    /// <returns>The target, or null when the file can't be read as a shortcut.</returns>
    ShortcutTarget? Read(string shortcutPath);
}
