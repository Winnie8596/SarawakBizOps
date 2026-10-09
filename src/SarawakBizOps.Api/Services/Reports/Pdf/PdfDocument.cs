using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SarawakBizOps.Api.Services.Reports.Pdf;

/// <summary>
/// A deliberately small PDF 1.4 writer — pages of text, lines, rectangles and images, the two standard
/// Helvetica fonts — which is all the service report needs. Hand-rolled instead of a PDF library (PRD D-06,
/// Open Question 3): no licence to vet for a public MIT repository, no native graphics dependency in the Docker
/// image, and output that is byte-for-byte deterministic for the same input. Text Helvetica cannot show is
/// drawn with a subset of the embedded fallback font (<see cref="PdfFont.Fallback"/>), written only when a
/// document uses it, so a report in Latin script has no embedded font at all.
/// Coordinates are PDF points (1/72 inch) with the origin at the bottom-left of an A4 page.
/// </summary>
public sealed class PdfDocument
{
    private readonly List<PdfPage> _pages = new();
    private readonly List<PdfImage> _images = new();
    private EmbeddedGlyphs? _embedded;

    /// <summary>The resource name of the embedded fallback font in content streams.</summary>
    internal const string EmbeddedFontResource = "F3";

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage()
    {
        var page = new PdfPage(this);
        _pages.Add(page);
        return page;
    }

    // One XObject per distinct image, however often it is drawn.
    internal string ImageName(PdfImage image)
    {
        var index = _images.IndexOf(image);
        if (index < 0)
        {
            _images.Add(image);
            index = _images.Count - 1;
        }
        return $"Im{index + 1}";
    }

    // The fallback glyphs this document draws, created on the first character Helvetica cannot show.
    internal EmbeddedGlyphs Embedded => _embedded ??= new EmbeddedGlyphs(PdfFont.Fallback);

    /// <summary>Serialises the document. <paramref name="title"/> goes into the document information.</summary>
    public byte[] Save(string title, DateTime createdUtc)
    {
        if (_pages.Count == 0)
        {
            AddPage();
        }

        // Object numbers: 1 catalog, 2 page tree, 3–4 fonts, 5 info, then the images, then (only if used) the
        // objects of the embedded font, then per page its content stream and the page itself.
        const int firstImage = 6;
        var embeddedFont = firstImage + _images.Count;
        var firstPage = embeddedFont + (_embedded is null ? 0 : EmbeddedGlyphs.ObjectCount);
        int ContentObject(int page) => firstPage + page * 2;
        int PageObject(int page) => firstPage + page * 2 + 1;

        using var output = new MemoryStream();
        var offsets = new List<long>();

        void Write(string text) => output.Write(Encoding.ASCII.GetBytes(text));

        void BeginObject(int number)
        {
            while (offsets.Count < number) offsets.Add(0);
            offsets[number - 1] = output.Position;
            Write($"{number} 0 obj\n");
        }

        void WriteStream(int number, string dictionary, byte[] data)
        {
            BeginObject(number);
            Write($"<< {dictionary} /Length {data.Length} >>\nstream\n");
            output.Write(data);
            Write("\nendstream\nendobj\n");
        }

        // The header, then a comment of high bytes that tells transfer tools the file is binary.
        Write("%PDF-1.4\n");
        output.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        BeginObject(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(p => $"{PageObject(p)} 0 R"));
        Write($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>\nendobj\n");

        foreach (var (font, number) in new[] { (PdfFont.Regular, 3), (PdfFont.Bold, 4) })
        {
            BeginObject(number);
            Write($"<< /Type /Font /Subtype /Type1 /BaseFont /{font.BaseFont} /Encoding /WinAnsiEncoding >>\nendobj\n");
        }

        BeginObject(5);
        Write($"<< /Title {PdfPage.TextString(title)} /Producer (SarawakBizOps) " +
              $"/CreationDate (D:{createdUtc:yyyyMMddHHmmss}Z) >>\nendobj\n");

        for (var i = 0; i < _images.Count; i++)
        {
            var image = _images[i];
            WriteStream(firstImage + i,
                $"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} " +
                $"/ColorSpace /{image.ColorSpace} /BitsPerComponent 8 /Filter /{image.Filter}",
                image.Data);
        }

        var fonts = "/F1 3 0 R /F2 4 0 R";
        if (_embedded is not null)
        {
            foreach (var (number, dictionary, data) in _embedded.Objects(embeddedFont))
            {
                if (data is null)
                {
                    BeginObject(number);
                    Write($"{dictionary}\nendobj\n");
                }
                else
                {
                    WriteStream(number, dictionary, data);
                }
            }
            fonts += $" /{EmbeddedFontResource} {embeddedFont} 0 R";
        }

        var xObjects = _images.Count == 0
            ? string.Empty
            : " /XObject << " + string.Join(" ", _images.Select((_, i) => $"/Im{i + 1} {firstImage + i} 0 R")) + " >>";

        for (var p = 0; p < _pages.Count; p++)
        {
            WriteStream(ContentObject(p), "/Filter /FlateDecode", Deflate(_pages[p].ContentBytes()));

            BeginObject(PageObject(p));
            Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PdfPage.Number(PdfPage.Width)} {PdfPage.Number(PdfPage.Height)}] " +
                  $"/Resources << /Font << {fonts} >>{xObjects} >> /Contents {ContentObject(p)} 0 R >>\nendobj\n");
        }

        var xref = output.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        return output.ToArray();
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

/// <summary>One A4 page: drawing calls append PDF content-stream operators.</summary>
public sealed class PdfPage
{
    public const double Width = 595.28;
    public const double Height = 841.89;

    private readonly PdfDocument _document;
    private readonly StringBuilder _content = new();

    internal PdfPage(PdfDocument document) => _document = document;

    /// <summary>Draws one line of text with its baseline at <paramref name="y"/>. Grey is 0 (black) to 1 (white).</summary>
    public void Text(double x, double y, string text, PdfFont font, double size, double grey = 0)
    {
        if (text.Length == 0)
        {
            return;
        }

        var runs = PdfFont.Runs(text);
        if (runs is [{ Embedded: false }])
        {
            _content.Append($"BT {Number(grey)} g /{font.ResourceName} {Number(size)} Tf 1 0 0 1 {Number(x)} {Number(y)} Tm ")
                .Append(LiteralString(PdfFont.Encode(text)))
                .Append(" Tj ET\n");
            return;
        }

        // Mixed scripts: each run in its own font, one after the other. Tj moves the pen by the glyph widths,
        // which are the ones Width() measured, so wrapping and right-alignment still line up.
        _content.Append($"BT {Number(grey)} g 1 0 0 1 {Number(x)} {Number(y)} Tm ");
        foreach (var run in runs)
        {
            if (!run.Embedded)
            {
                _content.Append($"/{font.ResourceName} {Number(size)} Tf ")
                    .Append(LiteralString(PdfFont.Encode(run.Text))).Append(" Tj ");
            }
            else if (font == PdfFont.Bold)
            {
                // The fallback has a single weight: bold is its outline filled and also stroked (render mode 2),
                // which thickens every stroke by 3% of the type size.
                _content.Append($"/{PdfDocument.EmbeddedFontResource} {Number(size)} Tf {Number(grey)} G {Number(size * 0.03)} w 2 Tr ")
                    .Append(_document.Embedded.Hex(run.Text)).Append(" Tj 0 Tr ");
            }
            else
            {
                _content.Append($"/{PdfDocument.EmbeddedFontResource} {Number(size)} Tf ")
                    .Append(_document.Embedded.Hex(run.Text)).Append(" Tj ");
            }
        }
        _content.Append("ET\n");
    }

    public void Line(double x1, double y1, double x2, double y2, double width = 0.5, double grey = 0)
        => _content.Append($"{Number(grey)} G {Number(width)} w {Number(x1)} {Number(y1)} m {Number(x2)} {Number(y2)} l S\n");

    /// <summary>A rectangle filled and/or outlined; null leaves that part out.</summary>
    public void Rectangle(double x, double y, double width, double height, double? fillGrey = null, double? strokeGrey = null)
    {
        if (fillGrey is null && strokeGrey is null)
        {
            return;
        }

        var paint = (fillGrey, strokeGrey) switch
        {
            (not null, not null) => "B",
            (not null, null) => "f",
            _ => "S"
        };
        _content.Append(fillGrey is { } f ? $"{Number(f)} g " : string.Empty)
            .Append(strokeGrey is { } s ? $"{Number(s)} G 0.5 w " : string.Empty)
            .Append($"{Number(x)} {Number(y)} {Number(width)} {Number(height)} re {paint}\n");
    }

    /// <summary>Draws <paramref name="image"/> upright (EXIF orientation applied) filling the given box.</summary>
    public void Image(PdfImage image, double x, double y, double width, double height)
    {
        var name = _document.ImageName(image);
        var (a, b, c, d, e, f) = image.OrientationMatrix;
        _content.Append($"q {Number(width * a)} {Number(height * b)} {Number(width * c)} {Number(height * d)} " +
                        $"{Number(x + width * e)} {Number(y + height * f)} cm /{name} Do Q\n");
    }

    internal byte[] ContentBytes() => Encoding.ASCII.GetBytes(_content.ToString());

    internal static string Number(double value)
        => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    internal static string LiteralString(string text) => LiteralString(PdfFont.Encode(text));

    /// <summary>
    /// A PDF text string for the document information: a literal when it is printable ASCII, otherwise UTF-16BE
    /// with a byte order mark, so a title in any script survives.
    /// </summary>
    internal static string TextString(string text)
        => text.All(c => c is >= ' ' and <= '~')
            ? LiteralString(text)
            : "<FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text)) + ">";

    // A PDF literal string kept in 7-bit ASCII: ( ) \ escaped, anything outside printable ASCII as \ooo.
    private static string LiteralString(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length + 2).Append('(');
        foreach (var b in bytes)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
            {
                builder.Append('\\').Append((char)b);
            }
            else if (b is >= 32 and <= 126)
            {
                builder.Append((char)b);
            }
            else
            {
                builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            }
        }
        return builder.Append(')').ToString();
    }
}
