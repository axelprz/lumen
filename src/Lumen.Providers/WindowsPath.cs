// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Providers;

/// <summary>
/// Store data uses Windows paths. These helpers keep backslashes regardless of the OS that runs the tests
/// (System.IO.Path would use '/' on Linux).
/// </summary>
internal static class WindowsPath
{
    public static string Normalize(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');

    public static string Join(string left, string right) =>
        Normalize(left) + "\\" + right.Replace('/', '\\').TrimStart('\\');

    public static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    public static string? Directory(string path)
    {
        var i = path.LastIndexOfAny(['\\', '/']);
        return i <= 0 ? null : path[..i];
    }
}
