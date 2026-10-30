// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Runtime.InteropServices;
using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

/// <summary>CPU times (<c>GetSystemTimes</c>) and memory (<c>GlobalMemoryStatusEx</c>). Read-only, no elevation, no network.</summary>
public sealed partial class WindowsSystemUsage : ISystemUsage
{
    public CpuTimes? ReadCpuTimes() =>
        GetSystemTimes(out var idle, out var kernel, out var user)
            ? new CpuTimes(idle, kernel, user)
            : null;

    public unsafe MemoryUsage? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)sizeof(MemoryStatusEx) };
        return GlobalMemoryStatusEx(ref status) ? new MemoryUsage(status.TotalPhys, status.AvailPhys) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    // FILETIMEs read as 64-bit tick counts (same layout: low part first).
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
