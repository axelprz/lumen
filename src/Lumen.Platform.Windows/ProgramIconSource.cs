// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Runtime.InteropServices;
using Lumen.Core.Artwork;
using Lumen.Core.Library;
using Lumen.Core.Platform;

namespace Lumen.Platform.Windows;

/// <summary>
/// Icons for Start menu apps and manual items: the program's own icon at 256 px, as Windows shows it. Windows reads
/// the icon from the file as data (<c>PrivateExtractIcons</c>); nothing is run and no shell extension is loaded. The
/// result is saved as PNG in Lumen's artwork cache.
/// </summary>
public sealed partial class ProgramIconSource(IShortcutReader shortcuts, IFileSystem fileSystem) : ILocalArtworkSource
{
    private const int Size = 256;
    private static readonly HashSet<string> IconFiles = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll", ".ico" };

    public bool CanHandle(LibraryEntry entry, ArtworkKind kind) =>
        kind == ArtworkKind.Icon && entry.Ref.Source is "startmenu" or ManualItem.Source;

    public Task<byte[]?> GetImageAsync(LibraryEntry entry, ArtworkKind kind, CancellationToken ct) =>
        Task.Run(() => IconLocation(entry) is var (path, index) ? Extract(path, index) : null, ct);

    /// <summary>Where the icon is: the shortcut's own icon, else its target; a manual .exe is its own icon.</summary>
    private (string Path, int Index)? IconLocation(LibraryEntry entry)
    {
        string? icon = entry.Extra.GetValueOrDefault("icon");
        var index = int.TryParse(entry.Extra.GetValueOrDefault("iconIndex"), out var i) ? i : 0;
        var target = entry.Extra.GetValueOrDefault("target");

        if (entry.Ref.Source == ManualItem.Source && entry.Launch is { Kind: LaunchKind.Executable } launch)
        {
            if (launch.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && shortcuts.Read(launch.Target) is { } link)
            {
                (icon, index, target) = (link.IconPath, link.IconIndex, link.TargetPath);
            }
            else
            {
                target = launch.Target;
            }
        }

        foreach (var (path, at) in new[] { (icon, index), (target, 0) })
        {
            if (path is not null && IconFiles.Contains(Path.GetExtension(path)) && fileSystem.FileExists(path))
            {
                return (path, at);
            }
        }

        return null;
    }

    private static unsafe byte[]? Extract(string path, int index)
    {
        IntPtr icon;
        int id;
        if (PrivateExtractIconsW(path, index, Size, Size, &icon, &id, 1, 0) != 1 || icon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!GetIconInfo(icon, out var info))
            {
                return null;
            }

            try
            {
                return info.Color == IntPtr.Zero ? null : ToPng(info);
            }
            finally
            {
                DeleteObject(info.Mask);
                if (info.Color != IntPtr.Zero)
                {
                    DeleteObject(info.Color);
                }
            }
        }
        finally
        {
            DestroyIcon(icon);
        }
    }

    private static unsafe byte[]? ToPng(IconInfo info)
    {
        Bitmap bitmap;
        if (GetObjectW(info.Color, sizeof(Bitmap), &bitmap) == 0 || bitmap.Width < 16 || bitmap.Height < 16)
        {
            return null;
        }

        var (width, height) = (bitmap.Width, bitmap.Height);
        var pixels = ReadBgra(info.Color, width, height);
        if (pixels is null)
        {
            return null;
        }

        // Old icons have no alpha channel: their transparency is in the mask (white = transparent).
        var hasAlpha = false;
        for (var p = 3; p < pixels.Length && !hasAlpha; p += 4)
        {
            hasAlpha = pixels[p] != 0;
        }

        if (!hasAlpha)
        {
            var mask = ReadBgra(info.Mask, width, height);
            for (var p = 0; p < pixels.Length; p += 4)
            {
                pixels[p + 3] = mask is not null && mask[p] != 0 ? (byte)0 : (byte)255;
            }
        }

        return PngEncoder.EncodeBgra(pixels, width, height);
    }

    private static unsafe byte[]? ReadBgra(IntPtr bitmap, int width, int height)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var screen = GetDC(IntPtr.Zero);
        try
        {
            fixed (byte* buffer = pixels)
            {
                return GetDIBits(screen, bitmap, 0, (uint)height, buffer, &header, 0) == height ? pixels : null;
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, IntPtr* icons, int* ids, uint count, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(IntPtr icon, out IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetObjectW(IntPtr handle, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte* bits, BitmapInfoHeader* info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr window, IntPtr dc);
}
