// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.IO;
using System.Windows.Media.Imaging;

namespace Lumen.App.Services;

/// <summary>
/// Decoded tile images (covers, icons) kept in memory so scrolling back over them costs nothing. Decoding a cover
/// takes ~5 ms, so it runs off the UI thread; the most recently used images stay, up to <see cref="BudgetBytes"/>.
/// Thread-safe. Images are frozen, so any thread can hand them to the UI.
/// </summary>
public sealed class ImageCache
{
    /// <summary>About 250 covers at 1.5× display scaling.</summary>
    public const long BudgetBytes = 96L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<Entry> _recent = new();
    private readonly Dictionary<string, Task<BitmapSource?>> _loading = new(StringComparer.OrdinalIgnoreCase);
    private long _bytes;

    /// <summary>Display scaling (1 = 96 DPI); images are decoded at their on-screen pixel size. Set by the main window.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>The decoded image if it is in memory; never blocks.</summary>
    public BitmapSource? TryGet(string path, double width)
    {
        var key = Key(path, width);
        lock (_gate)
        {
            if (!_byKey.TryGetValue(key, out var node))
            {
                return null;
            }

            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Image;
        }
    }

    /// <summary>Decodes on a worker thread (once per image, even if many ask) and keeps the result.</summary>
    public Task<BitmapSource?> LoadAsync(string path, double width)
    {
        var key = Key(path, width);
        lock (_gate)
        {
            if (_byKey.TryGetValue(key, out var node))
            {
                return Task.FromResult<BitmapSource?>(node.Value.Image);
            }

            if (_loading.TryGetValue(key, out var pending))
            {
                return pending;
            }

            var pixels = (int)Math.Ceiling(width * Math.Max(1, Scale));
            var task = Task.Run(() => Decode(path, pixels)).ContinueWith(
                t =>
                {
                    lock (_gate)
                    {
                        _loading.Remove(key);
                        if (t.Result is { } image)
                        {
                            Add(key, image);
                        }
                    }

                    return t.Result;
                },
                TaskScheduler.Default);
            _loading[key] = task;
            return task;
        }
    }

    private void Add(string key, BitmapSource image)
    {
        var size = (long)image.PixelWidth * image.PixelHeight * 4;
        _byKey[key] = _recent.AddFirst(new Entry(key, image, size));
        _bytes += size;
        while (_bytes > BudgetBytes && _recent.Last is { } oldest && oldest != _recent.First)
        {
            _recent.RemoveLast();
            _byKey.Remove(oldest.Value.Key);
            _bytes -= oldest.Value.Size;
        }
    }

    private static string Key(string path, double width) => path + "|" + width.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static BitmapSource? Decode(string path, int pixelWidth)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad; // read now, don't keep the file locked
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = pixelWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record Entry(string Key, BitmapSource Image, long Size);
}
