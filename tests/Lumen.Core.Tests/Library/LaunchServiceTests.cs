// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class LaunchServiceTests
{
    private readonly RecordingLauncher _launcher = new();

    [Fact]
    public async Task Installed_RunsLaunchSpec()
    {
        var entry = Game("steam", "1", "A");

        (await Service().RunPrimaryActionAsync(entry, CancellationToken.None)).ShouldBe(LaunchOutcome.Started);

        _launcher.Started.Single().ShouldBe(entry.Launch);
    }

    [Fact]
    public async Task NotInstalled_RunsInstallSpec()
    {
        var entry = Game("steam", "1", "A", installed: false);

        await Service().RunPrimaryActionAsync(entry, CancellationToken.None);

        _launcher.Started.Single().ShouldBe(entry.Install);
    }

    [Fact]
    public async Task NoSpec_IsNotLaunchable()
    {
        var entry = Game("steam", "1", "A") with { Launch = null };

        (await Service().RunPrimaryActionAsync(entry, CancellationToken.None)).ShouldBe(LaunchOutcome.NotLaunchable);
        _launcher.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task LauncherError_IsReportedNotThrown()
    {
        _launcher.Throw = true;

        (await Service().RunPrimaryActionAsync(Game("steam", "1", "A"), CancellationToken.None)).ShouldBe(LaunchOutcome.Failed);
    }

    [Fact]
    public async Task Launched_ReportsStartsAndFailures_NotEntriesWithNothingToLaunch()
    {
        var service = Service();
        var seen = new List<LaunchEventArgs>();
        service.Launched += (_, e) => seen.Add(e);

        await service.RunPrimaryActionAsync(Game("steam", "1", "A"), CancellationToken.None);
        await service.RunPrimaryActionAsync(Game("steam", "2", "B", installed: false), CancellationToken.None);
        await service.RunPrimaryActionAsync(Game("steam", "3", "C") with { Launch = null }, CancellationToken.None);
        _launcher.Throw = true;
        await service.RunPrimaryActionAsync(Game("steam", "4", "D"), CancellationToken.None);

        seen.Select(e => (e.Entry.Title, e.Installing, e.Outcome)).ShouldBe(
        [
            ("A", false, LaunchOutcome.Started),
            ("B", true, LaunchOutcome.Started),
            ("D", false, LaunchOutcome.Failed),
        ]);
    }

    private LaunchService Service() => new(_launcher, NullLogger<LaunchService>.Instance);

    private sealed class RecordingLauncher : ILauncher
    {
        public List<LaunchSpec> Started { get; } = [];

        public bool Throw { get; set; }

        public Task<int?> StartAsync(LaunchSpec spec, CancellationToken ct)
        {
            if (Throw)
            {
                throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
            }

            Started.Add(spec);
            return Task.FromResult<int?>(null);
        }
    }
}
