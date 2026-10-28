// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

/// <summary>
/// Running processes' image paths via <c>QueryFullProcessImageName</c> with "limited information" access, which
/// works for games (32- or 64-bit, Store apps) without admin. Processes that refuse even that are skipped.
/// </summary>
public sealed partial class WindowsProcessList : IProcessList
{
    private const uint QueryLimitedInformation = 0x1000;

    public unsafe IReadOnlyList<string> GetImagePaths()
    {
        var paths = new List<string>();
        var buffer = stackalloc char[1024];
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var handle = OpenProcess(QueryLimitedInformation, false, (uint)process.Id);
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var length = 1024u;
                    if (QueryFullProcessImageNameW(handle, 0, buffer, ref length))
                    {
                        paths.Add(new string(buffer, 0, (int)length));
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }

        return paths;
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);
}
