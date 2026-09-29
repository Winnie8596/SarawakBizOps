using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SarawakBizOps.Api.Data;

/// <summary>
/// A tiny, dependency-free PNG writer for the generated demo images (the Phase 8 signature scrawl and the
/// Phase 9 job photos), so the seed ships no image files and adds no imaging library (PRD D-06).
/// </summary>
internal static class DemoPng
{
    public const byte Greyscale = 0;
    public const byte Rgb = 2;

    /// <summary>
    /// Encodes 8-bit <paramref name="pixels"/> (row-major; 1 byte per pixel for greyscale, 3 for RGB) as a PNG.
    /// </summary>
    public static byte[] Encode(int width, int height, byte colourType, byte[] pixels)
    {
        var bytesPerPixel = colourType == Rgb ? 3 : 1;
        var stride = width * bytesPerPixel;

        // Each scanline is prefixed with filter type 0 (None).
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(pixels, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; // bit depth
        header[9] = colourType;
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
