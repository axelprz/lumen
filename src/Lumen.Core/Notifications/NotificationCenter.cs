// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

namespace Lumen.Core.Notifications;

public enum NotificationKind
{
    /// <summary>A new version is available or ready to install.</summary>
    Update,

    /// <summary>A store could not be read completely in the last scan.</summary>
    StoreProblem,

    /// <summary>Something arrived from the sync folder.</summary>
    Sync,

    /// <summary>A game or app could not be started.</summary>
    Launch,
}

/// <summary>
/// One entry of the bell. Texts are functions so they are written in the current language each time they are read
/// (nothing translated is stored).
/// </summary>
/// <param name="Key">Identity: posting the same key again replaces the entry (one "update", one problem per store).</param>
public sealed record Notification(string Key, NotificationKind Kind, Func<string> Title, Func<string> Message, DateTimeOffset At);

/// <summary>
/// What the notification center (the bell in the top bar) lists, newest first. Kept in memory only (owner,
/// 2026-11-02): nothing is saved, so a restart starts empty and real problems show up again by themselves.
/// Safe to use from any thread; <see cref="Changed"/> may be raised on any thread.
/// </summary>
public sealed class NotificationCenter(TimeProvider? time = null)
{
    /// <summary>Oldest entries beyond this are dropped.</summary>
    public const int MaxItems = 50;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private IReadOnlyList<Notification> _items = [];

    public IReadOnlyList<Notification> Items => Volatile.Read(ref _items);

    public event EventHandler? Changed;

    /// <summary>Adds an entry at the top, replacing any entry with the same key.</summary>
    public void Post(string key, NotificationKind kind, Func<string> title, Func<string> message)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        var entry = new Notification(key, kind, title, message, _time.GetUtcNow());
        lock (_gate)
        {
            _items = new[] { entry }.Concat(_items.Where(n => n.Key != key)).Take(MaxItems).ToList();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Contains(string key) => Items.Any(n => n.Key == key);

    /// <summary>Removes the entry with this key (the user dismissed it, or the problem is gone). False when there was none.</summary>
    public bool Remove(string key)
    {
        lock (_gate)
        {
            if (_items.All(n => n.Key != key))
            {
                return false;
            }

            _items = _items.Where(n => n.Key != key).ToList();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items = [];
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
