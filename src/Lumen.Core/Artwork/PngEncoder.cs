// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Lumen.Core.Artwork;

/// <summary>
/// Minimal PNG writer for 32-bit images (icons extracted from programs). Enough for Lumen's own artwork cache;
/// avoids a dependency on an imaging library. Pure.
/// </summary>
public static class PngEncoder
{
    private static readonly uint[] _crcTable = BuildCrcTable();

    /// <param name="bgra">Pixels top-down, 4 bytes each in B, G, R, A order (what Windows bitmaps use).</param>
    public static byte[] EncodeBgra(ReadOnlySpan<byte> bgra, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (bgra.Length != width * height * 4)
        {
            throw new ArgumentException($"Expected {width * height * 4} bytes for {width}×{height}, got {bgra.Length}.", nameof(bgra));
        }

        // Each row: filter byte 0 (none), then RGBA.
        var raw = new byte[height * ((width * 4) + 1)];
        var o = 0;
        for (var y = 0; y < height; y++)
        {
            raw[o++] = 0;
            var row = bgra.Slice(y * width * 4, width * 4);
            for (var x = 0; x < width * 4; x += 4)
            {
                raw[o++] = row[x + 2];
                raw[o++] = row[x + 1];
                raw[o++] = row[x];
                raw[o++] = row[x + 3];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // color type: RGBA
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
