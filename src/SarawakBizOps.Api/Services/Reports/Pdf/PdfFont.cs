namespace SarawakBizOps.Api.Services.Reports.Pdf;

/// <summary>
/// The two standard-14 fonts the report uses (Helvetica and Helvetica-Bold). Every PDF viewer ships them, so
/// nothing is embedded and no font files or font library are needed (PRD D-06). Text is written in
/// WinAnsiEncoding; <see cref="Encode"/> maps a .NET string to those single bytes and <see cref="Width"/>
/// measures it with the fonts' published (AFM) glyph widths, which is what line wrapping needs.
/// </summary>
public sealed class PdfFont
{
    // Adobe's AFM widths (units per 1000 em) for codes 32–126 in WinAnsiEncoding (code 39 is quotesingle and 96
    // is grave there, not the curly quotes of StandardEncoding).
    private static readonly int[] HelveticaWidths =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, // space … /
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, // 0 … ?
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, // @ … O
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, // P … _
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, // ` … o
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584        // p … ~
    };

    private static readonly int[] HelveticaBoldWidths =
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
    };

    // Declared after the width tables above: static fields initialise in textual order.
    public static readonly PdfFont Regular = new("F1", "Helvetica", HelveticaWidths, fallbackWidth: 556);
    public static readonly PdfFont Bold = new("F2", "Helvetica-Bold", HelveticaBoldWidths, fallbackWidth: 611);

    /// <summary>The resource name used in content streams (/F1, /F2).</summary>
    public string ResourceName { get; }

    /// <summary>The PostScript name of the standard font.</summary>
    public string BaseFont { get; }

    private readonly int[] _asciiWidths;
    private readonly int _fallbackWidth;

    private PdfFont(string resourceName, string baseFont, int[] asciiWidths, int fallbackWidth)
    {
        ResourceName = resourceName;
        BaseFont = baseFont;
        _asciiWidths = asciiWidths;
        _fallbackWidth = fallbackWidth;
    }

    /// <summary>The width of <paramref name="text"/> in points at <paramref name="size"/>.</summary>
    public double Width(string text, double size)
    {
        var units = 0;
        foreach (var code in Encode(text))
        {
            units += GlyphWidth(code);
        }
        return units * size / 1000.0;
    }

    private int GlyphWidth(byte code)
    {
        if (code is >= 32 and <= 126)
        {
            return _asciiWidths[code - 32];
        }

        return code switch
        {
            0x85 or 0x97 => 1000,            // ellipsis, em dash
            0x96 => 556,                     // en dash
            0x91 or 0x92 => this == Bold ? 278 : 222, // single quotes
            0x93 or 0x94 => this == Bold ? 500 : 333, // double quotes
            0x95 => 350,                     // bullet
            0xA0 => 278,                     // no-break space
            0xB7 => 278,                     // middle dot
            0xD7 => 584,                     // multiplication sign
            _ => _fallbackWidth              // Latin-1 letters: close enough to a lower-case letter for wrapping
        };
    }

    /// <summary>
    /// The WinAnsiEncoding bytes for <paramref name="text"/>. Printable ASCII and Latin-1 map to themselves, the
    /// common typographic characters (dashes, curly quotes, bullet, ellipsis) to their WinAnsi codes, line breaks
    /// and tabs to a space, and anything the standard fonts cannot show (e.g. CJK) to '?'.
    /// </summary>
    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            bytes[i] = EncodeChar(text[i]);
        }
        return bytes;
    }

    private static byte EncodeChar(char c) => c switch
    {
        >= ' ' and <= '~' => (byte)c,
        '\t' or '\r' or '\n' => (byte)' ',
        >= ' ' and <= 'ÿ' => (byte)c,
        '–' => 0x96, // en dash
        '—' => 0x97, // em dash
        '‘' => 0x91,
        '’' => 0x92,
        '“' => 0x93,
        '”' => 0x94,
        '•' => 0x95, // bullet
        '…' => 0x85, // ellipsis
        '€' => 0x80, // euro
        _ => (byte)'?'
    };
}
