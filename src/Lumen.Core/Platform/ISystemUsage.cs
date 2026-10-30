// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Platform;

/// <summary>System-wide CPU times since boot, in 100-ns ticks. Kernel time includes idle time (as Windows reports it).</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

/// <summary>Physical memory, in bytes.</summary>
public readonly record struct MemoryUsage(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes > AvailableBytes ? TotalBytes - AvailableBytes : 0;

    /// <summary>Used share, 0 to 1.</summary>
    public double UsedFraction => TotalBytes == 0 ? 0 : (double)UsedBytes / TotalBytes;
}

/// <summary>Reads how busy this PC is (the sidebar monitor). Local only: nothing leaves the machine.</summary>
public interface ISystemUsage
{
    /// <summary>Null when the system would not say.</summary>
    CpuTimes? ReadCpuTimes();

    /// <summary>Null when the system would not say.</summary>
    MemoryUsage? ReadMemory();
}

public static class CpuLoad
{
    /// <summary>
    /// Share of time the CPUs were busy between two readings, 0 to 1. Null when no time passed or the counters went
    /// backwards (they never should, but a wrong number is worse than none).
    /// </summary>
    public static double? Between(CpuTimes earlier, CpuTimes later)
    {
        if (later.Idle < earlier.Idle || later.Kernel < earlier.Kernel || later.User < earlier.User)
        {
            return null;
        }

        var idle = later.Idle - earlier.Idle;
        var total = (later.Kernel - earlier.Kernel) + (later.User - earlier.User);
        if (total == 0 || idle > total)
        {
            return null;
        }

        return (double)(total - idle) / total;
    }
}
