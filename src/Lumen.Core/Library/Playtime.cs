// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Lumen.Core.Platform;
using Microsoft.Extensions.Logging;

namespace Lumen.Core.Library;

/// <summary>Lumen's own record for one entry: minutes played in sessions it saw, and the end of the latest one.</summary>
public sealed record PlaytimeTotal(long Minutes, DateTimeOffset? LastPlayed);

/// <summary>
/// Play sessions Lumen recorded (SQLite <c>play_sessions</c>, per machine, never synced). A session is written as soon
/// as it starts and its end is moved forward while the game runs, so closing Lumen mid-game loses at most a minute.
/// </summary>
public interface IPlaytimeStore
{
    /// <returns>The new session's id.</returns>
    Task<long> StartAsync(ItemRef itemRef, DateTimeOffset start, CancellationToken ct);

    /// <summary>Moves the session's end forward while the game is still running.</summary>
    Task ExtendAsync(long sessionId, DateTimeOffset lastSeen, CancellationToken ct);

    /// <summary>Closes the session at <paramref name="end"/>; sessions shorter than <paramref name="minimum"/> are dropped.</summary>
    Task FinishAsync(long sessionId, DateTimeOffset end, TimeSpan minimum, CancellationToken ct);

    /// <summary>Closes sessions left open by a previous run (Lumen closed mid-game) at their last recorded time.</summary>
    Task CloseLeftoversAsync(TimeSpan minimum, CancellationToken ct);

    Task<IReadOnlyDictionary<ItemRef, PlaytimeTotal>> LoadTotalsAsync(CancellationToken ct);
}

/// <summary>
/// PLAN-v1 §4.4: Lumen tracks what it launches by watching for processes whose image path is inside the entry's
/// install folder. A session starts at the first match and ends after <see cref="EndAfter"/> without one; if nothing
/// shows up within <see cref="WaitForStart"/> (e.g. the launcher stayed on its login screen) the watch is dropped.
/// Only reads the process list; needs no admin.
/// </summary>
public sealed partial class PlaytimeTracker : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EndAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WaitForStart = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MinimumSession = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);

    private readonly IProcessList _processes;
    private readonly IPlaytimeStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<PlaytimeTracker> _logger;
    private readonly List<Watch> _watches = [];
    private readonly SemaphoreSlim _tick = new(1, 1);
    private ITimer? _timer;
    private bool _disposed;

    public PlaytimeTracker(IProcessList processes, IPlaytimeStore store, ILogger<PlaytimeTracker> logger, TimeProvider? time = null)
    {
        _processes = processes;
        _store = store;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised (on a worker thread) after a session was saved, so totals can be reloaded.</summary>
    public event EventHandler? SessionsChanged;

    /// <summary>Entries being watched right now (for tests and diagnostics).</summary>
    public int Watching
    {
        get
        {
            lock (_watches)
            {
                return _watches.Count;
            }
        }
    }

    /// <summary>Closes sessions a previous run left open. Call once at startup.</summary>
    public Task RecoverAsync(CancellationToken ct) => _store.CloseLeftoversAsync(MinimumSession, ct);

    /// <summary>Starts watching an entry Lumen just launched (installed entries with a folder only).</summary>
    public void Track(LibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsInstalled || string.IsNullOrWhiteSpace(entry.InstallDirectory) || _disposed)
        {
            return;
        }

        var folder = entry.InstallDirectory.TrimEnd('\\', '/') + "\\";
        lock (_watches)
        {
            if (_watches.Any(w => w.Ref == entry.Ref))
            {
                return; // already watching (Play pressed twice)
            }

            _watches.Add(new Watch(entry.Ref, folder, _time.GetUtcNow()));
            _timer ??= _time.CreateTimer(_ => _ = TickAsync(CancellationToken.None), null, PollInterval, PollInterval);
        }

        LogWatching(entry.Ref.ToString());
    }

    /// <summary>One poll: match processes to watched folders and advance each watch. Public for tests.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!await _tick.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return; // the previous poll is still running
        }

        try
        {
            List<Watch> watches;
            lock (_watches)
            {
                watches = [.. _watches];
            }

            if (watches.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> paths;
            try
            {
                paths = _processes.GetImagePaths();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogListFailed(ex);
                return;
            }

            var now = _time.GetUtcNow();
            var changed = false;
            foreach (var watch in watches)
            {
                var running = paths.Any(p => p.StartsWith(watch.Folder, StringComparison.OrdinalIgnoreCase));
                changed |= await AdvanceAsync(watch, running, now, ct).ConfigureAwait(false);
            }

            if (changed)
            {
                SessionsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTickFailed(ex); // playtime is a convenience: never let it break anything
        }
        finally
        {
            _tick.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
    }

    // Returns true when a session was finished (totals changed).
    private async Task<bool> AdvanceAsync(Watch watch, bool running, DateTimeOffset now, CancellationToken ct)
    {
        if (running)
        {
            watch.LastSeen = now;
            if (watch.SessionId is null)
            {
                watch.SessionId = await _store.StartAsync(watch.Ref, now, ct).ConfigureAwait(false);
                watch.SavedAt = now;
                LogStarted(watch.Ref.ToString());
            }
            else if (now - watch.SavedAt >= SaveEvery)
            {
                await _store.ExtendAsync(watch.SessionId.Value, now, ct).ConfigureAwait(false);
                watch.SavedAt = now;
            }

            return false;
        }

        if (watch.SessionId is { } id && watch.LastSeen is { } last && now - last >= EndAfter)
        {
            await _store.FinishAsync(id, last, MinimumSession, ct).ConfigureAwait(false);
            Remove(watch);
            LogEnded(watch.Ref.ToString(), (last - watch.Started!.Value).TotalMinutes);
            return true;
        }

        if (watch.SessionId is null && now - watch.Launched >= WaitForStart)
        {
            Remove(watch);
            LogGaveUp(watch.Ref.ToString());
        }

        return false;
    }

    private void Remove(Watch watch)
    {
        lock (_watches)
        {
            _watches.Remove(watch);
            if (_watches.Count == 0)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }

    private sealed class Watch(ItemRef itemRef, string folder, DateTimeOffset launched)
    {
        public ItemRef Ref { get; } = itemRef;

        public string Folder { get; } = folder;

        public DateTimeOffset Launched { get; } = launched;

        public long? SessionId
        {
            get;
            set
            {
                field = value;
                Started ??= LastSeen;
            }
        }

        public DateTimeOffset? Started { get; private set; }

        public DateTimeOffset? LastSeen { get; set; }

        public DateTimeOffset SavedAt { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Playtime: watching {Ref}")]
    private partial void LogWatching(string @ref);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playtime: {Ref} started")]
    private partial void LogStarted(string @ref);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playtime: {Ref} ended after {Minutes:0.#} min")]
    private partial void LogEnded(string @ref, double minutes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Playtime: {Ref} never started a process in its folder; stopped watching")]
    private partial void LogGaveUp(string @ref);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Playtime: could not list processes")]
    private partial void LogListFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Playtime: a poll failed")]
    private partial void LogTickFailed(Exception exception);
}
