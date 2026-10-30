// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;

namespace Lumen.Core.Tests.Platform;

public sealed class CpuLoadTests
{
    [Fact]
    public void Busy_IsKernelPlusUserMinusIdle_OverTheTotal()
    {
        // 1000 ticks passed: kernel 600 (of which 500 idle) + user 400 → busy 500 of 1000.
        var load = CpuLoad.Between(new CpuTimes(1000, 2000, 3000), new CpuTimes(1500, 2600, 3400));

        load.ShouldBe(0.5);
    }

    [Fact]
    public void AllIdle_IsZero()
    {
        CpuLoad.Between(new CpuTimes(0, 0, 0), new CpuTimes(100, 100, 0)).ShouldBe(0);
    }

    [Theory]
    [InlineData(100, 100, 100)] // no time passed
    [InlineData(50, 200, 200)] // idle went backwards
    public void UnusableReadings_AreNull(ulong idle, ulong kernel, ulong user)
    {
        CpuLoad.Between(new CpuTimes(100, 100, 100), new CpuTimes(idle, kernel, user)).ShouldBeNull();
    }

    [Fact]
    public void Memory_UsedFraction()
    {
        var memory = new MemoryUsage(16UL << 30, 4UL << 30);

        memory.UsedBytes.ShouldBe(12UL << 30);
        memory.UsedFraction.ShouldBe(0.75);
        new MemoryUsage(0, 0).UsedFraction.ShouldBe(0);
    }
}
