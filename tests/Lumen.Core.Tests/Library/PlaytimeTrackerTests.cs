// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Library;
using Lumen.Core.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using static Lumen.Core.Tests.Library.TestEntries;

namespace Lumen.Core.Tests.Library;

public sealed class PlaytimeTrackerTests
{
    private const string GameExe = @"C:\Games\Sample\bin\sample.exe";

    private readonly ManualClock _clock = new();
    private readonly FakeProcesses _processes = new();
    private readonly MemoryPlaytime _store = new();
    private readonly LibraryEntry _game = Game("epic", "Sample", "Sample") with { InstallDirectory = @"C:\Games\Sample" };

    [Fact]
    public async Task Session_StartsAtFirstMatch_AndEndsAtLastSeen_After30sWithoutIt()
    {
        var tracker = Tracker();
        var changed = 0;
        tracker.SessionsChanged += (_, _) => changed++;
        tracker.Track(_game);

        await Poll(tracker, seconds: 10);                 // launcher still loading: nothing yet
        _processes.Paths.Add(GameExe);
        await Poll(tracker, seconds: 5);                  // t=15 s: game running → session starts
        await Poll(tracker, seconds: 600);                // t=615 s: still running
        _processes.Paths.Clear();
        await Poll(tracker, seconds: 20);                 // gone 20 s: not over yet
        _store.Finished.ShouldBeEmpty();
        await Poll(tracker, seconds: 15);                 // gone 35 s: over

        var session = _store.Finished.ShouldHaveSingleItem();
        session.End.ShouldBe(_clock.Start.AddSeconds(615)); // the last moment it was seen
        _store.Sessions[session.Id].Start.ShouldBe(_clock.Start.AddSeconds(15));
        session.Minimum.ShouldBe(PlaytimeTracker.MinimumSession);
        changed.ShouldBe(1);
        tracker.Watching.ShouldBe(0);
    }

    [Fact]
    public async Task LongSessions_AreSavedEveryMinute_SoClosingLumenLosesLittle()
    {
        var tracker = Tracker();
        tracker.Track(_game);
        _processes.Paths.Add(GameExe);

        for (var i = 0; i < 30; i++)
        {
            await Poll(tracker, seconds: 5); // 2.5 minutes of play
        }

        _store.Extensions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task NothingInTheFolder_Within5Minutes_StopsWatching()
    {
        var tracker = Tracker();
        tracker.Track(_game);
        _processes.Paths.Add(@"C:\Games\Other\other.exe");
        _processes.Paths.Add(@"C:\Games\SampleTwo\sample.exe"); // a sibling folder with the same prefix is not a match

        await Poll(tracker, seconds: 299);
        tracker.Watching.ShouldBe(1);
        await Poll(tracker, seconds: 2);

        tracker.Watching.ShouldBe(0);
        _store.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public void OnlyInstalledEntriesWithAFolder_AreWatched_Once()
    {
        var tracker = Tracker();

        tracker.Track(_game);
        tracker.Track(_game);
        tracker.Track(_game with { InstallDirectory = null });
        tracker.Track(Game("steam", "2", "Not installed", installed: false));

        tracker.Watching.ShouldBe(1);
    }

    [Fact]
    public async Task ProcessListFailures_AreIgnored()
    {
        var tracker = Tracker();
        tracker.Track(_game);
        _processes.Fail = true;

        await Poll(tracker, seconds: 5);

        tracker.Watching.ShouldBe(1);
    }

    private PlaytimeTracker Tracker() => new(_processes, _store, NullLogger<PlaytimeTracker>.Instance, _clock);

    private async Task Poll(PlaytimeTracker tracker, int seconds)
    {
        _clock.Advance(TimeSpan.FromSeconds(seconds));
        await tracker.TickAsync(CancellationToken.None);
    }

    private sealed class FakeProcesses : IProcessList
    {
        public List<string> Paths { get; } = [];

        public bool Fail { get; set; }

        public IReadOnlyList<string> GetImagePaths() => Fail ? throw new InvalidOperationException("no access") : Paths.ToList();
    }

    /// <summary>A clock the test moves by hand; its timers never fire (the test polls).</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Start { get; } = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new NoTimer();

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class MemoryPlaytime : IPlaytimeStore
    {
        public Dictionary<long, (ItemRef Ref, DateTimeOffset Start)> Sessions { get; } = [];

        public List<(long Id, DateTimeOffset End, TimeSpan Minimum)> Finished { get; } = [];

        public List<DateTimeOffset> Extensions { get; } = [];

        public Task<long> StartAsync(ItemRef itemRef, DateTimeOffset start, CancellationToken ct)
        {
            var id = Sessions.Count + 1;
            Sessions[id] = (itemRef, start);
            return Task.FromResult((long)id);
        }

        public Task ExtendAsync(long sessionId, DateTimeOffset lastSeen, CancellationToken ct)
        {
            Extensions.Add(lastSeen);
            return Task.CompletedTask;
        }

        public Task FinishAsync(long sessionId, DateTimeOffset end, TimeSpan minimum, CancellationToken ct)
        {
            Finished.Add((sessionId, end, minimum));
            return Task.CompletedTask;
        }

        public Task CloseLeftoversAsync(TimeSpan minimum, CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<ItemRef, PlaytimeTotal>> LoadTotalsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<ItemRef, PlaytimeTotal>>(new Dictionary<ItemRef, PlaytimeTotal>());
    }
}
