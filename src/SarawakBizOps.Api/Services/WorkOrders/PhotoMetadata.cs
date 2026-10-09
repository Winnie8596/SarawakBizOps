using System.Buffers.Binary;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.WorkOrders;

/// <summary>
/// Removes the metadata a phone writes into a photo before it is stored: EXIF (GPS position, time, camera and
/// owner details, a thumbnail), XMP, IPTC, comments, and anything after the image itself (the extra pictures of a
/// multi-picture file). Only the EXIF orientation survives, in a minimal EXIF block of its own, so a portrait
/// photo still shows upright in the app and in the report. The pixels are never decoded or re-encoded: the image
/// data is copied byte for byte, so nothing is lost and no imaging library is needed (PRD D-06).
/// <para>
/// Kept, because they change how the image looks rather than describe it: the JFIF header, the ICC colour
/// profile and Adobe's colour-transform marker in a JPEG; colour, gamma, transparency and density chunks in a PNG.
/// </para>
/// </summary>
public static class PhotoMetadata
{
    /// <summary>
    /// <paramref name="image"/> (of the type <see cref="PhotoRules.DetectContentType"/> found) without its
    /// metadata; null when its structure cannot be followed, so it cannot be cleaned and is refused.
    /// </summary>
    public static byte[]? Strip(byte[] image, string contentType) => contentType switch
    {
        PhotoRules.Jpeg => StripJpeg(image),
        PhotoRules.Png => StripPng(image),
        _ => null
    };

    // ---------------------------------------------------------------- JPEG

    private static byte[]? StripJpeg(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        using var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg, 0, 2); // SOI

        var at = 2;
        while (at < jpeg.Length)
        {
            if (jpeg[at] != 0xFF)
            {
                return null;
            }

            if (at + 1 >= jpeg.Length)
            {
                break; // a lone fill byte at the very end
            }

            var marker = jpeg[at + 1];
            if (marker == 0xFF)
            {
                at++; // fill byte before a marker
                continue;
            }

            if (marker == 0xD9)
            {
                output.Write(jpeg, at, 2); // EOI: the image ends here; whatever follows is dropped
                return output.ToArray();
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Write(jpeg, at, 2); // markers without a length
                at += 2;
                continue;
            }

            if (at + 4 > jpeg.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at + 2));
            if (length < 2 || at + 2 + length > jpeg.Length)
            {
                return null;
            }

            var segment = jpeg.AsSpan(at + 4, length - 2);
            if (marker == 0xE1 && segment.StartsWith("Exif\0\0"u8))
            {
                if (ExifOrientation(segment) is { } orientation and not 1)
                {
                    WriteSegment(output, 0xE1, OrientationOnlyExif(orientation));
                }
            }
            else if (KeepJpegSegment(marker, segment))
            {
                output.Write(jpeg, at, 2 + length);
            }

            at += 2 + length;

            if (marker == 0xDA)
            {
                // Entropy-coded data follows the scan header up to the next marker that is not a stuffed 0xFF00
                // or a restart marker. It is the picture itself: copied as it is.
                var start = at;
                while (at + 1 < jpeg.Length && !(jpeg[at] == 0xFF && jpeg[at + 1] is not 0x00 and not (>= 0xD0 and <= 0xD7)))
                {
                    at++;
                }

                if (at + 1 >= jpeg.Length)
                {
                    at = jpeg.Length; // no EOI: keep the data to the end, as a viewer would show it
                }
                output.Write(jpeg, start, at - start);
            }
        }

        return output.ToArray();
    }

    private static bool KeepJpegSegment(byte marker, ReadOnlySpan<byte> segment) => marker switch
    {
        0xE0 => segment.StartsWith("JFIF\0"u8),          // APP0 JFIF (not JFXX, which is a thumbnail)
        0xE2 => segment.StartsWith("ICC_PROFILE\0"u8),   // APP2 colour profile (not MPF or FlashPix)
        0xEE => segment.StartsWith("Adobe"u8),           // APP14 colour transform
        >= 0xE0 and <= 0xEF => false,                    // every other APPn: XMP, IPTC, maker data, ...
        0xFE => false,                                   // COM
        _ => true                                        // frame, tables, scan: the image itself
    };

    private static void WriteSegment(Stream output, byte marker, byte[] data)
    {
        Span<byte> header = stackalloc byte[4];
        header[0] = 0xFF;
        header[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(data.Length + 2));
        output.Write(header);
        output.Write(data);
    }

    /// <summary>"Exif\0\0" and a big-endian TIFF header whose only entry is the Orientation tag.</summary>
    private static byte[] OrientationOnlyExif(int orientation) => "Exif\0\0"u8.ToArray().Concat(OrientationTiff(orientation)).ToArray();

    private static byte[] OrientationTiff(int orientation)
    {
        var tiff = new byte[8 + 2 + 12 + 4];
        tiff[0] = tiff[1] = (byte)'M';
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(4), 8);       // IFD0 right after the header
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(8), 1);       // one entry
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(10), 0x0112); // Orientation
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(12), 3);      // SHORT
        BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(14), 1);      // one value, held in the entry
        BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(18), (ushort)orientation);
        return tiff;                                                     // next IFD offset 0: no thumbnail IFD
    }

    /// <summary>The Orientation (1–8) in IFD0 of an "Exif\0\0" block, or null when absent or out of range.</summary>
    public static int? ExifOrientation(ReadOnlySpan<byte> exif)
        => exif.StartsWith("Exif\0\0"u8) ? TiffOrientation(exif[6..]) : null;

    private static int? TiffOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
        {
            return null;
        }

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

    // Text (which phones and editors fill with author, software and location), the modification time and EXIF.
    private static readonly HashSet<string> PngMetadataChunks = new(StringComparer.Ordinal) { "tEXt", "zTXt", "iTXt", "tIME", "eXIf" };

    private static byte[]? StripPng(byte[] png)
    {
        if (!png.AsSpan().StartsWith(PngSignature))
        {
            return null;
        }

        using var output = new MemoryStream(png.Length);
        output.Write(PngSignature);

        var at = PngSignature.Length;
        while (at + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (length < 0 || at + 12L + length > png.Length)
            {
                return null;
            }

            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            if (type == "eXIf")
            {
                if (TiffOrientation(png.AsSpan(at + 8, length)) is { } orientation and not 1)
                {
                    PngChunks.Write(output, "eXIf", OrientationTiff(orientation));
                }
            }
            else if (!PngMetadataChunks.Contains(type))
            {
                output.Write(png, at, 12 + length);
            }

            at += 12 + length;
            if (type == "IEND")
            {
                return output.ToArray(); // anything after IEND is dropped
            }
        }

        return null; // no IEND: a truncated file
    }

}
