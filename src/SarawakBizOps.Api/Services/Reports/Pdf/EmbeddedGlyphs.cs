using System.IO.Compression;
using System.Text;

namespace SarawakBizOps.Api.Services.Reports.Pdf;

/// <summary>
/// The fallback font's glyphs one document draws, numbered from 1 in the order they are first used. That number
/// is both the CID written in the page content (two bytes, Identity-H) and the glyph id in the embedded subset
/// (CIDToGIDMap /Identity), so the content can be written before the document knows which glyphs it will need.
/// First-use order and a tag derived from the glyph list keep the output byte-for-byte deterministic.
/// </summary>
internal sealed class EmbeddedGlyphs
{
    /// <summary>Type0 font, CIDFontType2, font descriptor, font file and ToUnicode CMap.</summary>
    public const int ObjectCount = 5;

    private readonly TrueTypeFont _font;
    private readonly List<ushort> _glyphs = new() { 0 };   // CID → glyph id in the full font; CID 0 is .notdef
    private readonly List<int> _codePoints = new() { 0 };  // CID → the character it was first drawn for
    private readonly Dictionary<ushort, ushort> _cids = new();

    public EmbeddedGlyphs(TrueTypeFont font) => _font = font;

    /// <summary><paramref name="text"/> as a hex string of two-byte CIDs, adding any glyph not used before.</summary>
    public string Hex(string text)
    {
        var hex = new StringBuilder("<");
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = _font.GlyphId(rune.Value);
            if (!_cids.TryGetValue(glyph, out var cid))
            {
                cid = checked((ushort)_glyphs.Count);
                _glyphs.Add(glyph);
                _codePoints.Add(rune.Value);
                _cids[glyph] = cid;
            }
            hex.Append(cid.ToString("X4"));
        }
        return hex.Append('>').ToString();
    }

    /// <summary>
    /// The font's PDF objects, numbered from <paramref name="first"/> (the Type0 font the pages refer to): each
    /// with its dictionary and, for a stream, its data.
    /// </summary>
    public IEnumerable<(int Number, string Dictionary, byte[]? Data)> Objects(int first)
    {
        var (cidFont, descriptor, fontFile, toUnicode) = (first + 1, first + 2, first + 3, first + 4);

        // A subset is named with six capital letters and a '+' before the font's own name (PDF 1.7 §9.6.4).
        var name = $"{SubsetTag()}+{_font.PostScriptName}";
        double Scale(int units) => units * 1000.0 / _font.UnitsPerEm;
        var box = _font.BoundingBox;
        var widths = string.Join(" ", _glyphs.Select(g => PdfPage.Number(PdfFont.FallbackWidth(g))));

        yield return (first,
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding /Identity-H " +
            $"/DescendantFonts [{cidFont} 0 R] /ToUnicode {toUnicode} 0 R >>", null);

        yield return (cidFont,
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{name} " +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {descriptor} 0 R /DW 1000 /W [0 [{widths}]] /CIDToGIDMap /Identity >>", null);

        yield return (descriptor,
            $"<< /Type /FontDescriptor /FontName /{name} /Flags 4 " +
            $"/FontBBox [{PdfPage.Number(Scale(box.XMin))} {PdfPage.Number(Scale(box.YMin))} " +
            $"{PdfPage.Number(Scale(box.XMax))} {PdfPage.Number(Scale(box.YMax))}] /ItalicAngle 0 " +
            $"/Ascent {PdfPage.Number(Scale(_font.Ascent))} /Descent {PdfPage.Number(Scale(_font.Descent))} " +
            $"/CapHeight {PdfPage.Number(Scale(_font.CapHeight))} /StemV 80 /FontFile2 {fontFile} 0 R >>", null);

        var subset = _font.Subset(_glyphs);
        yield return (fontFile, $"/Length1 {subset.Length} /Filter /FlateDecode", Deflate(subset));

        yield return (toUnicode, string.Empty, Encoding.ASCII.GetBytes(ToUnicodeCMap()));
    }

    // So copying or searching the text in a PDF viewer gives back the characters, not glyph numbers.
    private string ToUnicodeCMap()
    {
        var cmap = new StringBuilder()
            .Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
            .Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
            .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        // At most 100 entries per bfchar block.
        for (var start = 1; start < _glyphs.Count; start += 100)
        {
            var count = Math.Min(100, _glyphs.Count - start);
            cmap.Append(count).Append(" beginbfchar\n");
            for (var cid = start; cid < start + count; cid++)
            {
                var utf16 = Encoding.BigEndianUnicode.GetBytes(char.ConvertFromUtf32(_codePoints[cid]));
                cmap.Append($"<{cid:X4}> <{Convert.ToHexString(utf16)}>\n");
            }
            cmap.Append("endbfchar\n");
        }

        return cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n").ToString();
    }

    // Six letters from an FNV-1a hash of the glyph list: the same glyphs always give the same name.
    private string SubsetTag()
    {
        var hash = 2166136261u;
        foreach (var glyph in _glyphs)
        {
            hash = (hash ^ glyph) * 16777619u;
        }

        var tag = new char[6];
        for (var i = 0; i < tag.Length; i++)
        {
            tag[i] = (char)('A' + hash % 26);
            hash /= 26;
        }
        return new string(tag);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }
        return compressed.ToArray();
    }
}
