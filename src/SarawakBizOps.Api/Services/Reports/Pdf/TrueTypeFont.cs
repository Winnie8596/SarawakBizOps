using System.Buffers.Binary;
using System.Text;

namespace SarawakBizOps.Api.Services.Reports.Pdf;

/// <summary>
/// A TrueType (glyf-outline) font read straight from its tables, without a font library (PRD D-06): just what
/// embedding it in a PDF needs — the character map, advance widths, the metrics for the font descriptor, and
/// <see cref="Subset"/>, which writes a font holding only the glyphs one document uses. The report embeds that
/// subset, so a page with three Chinese characters carries three glyphs, not the whole 10 MB font.
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new();
    private readonly Dictionary<int, ushort> _cmap = new();
    private readonly int _numberOfHMetrics;
    private readonly bool _longLoca;

    public int UnitsPerEm { get; }
    public int GlyphCount { get; }
    public string PostScriptName { get; }

    /// <summary>xMin, yMin, xMax, yMax over all glyphs, in font units.</summary>
    public (int XMin, int YMin, int XMax, int YMax) BoundingBox { get; }

    public int Ascent { get; }
    public int Descent { get; }
    public int CapHeight { get; }

    public TrueTypeFont(byte[] data)
    {
        _data = data;
        var version = U32(0);
        if (version != 0x00010000 && version != 0x74727565) // 1.0 or 'true'; 'OTTO' (CFF outlines) is not supported
        {
            throw new InvalidDataException("Not a TrueType font with glyf outlines.");
        }

        var numTables = U16(4);
        for (var i = 0; i < numTables; i++)
        {
            var record = 12 + i * 16;
            var tag = Encoding.ASCII.GetString(_data, record, 4);
            var offset = (int)U32(record + 8);
            var length = (int)U32(record + 12);
            if (offset < 0 || length < 0 || offset + length > _data.Length)
            {
                throw new InvalidDataException($"The '{tag}' table lies outside the font file.");
            }
            _tables[tag] = (offset, length);
        }

        foreach (var required in new[] { "head", "hhea", "maxp", "hmtx", "loca", "glyf" })
        {
            if (!_tables.ContainsKey(required))
            {
                throw new InvalidDataException($"The font has no '{required}' table.");
            }
        }

        var head = Table("head");
        UnitsPerEm = U16(head + 18);
        BoundingBox = (I16(head + 36), I16(head + 38), I16(head + 40), I16(head + 42));
        _longLoca = I16(head + 50) == 1;

        var hhea = Table("hhea");
        Ascent = I16(hhea + 4);
        Descent = I16(hhea + 6);
        _numberOfHMetrics = U16(hhea + 34);

        GlyphCount = U16(Table("maxp") + 4);

        // OS/2 version 2 and later carries the cap height; otherwise the usual 70% of the em is close enough.
        CapHeight = _tables.TryGetValue("OS/2", out var os2) && os2.Length >= 90 && U16(os2.Offset) >= 2
            ? I16(os2.Offset + 88)
            : UnitsPerEm * 7 / 10;

        PostScriptName = ReadPostScriptName() ?? "EmbeddedFont";
        ReadCmap();
    }

    /// <summary>The glyph for <paramref name="codePoint"/>, or 0 (.notdef) when the font does not have one.</summary>
    public ushort GlyphId(int codePoint) => _cmap.GetValueOrDefault(codePoint);

    /// <summary>The glyph's advance width in font units.</summary>
    public int Advance(ushort glyph)
    {
        var hmtx = Table("hmtx");
        var index = Math.Min(glyph, _numberOfHMetrics - 1); // past the last long metric, the last advance repeats
        return U16(hmtx + index * 4);
    }

    /// <summary>
    /// A complete TrueType font holding just <paramref name="glyphs"/>, renumbered so that glyph <c>i</c> of the
    /// subset is <c>glyphs[i]</c> of this font (element 0 must be 0, .notdef). Glyphs that a composite glyph is
    /// built from are appended after the requested ones, which therefore keep their numbers. Hinting
    /// instructions, if any, are kept; the character map is left out, as a PDF CID font does not use it.
    /// </summary>
    public byte[] Subset(IReadOnlyList<ushort> glyphs)
    {
        if (glyphs.Count == 0 || glyphs[0] != 0)
        {
            throw new ArgumentException("A subset starts with glyph 0 (.notdef).", nameof(glyphs));
        }

        var order = new List<ushort>(glyphs);
        var newIds = new Dictionary<ushort, ushort>();
        for (var i = 0; i < order.Count; i++)
        {
            if (glyphs[i] >= GlyphCount)
            {
                throw new ArgumentOutOfRangeException(nameof(glyphs), glyphs[i], "No such glyph in the font.");
            }
            newIds.TryAdd(order[i], (ushort)i);
        }

        // Composite glyphs name their components by glyph id: pull those in too (they may be composites in turn).
        for (var i = 0; i < order.Count; i++)
        {
            foreach (var component in Components(GlyphData(order[i])))
            {
                if (newIds.TryAdd(component, (ushort)order.Count))
                {
                    order.Add(component);
                }
            }
        }

        using var glyf = new MemoryStream();
        var loca = new byte[(order.Count + 1) * 4];
        var hmtx = new byte[order.Count * 4];
        for (var i = 0; i < order.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)glyf.Position);
            var data = GlyphData(order[i]).ToArray();
            RenumberComponents(data, newIds);
            glyf.Write(data);
            while (glyf.Position % 4 != 0) glyf.WriteByte(0);

            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(i * 4), (ushort)Advance(order[i]));
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan(i * 4 + 2), LeftSideBearing(order[i]));
        }
        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(order.Count * 4), (uint)glyf.Position);

        var head = TableBytes("head");
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);  // checkSumAdjustment, set once the file is whole
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);  // indexToLocFormat: long offsets

        var hhea = TableBytes("hhea");
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)order.Count);

        var maxp = TableBytes("maxp");
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)order.Count);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["glyf"] = glyf.ToArray(),
            ["head"] = head,
            ["hhea"] = hhea,
            ["hmtx"] = hmtx,
            ["loca"] = loca,
            ["maxp"] = maxp,
            ["post"] = PostFormat3()
        };
        foreach (var hinting in new[] { "cvt ", "fpgm", "prep" })
        {
            if (_tables.ContainsKey(hinting))
            {
                tables[hinting] = TableBytes(hinting);
            }
        }

        return WriteFont(tables);
    }

    // ---------------------------------------------------------------- reading

    private int Table(string tag) => _tables[tag].Offset;

    private byte[] TableBytes(string tag)
    {
        var (offset, length) = _tables[tag];
        return _data.AsSpan(offset, length).ToArray();
    }

    private ushort U16(int at) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(at));
    private short I16(int at) => BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(at));
    private uint U32(int at) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(at));

    private short LeftSideBearing(ushort glyph)
    {
        var hmtx = Table("hmtx");
        return glyph < _numberOfHMetrics
            ? I16(hmtx + glyph * 4 + 2)
            : I16(hmtx + _numberOfHMetrics * 4 + (glyph - _numberOfHMetrics) * 2);
    }

    private ReadOnlySpan<byte> GlyphData(ushort glyph)
    {
        var loca = Table("loca");
        int start, end;
        if (_longLoca)
        {
            start = (int)U32(loca + glyph * 4);
            end = (int)U32(loca + glyph * 4 + 4);
        }
        else
        {
            start = U16(loca + glyph * 2) * 2;
            end = U16(loca + glyph * 2 + 2) * 2;
        }

        var (glyf, length) = _tables["glyf"];
        if (start > end || end > length)
        {
            throw new InvalidDataException($"Glyph {glyph} lies outside the glyf table.");
        }
        return _data.AsSpan(glyf + start, end - start);
    }

    // A composite glyph (negative contour count) is a list of components, each naming another glyph.
    private static List<ushort> Components(ReadOnlySpan<byte> glyph)
    {
        var components = new List<ushort>();
        if (glyph.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(glyph) >= 0)
        {
            return components;
        }

        var at = 10;
        while (true)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(glyph[at..]);
            components.Add(BinaryPrimitives.ReadUInt16BigEndian(glyph[(at + 2)..]));
            at += ComponentLength(flags);
            if ((flags & MoreComponents) == 0)
            {
                return components;
            }
        }
    }

    private static void RenumberComponents(byte[] glyph, Dictionary<ushort, ushort> newIds)
    {
        if (glyph.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(glyph) >= 0)
        {
            return;
        }

        var at = 10;
        while (true)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(glyph.AsSpan(at));
            var old = BinaryPrimitives.ReadUInt16BigEndian(glyph.AsSpan(at + 2));
            BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(at + 2), newIds[old]);
            at += ComponentLength(flags);
            if ((flags & MoreComponents) == 0)
            {
                return;
            }
        }
    }

    private const ushort MoreComponents = 0x0020;

    // flags + glyphIndex, the two arguments (words or bytes), then an optional scale, x/y scale or 2×2 matrix.
    private static int ComponentLength(ushort flags)
    {
        var length = 4 + ((flags & 0x0001) != 0 ? 4 : 2);
        if ((flags & 0x0008) != 0) length += 2;
        else if ((flags & 0x0040) != 0) length += 4;
        else if ((flags & 0x0080) != 0) length += 8;
        return length;
    }

    // Formats 12 (full Unicode) and 4 (the Basic Multilingual Plane), from the Windows or Unicode platform.
    private void ReadCmap()
    {
        if (!_tables.TryGetValue("cmap", out var cmap))
        {
            return; // a subset written by this class: no character map, glyph ids only
        }

        int? format12 = null, format4 = null;
        var count = U16(cmap.Offset + 2);
        for (var i = 0; i < count; i++)
        {
            var record = cmap.Offset + 4 + i * 8;
            var (platform, encoding) = (U16(record), U16(record + 2));
            var subtable = cmap.Offset + (int)U32(record + 4);
            var format = U16(subtable);
            if (format == 12 && ((platform == 3 && encoding == 10) || platform == 0))
            {
                format12 ??= subtable;
            }
            else if (format == 4 && ((platform == 3 && encoding == 1) || platform == 0))
            {
                format4 ??= subtable;
            }
        }

        if (format12 is { } table12)
        {
            var groups = (int)U32(table12 + 12);
            for (var g = 0; g < groups; g++)
            {
                var group = table12 + 16 + g * 12;
                var (first, last, glyph) = (U32(group), U32(group + 4), U32(group + 8));
                for (var c = first; c <= last && c <= 0x10FFFF; c++, glyph++)
                {
                    _cmap.TryAdd((int)c, (ushort)glyph);
                }
            }
        }
        else if (format4 is { } table4)
        {
            var segments = U16(table4 + 6) / 2;
            var ends = table4 + 14;
            var starts = ends + segments * 2 + 2;
            var deltas = starts + segments * 2;
            var rangeOffsets = deltas + segments * 2;
            for (var s = 0; s < segments; s++)
            {
                int start = U16(starts + s * 2), end = U16(ends + s * 2);
                var delta = I16(deltas + s * 2);
                var rangeOffset = U16(rangeOffsets + s * 2);
                for (var c = start; c <= end && c != 0xFFFF; c++)
                {
                    int glyph;
                    if (rangeOffset == 0)
                    {
                        glyph = (c + delta) & 0xFFFF;
                    }
                    else
                    {
                        var at = rangeOffsets + s * 2 + rangeOffset + (c - start) * 2;
                        glyph = U16(at);
                        if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                    }

                    if (glyph != 0)
                    {
                        _cmap.TryAdd(c, (ushort)glyph);
                    }
                }
            }
        }
    }

    // nameID 6 from the Windows (UTF-16BE) or Macintosh (ASCII) platform; letters, digits and '-' only, as a PDF
    // name needs no escaping then.
    private string? ReadPostScriptName()
    {
        if (!_tables.TryGetValue("name", out var name))
        {
            return null;
        }

        var count = U16(name.Offset + 2);
        var strings = name.Offset + U16(name.Offset + 4);
        for (var i = 0; i < count; i++)
        {
            var record = name.Offset + 6 + i * 12;
            if (U16(record + 6) != 6)
            {
                continue;
            }

            var platform = U16(record);
            var bytes = _data.AsSpan(strings + U16(record + 10), U16(record + 8));
            var text = platform is 0 or 3 ? Encoding.BigEndianUnicode.GetString(bytes) : Encoding.ASCII.GetString(bytes);
            var clean = new string(text.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').ToArray());
            if (clean.Length > 0)
            {
                return clean;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- writing

    // post format 3: the metrics of the original header, and no glyph names.
    private byte[] PostFormat3()
    {
        var post = new byte[32];
        if (_tables.TryGetValue("post", out var original) && original.Length >= 32)
        {
            _data.AsSpan(original.Offset, 32).CopyTo(post);
        }
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000);
        return post;
    }

    private static byte[] WriteFont(SortedDictionary<string, byte[]> tables)
    {
        var count = tables.Count;
        var entrySelector = (int)Math.Floor(Math.Log2(count));
        var searchRange = (1 << entrySelector) * 16;

        using var font = new MemoryStream();
        var header = new byte[12 + count * 16];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)(count * 16 - searchRange));
        font.Write(header);

        var headOffset = 0;
        var i = 0;
        foreach (var (tag, data) in tables)
        {
            var record = 12 + i++ * 16;
            if (tag == "head") headOffset = (int)font.Position;
            Encoding.ASCII.GetBytes(tag).CopyTo(header, record);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 4), Checksum(data));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 8), (uint)font.Position);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 12), (uint)data.Length);
            font.Write(data);
            while (font.Position % 4 != 0) font.WriteByte(0);
        }

        font.Position = 0;
        font.Write(header);
        var bytes = font.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(headOffset + 8), 0xB1B0AFBA - Checksum(bytes));
        return bytes;
    }

    // The sum of the data as big-endian 32-bit words, the last one zero-padded.
    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var i = 0;
        for (; i + 4 <= data.Length; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(data[i..]);
        }

        if (i < data.Length)
        {
            Span<byte> last = stackalloc byte[4];
            last.Clear();
            data[i..].CopyTo(last);
            sum += BinaryPrimitives.ReadUInt32BigEndian(last);
        }
        return sum;
    }
}
