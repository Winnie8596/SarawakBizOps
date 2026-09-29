using System.Buffers.Binary;
using System.IO.Compression;

namespace SarawakBizOps.Api.Services.Reports.Pdf;

/// <summary>
/// A raster ready to be placed in a PDF as an image XObject, built from the job's own files without an imaging
/// library (PRD D-06):
/// <list type="bullet">
/// <item>a <b>JPEG</b> is embedded as-is (PDF's DCTDecode filter is JPEG), after reading its size, colour
/// components and EXIF orientation from the headers;</item>
/// <item>a <b>PNG</b> is decoded row by row (inflate + un-filter), composited onto white and box-averaged down to
/// thumbnail size while it streams, so a large photo never has to fit in memory at full size;</item>
/// <item>anything else (WebP, CMYK or arithmetic-coded JPEG, interlaced PNG, or a damaged file) yields null and
/// the report shows a placeholder instead — an odd photo never stops a report from being generated.</item>
/// </list>
/// </summary>
public sealed class PdfImage
{
    /// <summary>Refuses to decode a PNG larger than this (a decompression bomb would otherwise cost CPU).</summary>
    public const long MaxPngPixels = 50_000_000;

    public int Width { get; }
    public int Height { get; }
    public string ColorSpace { get; }
    public string Filter { get; }
    public byte[] Data { get; }

    /// <summary>EXIF orientation 1–8 (1 = as stored). Applied when the image is placed, not by re-encoding.</summary>
    public int Orientation { get; }

    /// <summary>Width over height as the image should be seen, i.e. after <see cref="Orientation"/>.</summary>
    public double DisplayAspect => Orientation >= 5 ? (double)Height / Width : (double)Width / Height;

    private PdfImage(int width, int height, string colorSpace, string filter, byte[] data, int orientation = 1)
    {
        Width = width;
        Height = height;
        ColorSpace = colorSpace;
        Filter = filter;
        Data = data;
        Orientation = orientation is >= 1 and <= 8 ? orientation : 1;
    }

    /// <summary>
    /// The unit-square transform (a b c d e f) that shows the stored image upright for its EXIF orientation;
    /// the page scales it to the target box.
    /// </summary>
    public (double A, double B, double C, double D, double E, double F) OrientationMatrix => Orientation switch
    {
        2 => (-1, 0, 0, 1, 1, 0),   // mirrored horizontally
        3 => (-1, 0, 0, -1, 1, 1),  // rotated 180°
        4 => (1, 0, 0, -1, 0, 1),   // mirrored vertically
        5 => (0, -1, -1, 0, 1, 1),  // transposed
        6 => (0, -1, 1, 0, 0, 1),   // rotated 90° clockwise
        7 => (0, 1, 1, 0, 0, 0),    // transversed
        8 => (0, 1, -1, 0, 1, 0),   // rotated 90° anticlockwise
        _ => (1, 0, 0, 1, 0, 0)
    };

    // ---------------------------------------------------------------- JPEG

    /// <summary>The JPEG as a PDF image, or null when a PDF viewer could not be relied on to show it.</summary>
    public static PdfImage? FromJpeg(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        var orientation = 1;
        var i = 2;
        while (i + 4 <= jpeg.Length)
        {
            if (jpeg[i] != 0xFF)
            {
                return null;
            }

            var marker = jpeg[i + 1];
            if (marker == 0xFF)
            {
                i++; // fill byte
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                i += 2; // markers without a length
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(i + 2));
            if (length < 2 || i + 2 + length > jpeg.Length)
            {
                return null;
            }

            var segment = jpeg.AsSpan(i + 4, length - 2);
            if (marker == 0xE1)
            {
                orientation = ExifOrientation(segment) ?? orientation;
            }
            else if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                // Baseline, extended or progressive Huffman — what every camera and browser writes. Arithmetic-coded
                // and lossless variants are not reliably supported by viewers, so they fall through to null.
                if (segment.Length < 6)
                {
                    return null;
                }

                var precision = segment[0];
                var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                var components = segment[5];
                var colorSpace = components switch { 1 => "DeviceGray", 3 => "DeviceRGB", _ => null };
                if (precision != 8 || width == 0 || height == 0 || colorSpace is null)
                {
                    return null;
                }

                return new PdfImage(width, height, colorSpace, "DCTDecode", jpeg, orientation);
            }
            else if (marker is (>= 0xC3 and <= 0xCF) and not 0xC4 and not 0xC8 and not 0xCC || marker == 0xDA)
            {
                return null; // an unsupported frame type, or scan data before any frame header
            }

            i += 2 + length;
        }

        return null;
    }

    // The Orientation tag (0x0112) of IFD0 in an "Exif\0\0" APP1 segment, if present and sane.
    private static int? ExifOrientation(ReadOnlySpan<byte> app1)
    {
        if (app1.Length < 14 || !app1[..6].SequenceEqual("Exif\0\0"u8))
        {
            return null;
        }

        var tiff = app1[6..];
        bool little;
        if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I') little = true;
        else if (tiff[0] == (byte)'M' && tiff[1] == (byte)'M') little = false;
        else return null;

        ushort U16(ReadOnlySpan<byte> s) => little ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s);
        uint U32(ReadOnlySpan<byte> s) => little ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);

        var ifd = U32(tiff[4..]);
        if (ifd > (uint)tiff.Length - 2)
        {
            return null;
        }

        var entries = U16(tiff[(int)ifd..]);
        for (var e = 0; e < entries; e++)
        {
            var at = (int)ifd + 2 + e * 12;
            if (at + 12 > tiff.Length)
            {
                return null;
            }

            if (U16(tiff[at..]) == 0x0112)
            {
                var value = U16(tiff[(at + 8)..]);
                return value is >= 1 and <= 8 ? value : null;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- PNG

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// The PNG decoded and scaled so its longer side is at most <paramref name="maxSide"/> pixels, as a
    /// Flate-compressed greyscale or RGB raster (transparency flattened onto white); null when it cannot be read.
    /// </summary>
    public static PdfImage? FromPng(Stream png, int maxSide)
    {
        try
        {
            return DecodePng(png, maxSide);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException or OverflowException)
        {
            return null; // corrupt or truncated data
        }
    }

    private static PdfImage? DecodePng(Stream png, int maxSide)
    {
        Span<byte> signature = stackalloc byte[8];
        png.ReadExactly(signature);
        if (!signature.SequenceEqual(PngSignature))
        {
            return null;
        }

        int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        using var idat = new MemoryStream();
        var header = new byte[8];

        while (true)
        {
            png.ReadExactly(header);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length < 0)
            {
                return null;
            }

            var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            if (type == "IDAT")
            {
                CopyExactly(png, idat, length);
            }
            else
            {
                var data = new byte[length];
                png.ReadExactly(data);
                switch (type)
                {
                    case "IHDR" when length >= 13:
                        width = BinaryPrimitives.ReadInt32BigEndian(data);
                        height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                        bitDepth = data[8];
                        colorType = data[9];
                        interlace = data[12];
                        break;
                    case "PLTE":
                        palette = data;
                        break;
                    case "tRNS":
                        paletteAlpha = data;
                        break;
                }
            }

            png.ReadExactly(header.AsSpan(0, 4)); // CRC (not checked: a bad one just shows odd pixels)
            if (type == "IEND")
            {
                break;
            }
        }

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        var depthOk = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            _ => bitDepth is 8 or 16
        };
        if (channels == 0 || !depthOk || interlace != 0 || width <= 0 || height <= 0
            || (long)width * height > MaxPngPixels || (colorType == 3 && palette is null))
        {
            return null;
        }

        var grey = colorType is 0 or 4;
        var outChannels = grey ? 1 : 3;
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(width, height));
        var outWidth = Math.Max(1, (int)Math.Round(width * scale));
        var outHeight = Math.Max(1, (int)Math.Round(height * scale));

        var bitsPerPixel = channels * bitDepth;
        var rowBytes = (int)(((long)width * bitsPerPixel + 7) / 8);
        var filterStep = Math.Max(1, bitsPerPixel / 8);

        var previous = new byte[rowBytes];
        var current = new byte[rowBytes];
        var sums = new long[outWidth * outChannels];
        var counts = new int[outWidth];
        var output = new byte[outWidth * outHeight * outChannels];
        var pixel = new byte[4];
        var outRow = 0;

        idat.Position = 0;
        using var inflater = new ZLibStream(idat, CompressionMode.Decompress);
        for (var y = 0; y < height; y++)
        {
            var filter = inflater.ReadByte();
            if (filter < 0)
            {
                throw new EndOfStreamException();
            }

            inflater.ReadExactly(current);
            Unfilter(filter, current, previous, filterStep);

            var targetRow = (int)((long)y * outHeight / height);
            if (targetRow != outRow)
            {
                FlushRow(sums, counts, output, outRow, outWidth, outChannels);
                outRow = targetRow;
            }

            for (var x = 0; x < width; x++)
            {
                ReadPixel(current, x, colorType, bitDepth, channels, palette, paletteAlpha, pixel);
                var ox = (int)((long)x * outWidth / width);
                var slot = ox * outChannels;
                for (var c = 0; c < outChannels; c++)
                {
                    sums[slot + c] += pixel[c];
                }
                counts[ox]++;
            }

            (previous, current) = (current, previous);
        }

        FlushRow(sums, counts, output, outRow, outWidth, outChannels);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(output);
        }

        return new PdfImage(outWidth, outHeight, grey ? "DeviceGray" : "DeviceRGB", "FlateDecode", compressed.ToArray());
    }

    private static void CopyExactly(Stream from, Stream to, int count)
    {
        var buffer = new byte[Math.Min(count, 81920)];
        while (count > 0)
        {
            var read = from.Read(buffer, 0, Math.Min(buffer.Length, count));
            if (read == 0)
            {
                throw new EndOfStreamException();
            }
            to.Write(buffer, 0, read);
            count -= read;
        }
    }

    // PNG filter types 0–4 (the spec's "reconstruction" functions).
    private static void Unfilter(int filter, byte[] row, byte[] previous, int step)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (var i = step; i < row.Length; i++) row[i] += row[i - step];
                return;
            case 2:
                for (var i = 0; i < row.Length; i++) row[i] += previous[i];
                return;
            case 3:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= step ? row[i - step] : 0;
                    row[i] += (byte)((left + previous[i]) / 2);
                }
                return;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    int a = i >= step ? row[i - step] : 0, b = previous[i], c = i >= step ? previous[i - step] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    row[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                }
                return;
            default:
                throw new InvalidDataException($"Unknown PNG filter type {filter}.");
        }
    }

    // One pixel as 8-bit grey (pixel[0]) or RGB (pixel[0..2]), with any alpha composited onto white.
    private static void ReadPixel(
        byte[] row, int x, int colorType, int bitDepth, int channels, byte[]? palette, byte[]? paletteAlpha, byte[] pixel)
    {
        int Sample(int channel)
        {
            var index = x * channels + channel;
            return bitDepth switch
            {
                8 => row[index],
                16 => row[index * 2], // the high byte is plenty for a thumbnail
                _ => ((row[index * bitDepth / 8] >> (8 - bitDepth - index * bitDepth % 8)) & ((1 << bitDepth) - 1))
                     * 255 / ((1 << bitDepth) - 1)
            };
        }

        static byte Over(int value, int alpha) => (byte)((value * alpha + 255 * (255 - alpha) + 127) / 255);

        switch (colorType)
        {
            case 0:
                pixel[0] = (byte)Sample(0);
                break;
            case 4:
                pixel[0] = Over(Sample(0), Sample(1));
                break;
            case 2:
                pixel[0] = (byte)Sample(0);
                pixel[1] = (byte)Sample(1);
                pixel[2] = (byte)Sample(2);
                break;
            case 6:
                var a = Sample(3);
                pixel[0] = Over(Sample(0), a);
                pixel[1] = Over(Sample(1), a);
                pixel[2] = Over(Sample(2), a);
                break;
            case 3:
                // Sample() scaled sub-byte values to 0–255, so the raw index is read directly here.
                var i = x * bitDepth;
                var index = bitDepth == 8 ? row[x] : (row[i / 8] >> (8 - bitDepth - i % 8)) & ((1 << bitDepth) - 1);
                var entry = index * 3;
                if (entry + 2 >= palette!.Length)
                {
                    throw new InvalidDataException("PNG palette index out of range.");
                }
                var alpha = paletteAlpha is not null && index < paletteAlpha.Length ? paletteAlpha[index] : 255;
                pixel[0] = Over(palette[entry], alpha);
                pixel[1] = Over(palette[entry + 1], alpha);
                pixel[2] = Over(palette[entry + 2], alpha);
                break;
        }
    }

    private static void FlushRow(long[] sums, int[] counts, byte[] output, int outRow, int outWidth, int outChannels)
    {
        var rowStart = outRow * outWidth * outChannels;
        for (var ox = 0; ox < outWidth; ox++)
        {
            var count = Math.Max(1, counts[ox]);
            for (var c = 0; c < outChannels; c++)
            {
                output[rowStart + ox * outChannels + c] = (byte)(sums[ox * outChannels + c] / count);
            }
            counts[ox] = 0;
        }
        Array.Clear(sums);
    }
}
