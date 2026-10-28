// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Security.Cryptography;
using System.Text;

namespace Lumen.Platform.Windows.Windowing;

/// <summary>
/// One Lumen per Windows session and install folder. Lumen keeps running in the tray, so opening it again must bring
/// the running one back instead of starting a second copy over the same database. A copy started from another folder
/// (a portable copy, a development build) is a separate instance.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showSignal;
    private readonly RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showSignal, Action onShowRequested)
    {
        _mutex = mutex;
        _showSignal = showSignal;
        _registration = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// Becomes the running instance, or (when one is already running) asks it to show its window and returns null.
    /// </summary>
    /// <param name="onShowRequested">Called on a worker thread when another launch asks this instance to show itself.</param>
    public static SingleInstance? TryAcquire(string installFolder, Action onShowRequested)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installFolder);
        ArgumentNullException.ThrowIfNull(onShowRequested);

        var id = Id(installFolder);
        var mutex = new Mutex(initiallyOwned: false, $@"Local\Lumen-{id}", out var created);
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\Lumen-{id}-show");
        if (created)
        {
            return new SingleInstance(mutex, signal, onShowRequested);
        }

        signal.Set();
        signal.Dispose();
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showSignal.Dispose();
        _mutex.Dispose(); // the name is freed when the last handle closes; ownership is never taken
    }

    // Names are per install folder, hashed so the path itself never appears in a kernel object name.
    private static string Id(string installFolder)
    {
        var normalized = Path.GetFullPath(installFolder).TrimEnd('\\', '/').ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }
}
