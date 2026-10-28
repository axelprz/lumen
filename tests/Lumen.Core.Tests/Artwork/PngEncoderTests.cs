// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Axel Ismael Perez (AxelPRZ)

using System.Buffers.Binary;
using System.IO.Compression;
using Lumen.Core.Artwork;

namespace Lumen.Core.Tests.Artwork;

public sealed class PngEncoderTests
{
    [Fact]
    public void Writes_AValidRgbaPng_ThatRoundTrips()
    {
        // 2×2: blue, green / red, transparent (BGRA in, RGBA out)
        byte[] bgra = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0];

        var png = PngEncoder.EncodeBgra(bgra, 2, 2);

        ArtworkService.DetectImageExtension(png).ShouldBe(".png");
        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)).ShouldBe(2); // IHDR width
        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)).ShouldBe(2); // IHDR height
        png[25].ShouldBe((byte)6); // color type RGBA

        Inflate(Chunk(png, "IDAT")).ShouldBe([0, 0, 0, 255, 255, 0, 255, 0, 255, 0, 255, 0, 0, 255, 0, 0, 0, 0]);
    }

    [Fact]
    public void ChunkChecksums_AreCorrect()
    {
        var png = PngEncoder.EncodeBgra(new byte[4 * 3 * 3], 3, 3);

        // IEND always carries the same CRC.
        png.AsSpan(png.Length - 4).ToArray().ShouldBe(new byte[] { 0xAE, 0x42, 0x60, 0x82 });
    }

    [Fact]
    public void WrongBufferSize_Throws()
    {
        Should.Throw<ArgumentException>(() => PngEncoder.EncodeBgra(new byte[10], 2, 2));
    }

    private static byte[] Chunk(byte[] png, string type)
    {
        var at = 8;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var name = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            if (name == type)
            {
                return png.AsSpan(at + 8, length).ToArray();
            }

            at += 12 + length;
        }

        throw new InvalidOperationException($"No {type} chunk.");
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
