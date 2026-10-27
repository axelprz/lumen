// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using Microsoft.Extensions.Logging;

namespace Lumen.Core.Library;

/// <summary>
/// Keeps the library current without a manual "Rescan": watches store folders (Steam/Epic manifests) and rescans
/// when the window comes back to the front (covers stores without files to watch). Changes are debounced
/// (a download rewrites manifests many times), automatic scans are spaced out, and they never hit the network
/// for data that is cached (e.g. Steam's owned list). Only reads; never writes to store folders.
/// </summary>
public sealed partial class LibraryAutoRefresh : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultMinimumGap = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _debounce;
    private readonly TimeSpan _minimumGap;

    private readonly LibraryService _library;
    private readonly ILogger<LibraryAutoRefresh> _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private readonly object _gate = new();
    private DateTimeOffset _lastScan = DateTimeOffset.MinValue;
    private bool _scanning;
    private bool _pending;
    private bool _disposed;

    public LibraryAutoRefresh(LibraryService library, ILogger<LibraryAutoRefresh> logger, TimeSpan? debounce = null, TimeSpan? minimumGap = null)
    {
        _library = library;
        _logger = logger;
        _debounce = debounce ?? DefaultDebounce;
        _minimumGap = minimumGap ?? DefaultMinimumGap;
        _timer = new Timer(_ => _ = RunAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Starts watching the folders the providers report (call after the first scan).</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        IReadOnlyList<WatchTarget> targets;
        try
        {
            targets = await _library.GetWatchTargetsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWatchFailed(ex, "(all)");
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var target in targets)
            {
                try
                {
                    var watcher = new FileSystemWatcher(target.Directory, target.Filter)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        IncludeSubdirectories = false,
                    };
                    watcher.Created += OnFileEvent;
                    watcher.Changed += OnFileEvent;
                    watcher.Deleted += OnFileEvent;
                    watcher.Renamed += OnFileEvent;
                    watcher.Error += (_, e) => LogWatchFailed(e.GetException(), target.Directory);
                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    LogWatchFailed(ex, target.Directory);
                }
            }
        }

        LogWatching(_watchers.Count);
    }

    /// <summary>The main window came to the front: the user may have installed something meanwhile.</summary>
    public void OnWindowActivated() => Request(TimeSpan.FromMilliseconds(300));

    private void OnFileEvent(object sender, FileSystemEventArgs e) => Request(_debounce);

    private void Request(TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Respect the gap between automatic scans: wait until it has passed.
            var sinceLast = _lastScan == DateTimeOffset.MinValue ? TimeSpan.MaxValue : DateTimeOffset.UtcNow - _lastScan;
            var wait = sinceLast < _minimumGap ? MaxOf(delay, _minimumGap - sinceLast) : delay;
            _timer.Change(wait, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_scanning)
            {
                _pending = true; // a change arrived during a scan: run once more afterwards
                return;
            }

            _scanning = true;
        }

        try
        {
            await _library.ScanAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogScanFailed(ex);
        }
        finally
        {
            bool again;
            lock (_gate)
            {
                _scanning = false;
                _lastScan = DateTimeOffset.UtcNow;
                again = _pending;
                _pending = false;
            }

            if (again)
            {
                Request(_debounce);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        _timer.Dispose();
    }

    private static TimeSpan MaxOf(TimeSpan a, TimeSpan b) => a > b ? a : b;

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching {Count} store folders for installs and uninstalls")]
    private partial void LogWatching(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not watch {Directory}")]
    private partial void LogWatchFailed(Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automatic library rescan failed")]
    private partial void LogScanFailed(Exception exception);
}
