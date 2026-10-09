using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SarawakBizOps.Api.Data;
using SarawakBizOps.Api.Services.Reports;
using SarawakBizOps.Api.Services.Reports.Pdf;
using SarawakBizOps.Api.Tests.Infrastructure;

namespace SarawakBizOps.Api.Tests;

/// <summary>
/// Phase 10: the hand-rolled PDF writer and the report layout (Open Question 3 → no PDF library). These are the
/// plan's "a non-empty PDF is produced" smoke checks, made a little stricter: the file's structure (header, object
/// offsets, trailer) is valid, text is where it should be, and pages and images are there.
/// </summary>
public class ServiceReportPdfTests
{
    private static ServiceReportModel Model(int parts = 2, int photos = 0) => new()
    {
        ReportNumber = "SR-2026-000123",
        Revision = 1,
        GeneratedAtMyt = new DateTime(2026, 9, 22, 10, 5, 0),
        GeneratedByName = "Jenny Lau",
        WorkOrderId = 41,
        ServiceRequestId = 7,
        Customer = new ReportCustomer("Rajang Timber Mills Sdn Bhd", "Mr. Wong (site)", "084-000 111", null, "Lot 12, Sibu"),
        Equipment = new ReportEquipment("RTM-BSW-001", "Band Saw", "Wintersteiger", "DSB 1000", "Line A"),
        Priority = "High",
        ProblemDescription = "Blade tracking drifts after warm-up.",
        TechnicianName = "Ahmad Bujang",
        Diagnosis = "Tracking wheel bearing worn.",
        WorkPerformed = "Replaced the bearing and re-tracked the blade.",
        Notes = null,
        Parts = Enumerable.Range(1, parts)
            .Select(i => new ReportPartLine($"PN-{i:000}", $"Part number {i}", "pcs", 1, 10.5m, 10.5m))
            .ToList(),
        PartsTotal = parts * 10.5m,
        Photos = Enumerable.Range(1, photos)
            .Select(i => new ReportPhoto(i, 900 + i, "image/png", new DateTime(2026, 9, 21, 9, i, 0), "Ahmad Bujang"))
            .ToList(),
        HasSignature = true,
        Timeline = new[] { new ReportTimestamp("Completed", new DateTime(2026, 9, 22, 0, 30, 0)) }
    };

    private static byte[] Render(ServiceReportModel model, IReadOnlyList<PdfImage?>? photos = null, PdfImage? signature = null)
        => ServiceReportRenderer.Render(model, photos ?? model.Photos.Select(_ => (PdfImage?)null).ToList(), signature);

    [Fact]
    public void A_report_is_a_structurally_valid_pdf()
    {
        var pdf = Render(Model());
        var text = AssertStructurallyValid(pdf);

        Assert.Contains("/Title (Service report SR-2026-000123)", text);
        Assert.Contains("/BaseFont /Helvetica ", text);
        Assert.Contains("/BaseFont /Helvetica-Bold ", text);
        Assert.DoesNotContain("/FontFile2", text); // Latin script only: nothing is embedded
    }

    [Fact]
    public void The_pages_show_the_report_content_with_a_numbered_footer()
    {
        var content = PageContent(Render(Model()));

        foreach (var expected in new[]
                 {
                     "(SR-2026-000123)", "(Revision 1)", "(Rajang Timber Mills Sdn Bhd)", "(Mr. Wong \\(site\\))",
                     "(Band Saw \\(RTM-BSW-001\\))", "(Wintersteiger / DSB 1000)", "(Ahmad Bujang)", "(High)",
                     "(Blade tracking drifts after warm-up.)", "(Tracking wheel bearing worn.)",
                     "(Replaced the bearing and re-tracked the blade.)", "(PN-001)", "(RM 10.50)", "(Parts total)",
                     "(RM 21.00)", "(22 Sep 2026, 00:30)", "(SR-2026-000123 \\267 Page 1 of 1)"
                 })
        {
            Assert.Contains(expected, content);
        }
    }

    [Fact]
    public void Long_content_flows_onto_more_pages_and_every_page_has_its_footer()
    {
        var pdf = Render(Model(parts: 90));
        var content = PageContent(pdf);

        var pages = int.Parse(Regex.Match(Encoding.Latin1.GetString(pdf), @"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)").Groups[1].Value);
        Assert.True(pages >= 3, $"expected at least 3 pages, got {pages}");
        for (var page = 1; page <= pages; page++)
        {
            Assert.Contains($"(SR-2026-000123 \\267 Page {page} of {pages})", content);
        }
        Assert.Contains("(PN-090)", content);
        Assert.Contains("(RM 945.00)", content);
    }

    [Fact]
    public void The_same_input_always_renders_the_same_bytes()
        => Assert.Equal(Render(Model(parts: 5)), Render(Model(parts: 5)));

    [Fact]
    public void Photos_and_the_signature_are_embedded_as_images_and_a_missing_photo_gets_a_placeholder()
    {
        var model = Model(photos: 3);
        var png = PdfImage.FromPng(new MemoryStream(DemoPhotoImage.Create("unit test photo")), ReportRules.PhotoThumbnailMaxSide);
        var jpeg = PdfImage.FromJpeg(TestJpeg.Create(320, 240, orientation: 6));
        var signature = PdfImage.FromPng(new MemoryStream(DemoSignatureImage.Create("unit test")), ReportRules.SignatureMaxSide);

        var pdf = Render(model, new[] { png, jpeg, null }, signature);
        var text = Encoding.Latin1.GetString(pdf);
        var content = PageContent(pdf);

        Assert.Equal(3, Regex.Matches(text, "/Subtype /Image").Count);
        Assert.Contains("/Width 320 /Height 240 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", text);
        Assert.Contains("/Width 480 /Height 160 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode", text);
        Assert.Equal(3, Regex.Matches(content, @"/Im\d Do").Count);
        Assert.Contains("(Photo 3 \\267 21 Sep 2026, 09:03)", content);
        Assert.Contains("(Not pictured in", content);
    }

    [Fact]
    public void Without_a_readable_signature_the_report_says_so()
    {
        var content = PageContent(Render(Model(), signature: null));

        Assert.Contains("(The stored signature could not be read.)", content);
    }

    [Fact]
    public void Rendering_needs_one_image_slot_per_photo()
        => Assert.Throws<ArgumentException>(() => ServiceReportRenderer.Render(Model(photos: 2), new PdfImage?[1], null));

    // ---- Text ----

    [Fact]
    public void Text_is_encoded_as_win_ansi_and_characters_the_standard_fonts_cannot_show_become_question_marks()
    {
        Assert.Equal(new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xE9, (byte)' ', 0x96, (byte)' ', 0x93, (byte)'o', (byte)'k', 0x94 },
            PdfFont.Encode("Café – “ok”"));
        Assert.Equal("??"u8.ToArray(), PdfFont.Encode("古晋"));
        Assert.Equal("a b"u8.ToArray(), PdfFont.Encode("a\nb"));
    }

    [Fact]
    public void Text_is_measured_with_the_font_metrics()
    {
        Assert.Equal(5.56, PdfFont.Regular.Width("0", 10), 3);
        Assert.Equal(22.78, PdfFont.Regular.Width("Hello", 10), 2); // 722 + 556 + 222 + 222 + 556
        Assert.True(PdfFont.Bold.Width("Hello", 10) > PdfFont.Regular.Width("Hello", 10));
    }

    [Fact]
    public void Wrapping_keeps_every_line_within_the_width_and_breaks_words_that_are_too_long()
    {
        var text = "The compressor relay tripped twice during the load test.\nSee also: " + new string('x', 120);

        var lines = ServiceReportRenderer.Wrap(text, PdfFont.Regular, 10, 150);

        Assert.All(lines, line => Assert.True(PdfFont.Regular.Width(line, 10) <= 150, line));
        Assert.Equal("The compressor relay tripped", lines[0]);
        Assert.Contains(lines, l => l == "See also:");
        Assert.Equal(text.Replace("\n", "").Replace(" ", ""), string.Concat(lines).Replace(" ", ""));
    }

    // ---- Text Helvetica cannot show (6.11) ----

    [Fact]
    public void Text_is_split_between_helvetica_and_the_embedded_font_only_where_helvetica_cannot_show_it()
    {
        Assert.Equal(new[] { new TextRun(false, "Café – "), new TextRun(true, "古晋"), new TextRun(false, " ok") },
            PdfFont.Runs("Café – 古晋 ok"));
        Assert.Equal(new[] { new TextRun(false, "Rajang Timber Mills") }, PdfFont.Runs("Rajang Timber Mills"));

        // Scripts the embedded font has no glyphs for (and that would need shaping) stay with Helvetica, as '?'.
        Assert.Equal(new[] { new TextRun(false, "مرحبا") }, PdfFont.Runs("مرحبا"));
        Assert.Equal("?????"u8.ToArray(), PdfFont.Encode("مرحبا"));
    }

    [Fact]
    public void Chinese_greek_and_cyrillic_are_measured_with_the_embedded_font()
    {
        Assert.Equal(10, PdfFont.Regular.Width("古", 10), 3);       // a CJK ideograph is one em wide
        Assert.Equal(20 + PdfFont.Regular.Width("ab", 10), PdfFont.Regular.Width("a古晋b", 10), 3);
        Assert.True(PdfFont.Regular.Width("Пётр β", 10) > 0);
    }

    [Fact]
    public void A_report_with_chinese_text_embeds_a_subset_of_the_font_and_stays_a_valid_pdf()
    {
        var model = Model() with
        {
            Customer = new ReportCustomer("古晋机械工程有限公司", "王先生 (site)", "082-000 222", null, "砂拉越古晋"),
            Diagnosis = "Ремень изношен; β-bearing 磨损 \U00020087."
        };

        var pdf = Render(model);
        var text = AssertStructurallyValid(pdf);
        var content = PageContent(pdf);

        Assert.Contains("/Subtype /Type0", text);
        Assert.Contains("/Encoding /Identity-H", text);
        Assert.Contains("/Subtype /CIDFontType2", text);
        Assert.Contains("/CIDToGIDMap /Identity", text);
        Assert.Matches(@"/BaseFont /[A-Z]{6}\+NotoSansSC-Regular ", text);
        Assert.Contains("/F3 ", content);
        Assert.Contains("(Ahmad Bujang)", content); // Latin text is still plain literals in Helvetica
        Assert.Contains("( \\(site\\)) Tj", content);  // ...also the Latin part of a mixed line

        // The ToUnicode map gives the characters back for copying and searching, a supplementary one as a pair.
        Assert.Contains("<53E4>", text);  // 古
        Assert.Contains("<664B>", text);  // 晋
        Assert.Contains("<D840DC87>", text); // U+20087, outside the BMP
        Assert.Equal(Render(model), pdf); // still deterministic

        // The embedded file is a TrueType font with only the glyphs drawn, one em wide each for the ideographs.
        var font = new TrueTypeFont(Inflate(FontFile(pdf)));
        var distinct = (model.Customer.CompanyName + model.Customer.ContactPerson + model.Customer.Address + model.Diagnosis)
            .EnumerateRunes()
            .Where(r => PdfFont.Runs(r.ToString()) is [{ Embedded: true }])
            .Select(r => PdfFont.Fallback.GlyphId(r.Value))
            .Distinct()
            .Count();
        Assert.Equal(distinct + 1, font.GlyphCount); // + .notdef
        Assert.Equal(1000, font.Advance(1));
    }

    [Fact]
    public void Wrapping_text_without_spaces_breaks_between_characters_and_never_inside_a_surrogate_pair()
    {
        var chinese = string.Concat(Enumerable.Repeat("压缩机继电器在负载测试中跳闸两次", 4));
        var pairs = string.Concat(Enumerable.Repeat("\U00020087", 30));

        foreach (var text in new[] { chinese, pairs })
        {
            var lines = ServiceReportRenderer.Wrap(text, PdfFont.Regular, 10, 95);

            Assert.True(lines.Count > 1);
            Assert.All(lines, line => Assert.True(PdfFont.Regular.Width(line, 10) <= 95, line));
            Assert.All(lines, line => Assert.False(char.IsLowSurrogate(line[0]) || char.IsHighSurrogate(line[^1]), line));
            Assert.Equal(text, string.Concat(lines));
        }
    }

    [Fact]
    public void A_title_outside_ascii_is_written_as_utf16()
    {
        var document = new PdfDocument();
        document.AddPage();

        var text = Encoding.Latin1.GetString(document.Save("报告 SR-1", new DateTime(2026, 1, 1)));

        Assert.Contains("/Title <FEFF62A5544A002000530052002D0031>", text);
    }

    [Fact]
    public void A_font_subset_keeps_the_requested_glyphs_in_order_and_pulls_in_the_parts_of_composite_glyphs()
    {
        // Glyph 3 is a composite made of glyphs 1 and 2; asking for 3 alone brings 1 and 2 along after it.
        var font = new TrueTypeFont(TestTrueType.Create());

        var bytes = font.Subset(new ushort[] { 0, 3 });
        var subset = new TrueTypeFont(bytes);

        Assert.Equal(4, subset.GlyphCount);
        Assert.Equal(font.Advance(3), subset.Advance(1));
        Assert.Equal(new ushort[] { 2, 3 }, TestTrueType.ComponentsOf(bytes, 1)); // renumbered: old 1 → 2, old 2 → 3
        Assert.Equal(font.Advance(1), subset.Advance(2));
        Assert.Equal(font.Advance(2), subset.Advance(3));
        Assert.Equal(0xB1B0AFBAu, TestTrueType.WholeFileChecksum(bytes)); // head.checkSumAdjustment is right
        Assert.Throws<ArgumentException>(() => font.Subset(new ushort[] { 3 })); // .notdef must come first
    }

    // ---- Images ----

    [Fact]
    public void A_greyscale_png_is_decoded_and_scaled_down_to_the_thumbnail_size()
    {
        var png = DemoSignatureImage.Create("scaling"); // 480 × 160 greyscale

        var full = PdfImage.FromPng(new MemoryStream(png), 800)!;
        var half = PdfImage.FromPng(new MemoryStream(png), 240)!;

        Assert.Equal((480, 160, "DeviceGray", "FlateDecode"), (full.Width, full.Height, full.ColorSpace, full.Filter));
        Assert.Equal((240, 80), (half.Width, half.Height));
        Assert.Equal(240 * 80, Inflate(half.Data).Length);
    }

    [Fact]
    public void An_rgb_png_is_decoded_to_rgb()
    {
        var image = PdfImage.FromPng(new MemoryStream(DemoPhotoImage.Create("rgb")), 160)!; // 320 × 240 RGB

        Assert.Equal((160, 120, "DeviceRGB"), (image.Width, image.Height, image.ColorSpace));
        Assert.Equal(160 * 120 * 3, Inflate(image.Data).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Every_png_filter_type_decodes_to_the_original_pixels(byte filter)
    {
        var pixels = new byte[]
        {
            10, 20, 30, 200, 100, 50, 0, 255, 0,
            90, 80, 70, 1, 2, 3, 250, 240, 230,
            5, 5, 5, 128, 64, 32, 33, 66, 99
        };

        var image = PdfImage.FromPng(new MemoryStream(TestPng.Encode(3, 3, colourType: 2, bitDepth: 8, pixels, filter)), 100)!;

        Assert.Equal(pixels, Inflate(image.Data));
    }

    [Fact]
    public void Transparency_is_flattened_onto_white()
    {
        // RGBA: opaque red, fully transparent black, half-transparent blue.
        var rgba = new byte[] { 255, 0, 0, 255, 0, 0, 0, 0, 0, 0, 255, 128 };

        var image = PdfImage.FromPng(new MemoryStream(TestPng.Encode(3, 1, colourType: 6, bitDepth: 8, rgba)), 100)!;

        Assert.Equal(new byte[] { 255, 0, 0, 255, 255, 255, 127, 127, 255 }, Inflate(image.Data));
    }

    [Fact]
    public void A_two_bit_palette_png_with_a_transparent_entry_is_decoded()
    {
        var palette = new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0 };
        var alpha = new byte[] { 255, 255, 0 }; // entry 2 is transparent
        // Indices 0, 1, 2, 1 packed four to a byte at two bits each.
        var packed = new byte[] { 0b00_01_10_01 };

        var image = PdfImage.FromPng(new MemoryStream(TestPng.Encode(4, 1, colourType: 3, bitDepth: 2, packed, palette: palette, transparency: alpha)), 100)!;

        Assert.Equal(new byte[] { 0, 0, 0, 255, 0, 0, 255, 255, 255, 255, 0, 0 }, Inflate(image.Data));
    }

    [Fact]
    public void A_sixteen_bit_greyscale_png_uses_the_high_byte()
    {
        var samples = new byte[] { 0x12, 0x34, 0xFF, 0x00 };

        var image = PdfImage.FromPng(new MemoryStream(TestPng.Encode(2, 1, colourType: 0, bitDepth: 16, samples)), 100)!;

        Assert.Equal(new byte[] { 0x12, 0xFF }, Inflate(image.Data));
    }

    [Fact]
    public void Pngs_that_cannot_be_read_safely_give_no_image()
    {
        var valid = DemoPhotoImage.Create("truncate me");
        var interlaced = TestPng.Encode(2, 2, colourType: 0, bitDepth: 8, new byte[4], interlace: 1);
        var enormous = TestPng.Header(60_000, 60_000, colourType: 2, bitDepth: 8); // 3.6 gigapixels: refused unread

        Assert.Null(PdfImage.FromPng(new MemoryStream(valid[..(valid.Length / 2)]), 100));
        Assert.Null(PdfImage.FromPng(new MemoryStream(interlaced), 100));
        Assert.Null(PdfImage.FromPng(new MemoryStream(enormous), 100));
        Assert.Null(PdfImage.FromPng(new MemoryStream(TestJpeg.Create(10, 10)), 100));
    }

    [Fact]
    public void A_jpeg_is_embedded_as_is_with_its_size_and_orientation_read_from_the_headers()
    {
        var bytes = TestJpeg.Create(320, 240, orientation: 6);

        var image = PdfImage.FromJpeg(bytes)!;

        Assert.Same(bytes, image.Data);
        Assert.Equal((320, 240, "DeviceRGB", "DCTDecode", 6), (image.Width, image.Height, image.ColorSpace, image.Filter, image.Orientation));
        Assert.Equal(0.75, image.DisplayAspect); // rotated a quarter turn: shown portrait
        Assert.Equal(1, PdfImage.FromJpeg(TestJpeg.Create(320, 240, bigEndianExif: false))!.Orientation);
        Assert.Equal(8, PdfImage.FromJpeg(TestJpeg.Create(320, 240, orientation: 8, bigEndianExif: false))!.Orientation);
        Assert.Equal("DeviceGray", PdfImage.FromJpeg(TestJpeg.Create(8, 8, components: 1))!.ColorSpace);
    }

    [Fact]
    public void Jpegs_a_viewer_might_not_show_give_no_image()
    {
        Assert.Null(PdfImage.FromJpeg(TestJpeg.Create(320, 240, components: 4)));     // CMYK
        Assert.Null(PdfImage.FromJpeg(TestJpeg.Create(320, 240, frameMarker: 0xC3))); // lossless
        Assert.Null(PdfImage.FromJpeg(TestJpeg.Create(320, 240, frameMarker: 0xC9))); // arithmetic coding
        Assert.Null(PdfImage.FromJpeg(TestData.PhotoJpeg));                             // no frame header at all
        Assert.Null(PdfImage.FromJpeg(DemoPhotoImage.Create("not a jpeg")));
    }

    [Theory]
    [InlineData(1, 100, 0, 0, 50, 10, 20)]
    [InlineData(3, -100, 0, 0, -50, 110, 70)]
    [InlineData(6, 0, -50, 100, 0, 10, 70)]
    [InlineData(8, 0, 50, -100, 0, 110, 20)]
    public void Orientation_is_applied_when_the_image_is_placed(int orientation, double a, double b, double c, double d, double e, double f)
    {
        var document = new PdfDocument();
        var page = document.AddPage();
        page.Image(PdfImage.FromJpeg(TestJpeg.Create(40, 30, orientation: orientation))!, 10, 20, 100, 50);

        var content = PageContent(document.Save("t", new DateTime(2026, 1, 1)));

        Assert.Contains($"q {N(a)} {N(b)} {N(c)} {N(d)} {N(e)} {N(f)} cm /Im1 Do Q", content);
    }

    private static string N(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // ---- helpers ----

    /// <summary>Header, trailer, and an xref whose every entry points at the start of its object; returns the text.</summary>
    private static string AssertStructurallyValid(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4\n", text);
        Assert.EndsWith("%%EOF\n", text);

        var startXref = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value);
        Assert.StartsWith("xref\n0 ", text[startXref..]);
        var entries = Regex.Matches(text[startXref..], @"(\d{10}) 00000 n ").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(entries);
        for (var i = 0; i < entries.Count; i++)
        {
            Assert.StartsWith($"{i + 1} 0 obj\n", text[entries[i]..]);
        }
        return text;
    }

    /// <summary>The (still compressed) data of the one embedded font file.</summary>
    private static byte[] FontFile(byte[] pdf)
    {
        var match = Regex.Match(Encoding.Latin1.GetString(pdf), @"<< /Length1 \d+ /Filter /FlateDecode /Length (\d+) >>\nstream\n");
        Assert.True(match.Success, "no FontFile2 stream");
        var start = match.Index + match.Length;
        return pdf[start..(start + int.Parse(match.Groups[1].Value))];
    }

    private static byte[] Inflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress))
        {
            zlib.CopyTo(output);
        }
        return output.ToArray();
    }

    /// <summary>Every page content stream (the FlateDecode streams that are not images), decompressed.</summary>
    private static string PageContent(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var builder = new StringBuilder();
        foreach (Match match in Regex.Matches(text, @"<< /Filter /FlateDecode /Length (\d+) >>\nstream\n"))
        {
            var start = match.Index + match.Length;
            var length = int.Parse(match.Groups[1].Value);
            builder.Append(Encoding.Latin1.GetString(Inflate(pdf[start..(start + length)])));
        }
        return builder.ToString();
    }
}

/// <summary>Builds small PNGs with a chosen colour type, bit depth and filter, for decoder tests.</summary>
internal static class TestPng
{
    public static byte[] Header(int width, int height, byte colourType, byte bitDepth, byte interlace = 0)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = bitDepth;
        ihdr[9] = colourType;
        ihdr[12] = interlace;
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    /// <summary><paramref name="data"/> is the packed scanlines without filter bytes; each row is filtered with
    /// <paramref name="filter"/>.</summary>
    public static byte[] Encode(
        int width, int height, byte colourType, byte bitDepth, byte[] data, byte filter = 0,
        byte[]? palette = null, byte[]? transparency = null, byte interlace = 0)
    {
        var channels = colourType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        var bitsPerPixel = channels * bitDepth;
        var rowBytes = (width * bitsPerPixel + 7) / 8;
        var step = Math.Max(1, bitsPerPixel / 8);

        var raw = new MemoryStream();
        var previous = new byte[rowBytes];
        for (var y = 0; y < height; y++)
        {
            var row = data.AsSpan(y * rowBytes, rowBytes).ToArray();
            raw.WriteByte(filter);
            for (var i = 0; i < rowBytes; i++)
            {
                int a = i >= step ? row[i - step] : 0, b = previous[i], c = i >= step ? previous[i - step] : 0;
                var predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0
                };
                raw.WriteByte((byte)(row[i] - predictor));
            }
            previous = row;
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw.ToArray());
        }

        var header = Header(width, height, colourType, bitDepth, interlace);
        using var png = new MemoryStream();
        png.Write(header, 0, header.Length - 12); // everything but IEND
        if (palette is not null) Chunk(png, "PLTE", palette);
        if (transparency is not null) Chunk(png, "tRNS", transparency);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    // The decoder does not check CRCs, so these are left as zero.
    private static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        png.Write(Encoding.ASCII.GetBytes(type));
        png.Write(data);
        png.Write(new byte[4]);
    }
}

/// <summary>JPEG headers (no image data) — enough for everything the PDF writer reads from a JPEG.</summary>
internal static class TestJpeg
{
    public static byte[] Create(
        int width, int height, int components = 3, byte frameMarker = 0xC0, int? orientation = null, bool bigEndianExif = true)
    {
        using var jpeg = new MemoryStream();
        jpeg.Write(new byte[] { 0xFF, 0xD8 });
        if (orientation is not null || !bigEndianExif)
        {
            Segment(jpeg, 0xE1, Exif(orientation ?? 1, bigEndianExif));
        }

        var frame = new List<byte> { 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components };
        for (var c = 1; c <= components; c++) frame.AddRange(new byte[] { (byte)c, 0x11, 0 });
        Segment(jpeg, frameMarker, frame.ToArray());
        jpeg.Write(new byte[] { 0xFF, 0xD9 });
        return jpeg.ToArray();
    }

    private static byte[] Exif(int orientation, bool bigEndian)
    {
        var tiff = new byte[8 + 2 + 12 + 4];
        void U16(int at, int v) { if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(tiff.AsSpan(at), (ushort)v); else BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(at), (ushort)v); }
        void U32(int at, int v) { if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(tiff.AsSpan(at), (uint)v); else BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(at), (uint)v); }
        tiff[0] = tiff[1] = (byte)(bigEndian ? 'M' : 'I');
        U16(2, 42);
        U32(4, 8);        // IFD0 right after the header
        U16(8, 1);        // one entry
        U16(10, 0x0112);  // Orientation
        U16(12, 3);       // SHORT
        U32(14, 1);
        U16(18, orientation);
        return "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
    }

    private static void Segment(Stream jpeg, byte marker, byte[] data)
    {
        jpeg.Write(new byte[] { 0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2) });
        jpeg.Write(data);
    }
}

/// <summary>
/// A four-glyph TrueType font for the subsetter: .notdef, two simple glyphs, and glyph 3, a composite of 1 and 2
/// (the bundled Noto font has no composites, so this is what exercises the renumbering).
/// </summary>
internal static class TestTrueType
{
    public static byte[] Create()
    {
        var simple = new byte[] { 0, 1, 0, 0, 0, 0, 0, 10, 0, 10, 0, 0, 0, 0, 0x01, 0, 5, 0, 5, 0 };
        var composite = new List<byte> { 0xFF, 0xFF, 0, 0, 0, 0, 0, 20, 0, 20 };
        composite.AddRange(new byte[] { 0x00, 0x21, 0, 1, 0, 3, 0, 4 }); // words args, more components, glyph 1
        composite.AddRange(new byte[] { 0x00, 0x00, 0, 2, 7, 8 });       // byte args, last component, glyph 2
        var glyphs = new[] { Array.Empty<byte>(), simple, simple.Reverse().ToArray(), composite.ToArray() };

        var glyf = new MemoryStream();
        var loca = new byte[(glyphs.Length + 1) * 4];
        for (var i = 0; i < glyphs.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)glyf.Position);
            glyf.Write(glyphs[i]);
        }
        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(glyphs.Length * 4), (uint)glyf.Position);

        var hmtx = new byte[glyphs.Length * 4];
        for (var i = 0; i < glyphs.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(i * 4), (ushort)(500 + 100 * i));
        }

        var head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);

        var hhea = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(hhea, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), 800);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), -200);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)glyphs.Length);

        var maxp = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)glyphs.Length);

        var tables = new (string Tag, byte[] Data)[]
        {
            ("glyf", glyf.ToArray()), ("head", head), ("hhea", hhea), ("hmtx", hmtx), ("loca", loca), ("maxp", maxp)
        };
        using var font = new MemoryStream();
        var directory = new byte[12 + tables.Length * 16];
        BinaryPrimitives.WriteUInt32BigEndian(directory, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(directory.AsSpan(4), (ushort)tables.Length);
        font.Write(directory);
        for (var i = 0; i < tables.Length; i++)
        {
            var record = 12 + i * 16;
            Encoding.ASCII.GetBytes(tables[i].Tag).CopyTo(directory, record);
            BinaryPrimitives.WriteUInt32BigEndian(directory.AsSpan(record + 8), (uint)font.Position);
            BinaryPrimitives.WriteUInt32BigEndian(directory.AsSpan(record + 12), (uint)tables[i].Data.Length);
            font.Write(tables[i].Data);
            while (font.Position % 4 != 0) font.WriteByte(0);
        }
        font.Position = 0;
        font.Write(directory);
        return font.ToArray();
    }

    /// <summary>The glyph ids a composite glyph of <paramref name="font"/> (long loca) is built from.</summary>
    public static ushort[] ComponentsOf(byte[] font, int glyph)
    {
        int TableOffset(string tag)
        {
            var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (var i = 0; i < count; i++)
            {
                if (Encoding.ASCII.GetString(font, 12 + i * 16, 4) == tag)
                {
                    return (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(12 + i * 16 + 8));
                }
            }
            throw new InvalidDataException(tag);
        }

        var start = TableOffset("glyf") + (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(TableOffset("loca") + glyph * 4));
        var components = new List<ushort>();
        var at = start + 10;
        while (true)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(at));
            components.Add(BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(at + 2)));
            at += 4 + ((flags & 1) != 0 ? 4 : 2);
            if ((flags & 0x20) == 0) return components.ToArray();
        }
    }

    public static uint WholeFileChecksum(byte[] font)
    {
        uint sum = 0;
        for (var i = 0; i < font.Length; i += 4)
        {
            var word = new byte[4];
            font.AsSpan(i, Math.Min(4, font.Length - i)).CopyTo(word);
            sum += BinaryPrimitives.ReadUInt32BigEndian(word);
        }
        return sum;
    }
}
