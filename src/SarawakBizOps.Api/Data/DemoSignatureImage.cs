using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// Draws a fictional, signature-like scrawl as a small greyscale PNG for the demo seed (Phase 8), so seeded
/// Completed/Approved jobs carry a signature exactly as BR-03 requires — without shipping image files or
/// adding an imaging dependency. Deterministic per seed text; no real person's signature.
/// </summary>
public static class DemoSignatureImage
{
    private const int Width = 480;
    private const int Height = 160;

    public static byte[] Create(string seedText)
    {
        var pixels = new byte[Width * Height];
        Array.Fill(pixels, (byte)0xFF);

        // A few looping strokes whose shape comes from the seed text, left to right like handwriting.
        var seed = seedText.Aggregate(17, (h, c) => unchecked(h * 31 + c));
        var random = new Random(seed);
        var loops = 5 + random.Next(4);
        var amplitude = 28 + random.Next(18);
        double? prevX = null, prevY = null;
        for (var i = 0; i <= 900; i++)
        {
            var t = i / 900.0;
            var x = 30 + t * (Width - 80) + 14 * Math.Sin(t * loops * 2 * Math.PI);
            var y = Height / 2.0 + amplitude * Math.Sin(t * loops * Math.PI + seed % 7) * (1 - 0.4 * t)
                    + 10 * Math.Cos(t * loops * 4 * Math.PI);
            if (prevX is not null)
            {
                Line(pixels, prevX.Value, prevY!.Value, x, y);
            }
            (prevX, prevY) = (x, y);
        }

        // An underline flourish.
        Line(pixels, 40, Height - 30, Width - 60, Height - 38);
        return EncodePng(pixels);
    }

    private static void Line(byte[] pixels, double x0, double y0, double x1, double y1)
    {
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))) + 1;
        for (var s = 0; s <= steps; s++)
        {
            var x = (int)Math.Round(x0 + (x1 - x0) * s / steps);
            var y = (int)Math.Round(y0 + (y1 - y0) * s / steps);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var px = x + dx;
                    var py = y + dy;
                    if (px >= 0 && px < Width && py >= 0 && py < Height)
                    {
                        pixels[py * Width + px] = 0x1A;
                    }
                }
            }
        }
    }

    private static byte[] EncodePng(byte[] pixels)
    {
        // Each scanline is prefixed with filter type 0 (None).
        var raw = new byte[(Width + 1) * Height];
        for (var y = 0; y < Height; y++)
        {
            Buffer.BlockCopy(pixels, y * Width, raw, y * (Width + 1) + 1, Width);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8; // bit depth
        header[9] = 0; // colour type: greyscale
        // compression, filter and interlace methods all 0

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        png.Write(length);

        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        png.Write(typeAndData);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        png.Write(crc);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
        {
            c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c;
    }).ToArray();

    private static uint Crc32(byte[] bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFFu;
    }
}
