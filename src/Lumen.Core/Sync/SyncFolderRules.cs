// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Sync;

/// <summary>Why a folder can't be used as the sync folder.</summary>
public enum SyncFolderProblem
{
    None,

    /// <summary>Not a full path (for example a relative one).</summary>
    NotAbsolute,

    /// <summary>A whole drive or network share: pick a folder inside it.</summary>
    DriveRoot,

    /// <summary>Windows, Program Files or ProgramData: Lumen never writes there.</summary>
    SystemFolder,
}

/// <summary>
/// The sync folder is the one place outside its own folders where Lumen writes (and only its own files:
/// <c>config.json</c>, <c>collections/</c>, <c>routines/</c>). So the choice is limited to ordinary folders. Pure text
/// rules (Windows paths), so they behave the same on any machine and in tests.
/// </summary>
public static class SyncFolderRules
{
    private static readonly string[] SystemNames = ["windows", "program files", "program files (x86)", "programdata"];

    public static SyncFolderProblem Check(string? path)
    {
        var parts = Segments(path, out var rooted, out var share);
        if (!rooted)
        {
            return SyncFolderProblem.NotAbsolute;
        }

        if (parts.Count == 0)
        {
            return SyncFolderProblem.DriveRoot;
        }

        // C:\Windows\... : the first folder under the drive. A share (\\server\share\x) has no system folders to check.
        return !share && SystemNames.Contains(parts[0], StringComparer.OrdinalIgnoreCase) ? SyncFolderProblem.SystemFolder : SyncFolderProblem.None;
    }

    // Folders after the drive ("C:") or the share ("\\server\share"); rooted = it starts with one of those.
    private static List<string> Segments(string? path, out bool rooted, out bool share)
    {
        rooted = false;
        share = false;
        var text = path?.Trim().Trim('"') ?? string.Empty;
        var all = text.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (text.StartsWith(@"\\", StringComparison.Ordinal) && !text.StartsWith(@"\\?\", StringComparison.Ordinal) && !text.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            rooted = all.Count >= 2;
            share = true;
            return rooted ? all.Skip(2).ToList() : [];
        }

        if (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] is '\\' or '/')
        {
            rooted = true;
            return all.Skip(1).ToList();
        }

        return [];
    }
}
