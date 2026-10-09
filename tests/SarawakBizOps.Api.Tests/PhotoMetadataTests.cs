using System.Buffers.Binary;
using System.Text;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Services.Common;
using SarawakBizOps.Api.Services.Reports;
using SarawakBizOps.Api.Services.Reports.Pdf;
using SarawakBizOps.Api.Services.WorkOrders;

namespace SarawakBizOps.Api.Tests;

/// <summary>6.11: what is left of a photo's metadata once it is stored — the orientation, and nothing else.</summary>
public class PhotoMetadataTests
{
    // Strings that must not survive: they stand for a GPS position, the camera and the owner.
    internal static readonly string[] Secrets = { "GPS 1.5535N 110.3593E", "SecretCam X100", "Owner: Ali", "Kuching site 7" };

    private static readonly byte[] ScanData =
        { 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56, 0xFF, 0x00, 0x78 }; // a stuffed 0xFF00 and a restart marker

    /// <summary>A phone-style JPEG: JFIF, EXIF with orientation and personal tags, XMP, an ICC profile, IPTC,
    /// a comment, the image itself, and a second picture appended after EOI (as multi-picture files do).</summary>
    internal static byte[] PhoneJpeg(int orientation, bool littleEndian = false)
    {
        using var jpeg = new MemoryStream();
        jpeg.Write(new byte[] { 0xFF, 0xD8 });
        Segment(jpeg, 0xE0, "JFIF\0"u8.ToArray().Concat(new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 }).ToArray());
        Segment(jpeg, 0xE1, Exif(orientation, littleEndian, "SecretCam X100", "GPS 1.5535N 110.3593E"));
        Segment(jpeg, 0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>Kuching site 7</x:xmpmeta>"));
        Segment(jpeg, 0xE2, Encoding.ASCII.GetBytes("ICC_PROFILE\0\u0001\u0001colour-profile-bytes"));
        Segment(jpeg, 0xED, Encoding.ASCII.GetBytes("Photoshop 3.0\0Owner: Ali"));
        Segment(jpeg, 0xEE, Encoding.ASCII.GetBytes("Adobe\0d\0\0\0\0\u0001"));
        Segment(jpeg, 0xFE, Encoding.ASCII.GetBytes("Owner: Ali"));
        Segment(jpeg, 0xDB, new byte[65]);                                     // quantisation table
        Segment(jpeg, 0xC0, new byte[] { 8, 0, 30, 0, 40, 3, 1, 0x11, 0, 2, 0x11, 0, 3, 0x11, 0 });
        Segment(jpeg, 0xC4, new byte[20]);                                     // Huffman table
        Segment(jpeg, 0xDA, new byte[] { 3, 1, 0, 2, 0x11, 3, 0x11, 0, 63, 0 }); // scan header
        jpeg.Write(ScanData);
        jpeg.Write(new byte[] { 0xFF, 0xD9 });
        jpeg.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 });                     // the appended second picture
        jpeg.Write(Encoding.ASCII.GetBytes("GPS 1.5535N 110.3593E"));
        return jpeg.ToArray();
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(8, true)]
    public void A_jpeg_keeps_its_orientation_colour_and_image_data_and_loses_everything_else(int orientation, bool littleEndian)
    {
        var original = PhoneJpeg(orientation, littleEndian);

        var stripped = PhotoMetadata.Strip(original, PhotoRules.Jpeg)!;

        var text = Encoding.Latin1.GetString(stripped);
        Assert.All(Secrets, secret => Assert.DoesNotContain(secret, text));
        Assert.DoesNotContain("xap/1.0", text);
        Assert.StartsWith("\xFF\xD8\xFF\xE0\0\x10JFIF\0", text);
        Assert.Contains("ICC_PROFILE\0", text);
        Assert.Contains("Adobe\0", text);
        Assert.EndsWith("\xFF\xD9", text); // nothing after the image's own end
        Assert.Contains(Encoding.Latin1.GetString(ScanData) + "\xFF\xD9", text); // the picture, byte for byte

        // The orientation is still there for the browser and the PDF writer, now in a minimal EXIF block.
        Assert.Equal(orientation, PdfImage.FromJpeg(stripped)!.Orientation);
        Assert.Equal(30, PdfImage.FromJpeg(stripped)!.Height);
        Assert.True(stripped.Length < original.Length);
    }

    [Fact]
    public void An_upright_jpeg_needs_no_exif_at_all()
    {
        var stripped = PhotoMetadata.Strip(PhoneJpeg(orientation: 1), PhotoRules.Jpeg)!;

        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(stripped));
        Assert.Equal(1, PdfImage.FromJpeg(stripped)!.Orientation);
    }

    [Fact]
    public void A_jpeg_with_nothing_to_remove_is_returned_unchanged()
    {
        var plain = TestJpeg.Create(40, 30);

        Assert.Equal(plain, PhotoMetadata.Strip(plain, PhotoRules.Jpeg));
    }

    [Fact]
    public void A_jpeg_whose_structure_cannot_be_followed_is_refused()
    {
        var damaged = PhoneJpeg(6);
        damaged[4] = 0xFF; // the first segment now claims to run far past the end of the file
        damaged[5] = 0xF0;

        Assert.Null(PhotoMetadata.Strip(damaged, PhotoRules.Jpeg));
        Assert.Null(PhotoMetadata.Strip(new byte[] { 0xFF, 0xD8, 0x00, 0x00 }, PhotoRules.Jpeg));
    }

    [Fact]
    public void A_png_loses_its_text_time_and_exif_but_keeps_its_orientation_and_pixels()
    {
        var original = PngWithMetadata(DemoPhotoImage.Create("metadata"), orientation: 3);

        var stripped = PhotoMetadata.Strip(original, PhotoRules.Png)!;

        var chunks = Chunks(stripped);
        Assert.Equal(new[] { "IHDR", "IDAT", "eXIf", "IEND" }, chunks.Select(c => c.Type));
        var text = Encoding.Latin1.GetString(stripped);
        Assert.All(Secrets, secret => Assert.DoesNotContain(secret, text));
        Assert.EndsWith("IEND\xAE\x42\x60\x82", text); // nothing after IEND

        var exif = chunks.Single(c => c.Type == "eXIf");
        Assert.Equal(3, PhotoMetadata.ExifOrientation("Exif\0\0"u8.ToArray().Concat(exif.Data).ToArray()));
        Assert.All(chunks, c => Assert.Equal(PngChunks.Crc32(Encoding.ASCII.GetBytes(c.Type).Concat(c.Data).ToArray()), c.Crc));

        // Still the same picture.
        var before = PdfImage.FromPng(new MemoryStream(original), ReportRules.PhotoThumbnailMaxSide)!;
        var after = PdfImage.FromPng(new MemoryStream(stripped), ReportRules.PhotoThumbnailMaxSide)!;
        Assert.Equal(before.Data, after.Data);
    }

    [Fact]
    public void The_demo_png_has_no_metadata_and_is_stored_as_it_is()
    {
        var png = DemoPhotoImage.Create("plain");

        Assert.Equal(png, PhotoMetadata.Strip(png, PhotoRules.Png));
    }

    [Fact]
    public void A_truncated_png_is_refused()
    {
        var png = DemoPhotoImage.Create("truncated");

        Assert.Null(PhotoMetadata.Strip(png[..^12], PhotoRules.Png)); // IEND cut off
    }

    [Fact]
    public void The_png_crc_is_the_standard_one()
        => Assert.Equal(0xAE426082u, PngChunks.Crc32("IEND"u8)); // the CRC every PNG ends with

    // ---- builders ----

    private static void Segment(Stream jpeg, byte marker, byte[] data)
    {
        jpeg.Write(new byte[] { 0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2) });
        jpeg.Write(data);
    }

    /// <summary>EXIF with Orientation, Make and an ASCII stand-in for the GPS data, values stored after IFD0.</summary>
    private static byte[] Exif(int orientation, bool littleEndian, string make, string gps)
        => "Exif\0\0"u8.ToArray().Concat(Tiff(orientation, littleEndian, make, gps)).ToArray();

    private static byte[] Tiff(int orientation, bool littleEndian, string make, string gps)
    {
        var strings = Encoding.ASCII.GetBytes(make + "\0" + gps + "\0");
        const int entries = 3;
        var valuesAt = 8 + 2 + entries * 12 + 4;
        var tiff = new byte[valuesAt + strings.Length];
        void U16(int at, int v) { if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), (ushort)v); }
        void U32(int at, int v) { if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at), (uint)v); else BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), (uint)v); }

        tiff[0] = tiff[1] = (byte)(littleEndian ? 'I' : 'M');
        U16(2, 42);
        U32(4, 8);
        U16(8, entries);
        U16(10, 0x010F); U16(12, 2); U32(14, make.Length + 1); U32(18, valuesAt);                     // Make
        U16(22, 0x0112); U16(24, 3); U32(26, 1); U16(30, orientation);                               // Orientation
        U16(34, 0x9286); U16(36, 2); U32(38, gps.Length + 1); U32(42, valuesAt + make.Length + 1);  // a comment
        strings.CopyTo(tiff, valuesAt);
        return tiff;
    }

    private static byte[] PngWithMetadata(byte[] png, int orientation)
    {
        var chunks = Chunks(png);
        using var output = new MemoryStream();
        output.Write(png.AsSpan(0, 8));
        foreach (var chunk in chunks)
        {
            if (chunk.Type == "IEND")
            {
                PngChunks.Write(output, "tEXt", Encoding.Latin1.GetBytes("Author\0Owner: Ali"));
                PngChunks.Write(output, "iTXt", Encoding.UTF8.GetBytes("Location\0\0\0\0\0Kuching site 7"));
                PngChunks.Write(output, "tIME", new byte[] { 0x07, 0xEA, 9, 22, 10, 5, 0 });
                PngChunks.Write(output, "eXIf", Tiff(orientation, littleEndian: true, "SecretCam X100", "GPS 1.5535N 110.3593E"));
            }
            PngChunks.Write(output, chunk.Type, chunk.Data);
        }
        output.Write(Encoding.ASCII.GetBytes("GPS 1.5535N 110.3593E")); // junk after IEND
        return output.ToArray();
    }

    private static List<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[], uint)>();
        var at = 8;
        while (at + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            chunks.Add((type, png[(at + 8)..(at + 8 + length)], BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length))));
            at += 12 + length;
            if (type == "IEND") break;
        }
        return chunks;
    }
}
