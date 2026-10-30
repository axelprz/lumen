// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Settings;
using Lumen.Core.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumen.Core.Tests.Sync;

public sealed class SyncWatchingTests
{
    private static readonly SyncOptions Fast = new() { Debounce = TimeSpan.FromMilliseconds(10), RescanInterval = TimeSpan.FromMilliseconds(100), Watch = true };

    [Fact]
    public async Task ALocalChange_ReachesTheFolderByItself()
    {
        var cloud = new MemoryFolder();
        var a = new FakePc(cloud);
        await a.Settings.SetAsync(LocalSettingKeys.Language, "en", CancellationToken.None);
        using var service = new SyncFolderService(cloud, _ => cloud, a.State, a.Settings, new NoWatcher(), NullLogger<SyncFolderService>.Instance, Fast);
        await service.AttachAsync(FakePc.FolderPath, null, CancellationToken.None);

        await a.Collections.CreateAsync("late", "Late", CancellationToken.None);
        await a.Collections.AddAsync("late", [ItemRef.Parse("steam:1")], CancellationToken.None);
        service.NotifyLocalChanged();

        await WaitForAsync(() => cloud.Has("collections/late.json"));
    }

    [Fact]
    public async Task AChangeInTheRoutinesFolder_IsAnnounced_AndOthersAreNot()
    {
        var cloud = new MemoryFolder();
        var a = new FakePc(cloud);
        await a.Settings.SetAsync(LocalSettingKeys.Language, "en", CancellationToken.None);
        var watcher = new CapturingWatcher();
        using var service = new SyncFolderService(cloud, _ => cloud, a.State, a.Settings, watcher, NullLogger<SyncFolderService>.Instance, Fast);
        var announced = 0;
        service.RoutinesChanged += (_, _) => Interlocked.Increment(ref announced);
        await service.AttachAsync(FakePc.FolderPath, null, CancellationToken.None);
        await WaitForAsync(() => watcher.Callback is not null);

        watcher.Callback!("/sync/collections/x.json");
        await Task.Delay(300);
        announced.ShouldBe(0);

        watcher.Callback("/sync/routines/work.json");
        await WaitForAsync(() => Volatile.Read(ref announced) > 0);
    }

    [Fact]
    public async Task ThePhysicalWatcher_SeesFilesInSubfolders_AndIgnoresTempFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lumen-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "routines"));
        try
        {
            var seen = new List<string>();
            using var watch = new PhysicalFolderWatcher().Watch(folder, p =>
            {
                lock (seen)
                {
                    seen.Add(p);
                }
            });

            await File.WriteAllTextAsync(Path.Combine(folder, "routines", "a.json.tmp"), "{}");
            await File.WriteAllTextAsync(Path.Combine(folder, "routines", "a.json"), "{}");

            await WaitForAsync(() =>
            {
                lock (seen)
                {
                    return seen.Any(p => p.EndsWith("a.json", StringComparison.Ordinal));
                }
            });
            lock (seen)
            {
                seen.ShouldNotContain(p => p.EndsWith(".tmp", StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }

    private sealed class CapturingWatcher : IFolderWatcher
    {
        public Action<string>? Callback { get; private set; }

        public IDisposable Watch(string folder, Action<string> changed)
        {
            Callback = changed;
            return new NoWatcher().Watch(folder, changed);
        }
    }
}
