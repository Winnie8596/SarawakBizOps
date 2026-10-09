using System.Globalization;
using SarawakBizOps.Api.Services.Reports.Pdf;

namespace SarawakBizOps.Api.Services.Reports;

/// <summary>
/// Lays a <see cref="ServiceReportModel"/> out as an A4 PDF: header and footer on every page, the job's
/// details, parts with RM totals, photo thumbnails and the customer's signature. Pure — the images are handed
/// in already prepared — so the same input always renders the same bytes.
/// </summary>
public static class ServiceReportRenderer
{
    private const double Left = 48;
    private const double Right = PdfPage.Width - 48;
    private const double ContentWidth = Right - Left;
    private const double Top = PdfPage.Height - 40;
    private const double Bottom = 56;

    private const double BodySize = 10;
    private const double BodyLeading = 13.5;
    private const double LabelSize = 8;
    private const double Muted = 0.4;

    /// <param name="photos">One entry per <see cref="ServiceReportModel.Photos"/> item, in the same order; null
    /// where the photo is not pictured (a type a PDF cannot hold, past the size cap, or unreadable).</param>
    /// <param name="signature">The customer's signature, or null if it could not be read.</param>
    public static byte[] Render(ServiceReportModel model, IReadOnlyList<PdfImage?> photos, PdfImage? signature)
    {
        if (photos.Count != model.Photos.Count)
        {
            throw new ArgumentException("There must be one image slot per photo.", nameof(photos));
        }

        var layout = new Layout(model);
        layout.Summary();

        layout.Heading("Customer and equipment");
        layout.TwoColumns(
            new (string, string?)[]
            {
                ("Customer", model.Customer.CompanyName),
                ("Contact", model.Customer.ContactPerson),
                ("Phone", model.Customer.Phone),
                ("Email", model.Customer.Email),
                ("Address", model.Customer.Address)
            },
            new (string, string?)[]
            {
                ("Equipment", $"{model.Equipment.EquipmentType} ({model.Equipment.SerialNumber})"),
                ("Brand / model", JoinNonEmpty(" / ", model.Equipment.Brand, model.Equipment.Model)),
                ("Location", model.Equipment.Location)
            });

        layout.Heading("Job");
        layout.TwoColumns(
            new (string, string?)[] { ("Technician", model.TechnicianName) },
            new (string, string?)[] { ("Priority", model.Priority) });
        layout.Paragraph("Problem reported", model.ProblemDescription);
        layout.Paragraph("Diagnosis", model.Diagnosis);
        layout.Paragraph("Work performed", model.WorkPerformed);
        if (model.Notes is not null)
        {
            layout.Paragraph("Notes", model.Notes);
        }

        layout.Heading("Parts used");
        layout.Parts(model.Parts, model.PartsTotal);

        layout.Heading("Timeline (MYT)", keepWith: model.Timeline.Count * BodyLeading);
        layout.Timeline(model.Timeline);

        if (model.Photos.Count > 0)
        {
            layout.Heading($"Photos ({model.Photos.Count})");
            layout.Photos(model.Photos, photos);
        }

        layout.Heading("Customer signature", keepWith: 120);
        layout.Signature(model, signature);

        return layout.Finish();
    }

    internal static string Date(DateTime myt) => myt.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    internal static string Money(decimal amount) => $"RM {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    internal static string Quantity(decimal quantity) => quantity.ToString("0.###", CultureInfo.InvariantCulture);

    private static string? JoinNonEmpty(string separator, params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join(separator, present);
    }

    /// <summary>Greedy word wrap to <paramref name="width"/>; words longer than a line are broken.</summary>
    public static List<string> Wrap(string text, PdfFont font, double size, double width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (font.Width(candidate, size) <= width)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                }

                // Text without spaces (a long code, or a sentence in Chinese) breaks between characters, never
                // inside a surrogate pair.
                line = word;
                while (font.Width(line, size) > width && NextCharacter(line, 0) < line.Length)
                {
                    var fit = NextCharacter(line, 0);
                    while (fit < line.Length && font.Width(line[..NextCharacter(line, fit)], size) <= width)
                    {
                        fit = NextCharacter(line, fit);
                    }
                    lines.Add(line[..fit]);
                    line = line[fit..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    private static int NextCharacter(string text, int index)
        => index + (char.IsSurrogatePair(text, index) ? 2 : 1);

    // Keeps the cursor (y, falling down the page) and starts new pages as content runs out of room.
    private sealed class Layout
    {
        private readonly ServiceReportModel _model;
        private readonly PdfDocument _document = new();
        private PdfPage _page = null!;
        private double _y;

        public Layout(ServiceReportModel model)
        {
            _model = model;
            NewPage();
        }

        private void NewPage()
        {
            _page = _document.AddPage();
            _page.Text(Left, Top - 12, "SarawakBizOps", PdfFont.Bold, 15);
            _page.Text(Left, Top - 26, "Field service report", PdfFont.Regular, 9, Muted);
            RightText(Top - 12, _model.ReportNumber, PdfFont.Bold, 13);
            RightText(Top - 26, $"Revision {_model.Revision}", PdfFont.Regular, 9, Muted);
            _page.Line(Left, Top - 36, Right, Top - 36, 1);
            _y = Top - 52;
        }

        private void Ensure(double height)
        {
            if (_y - height < Bottom)
            {
                NewPage();
            }
        }

        private void RightText(double y, string text, PdfFont font, double size, double grey = 0, double right = Right)
            => _page.Text(right - font.Width(text, size), y, text, font, size, grey);

        public void Summary()
        {
            const double height = 40;
            _page.Rectangle(Left, _y - height, ContentWidth, height, fillGrey: 0.95);
            var column = ContentWidth / 3;
            var items = new[]
            {
                ("Work order", $"#{_model.WorkOrderId}"),
                ("Service request", $"#{_model.ServiceRequestId}"),
                ("Generated (MYT)", $"{Date(_model.GeneratedAtMyt)} by {_model.GeneratedByName}")
            };
            for (var i = 0; i < items.Length; i++)
            {
                var x = Left + 10 + i * column;
                _page.Text(x, _y - 14, items[i].Item1.ToUpperInvariant(), PdfFont.Bold, LabelSize - 1, Muted);
                var value = Wrap(items[i].Item2, PdfFont.Regular, 9, column - 14);
                _page.Text(x, _y - 27, value[0], PdfFont.Regular, 9);
                if (value.Count > 1)
                {
                    _page.Text(x, _y - 37, value[1], PdfFont.Regular, 9);
                }
            }
            _y -= height + 8;
        }

        /// <param name="keepWith">How much of what follows must fit on the same page (short sections stay whole).</param>
        public void Heading(string title, double keepWith = 22)
        {
            Ensure(36 + keepWith); // never strand a heading at the foot of a page
            _y -= 10;
            _page.Text(Left, _y - 11, title, PdfFont.Bold, 11.5);
            _page.Line(Left, _y - 16, Right, _y - 16, 0.5, 0.7);
            _y -= 26;
        }

        private double Field(double x, double y, double width, string label, string value)
        {
            _page.Text(x, y - LabelSize, label.ToUpperInvariant(), PdfFont.Bold, LabelSize - 1, Muted);
            y -= LabelSize + 4;
            foreach (var line in Wrap(value, PdfFont.Regular, BodySize, width))
            {
                _page.Text(x, y - BodySize, line, PdfFont.Regular, BodySize);
                y -= BodyLeading;
            }
            return y - 4;
        }

        private static double FieldHeight(double width, string value)
            => LabelSize + 4 + Wrap(value, PdfFont.Regular, BodySize, width).Count * BodyLeading + 4;

        /// <summary>Two stacks of label/value fields side by side; empty values are left out.</summary>
        public void TwoColumns(IEnumerable<(string Label, string? Value)> left, IEnumerable<(string Label, string? Value)> right)
        {
            const double gap = 20;
            var width = (ContentWidth - gap) / 2;
            var columns = new[] { left, right }
                .Select(c => c.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => (f.Label, Value: f.Value!)).ToList())
                .ToArray();

            Ensure(columns.Max(c => c.Sum(f => FieldHeight(width, f.Value))));
            var ends = new double[2];
            for (var i = 0; i < 2; i++)
            {
                var y = _y;
                foreach (var (label, value) in columns[i])
                {
                    y = Field(Left + i * (width + gap), y, width, label, value);
                }
                ends[i] = y;
            }
            _y = Math.Min(ends[0], ends[1]);
        }

        /// <summary>A labelled block of wrapped text that may run over onto the next page.</summary>
        public void Paragraph(string label, string text)
        {
            var lines = Wrap(string.IsNullOrWhiteSpace(text) ? "—" : text, PdfFont.Regular, BodySize, ContentWidth);
            Ensure(LabelSize + 4 + BodyLeading * Math.Min(lines.Count, 3));
            _page.Text(Left, _y - LabelSize, label.ToUpperInvariant(), PdfFont.Bold, LabelSize - 1, Muted);
            _y -= LabelSize + 4;
            foreach (var line in lines)
            {
                Ensure(BodyLeading);
                _page.Text(Left, _y - BodySize, line, PdfFont.Regular, BodySize);
                _y -= BodyLeading;
            }
            _y -= 6;
        }

        // Column right edges (or left edge for the text columns) of the parts table.
        private static readonly double PartNumberX = Left + 4;
        private static readonly double PartNameX = Left + 104;
        private const double PartNameWidth = 190;
        private static readonly double QuantityRight = Left + 350;
        private static readonly double UnitPriceRight = Left + 425;
        private static readonly double LineTotalRight = Right - 4;

        public void Parts(IReadOnlyList<ReportPartLine> parts, decimal total)
        {
            if (parts.Count == 0)
            {
                Ensure(BodyLeading);
                _page.Text(Left, _y - BodySize, "No parts were used on this job.", PdfFont.Regular, BodySize, Muted);
                _y -= BodyLeading + 4;
                return;
            }

            PartsHeader();
            foreach (var part in parts)
            {
                var name = Wrap(part.PartName, PdfFont.Regular, 9, PartNameWidth);
                var height = name.Count * 12 + 6;
                if (_y - height < Bottom)
                {
                    NewPage();
                    PartsHeader();
                }

                var baseline = _y - 12;
                _page.Text(PartNumberX, baseline, part.PartNumber, PdfFont.Regular, 9);
                for (var i = 0; i < name.Count; i++)
                {
                    _page.Text(PartNameX, baseline - i * 12, name[i], PdfFont.Regular, 9);
                }
                RightText(baseline, $"{Quantity(part.Quantity)} {part.Unit}", PdfFont.Regular, 9, right: QuantityRight);
                RightText(baseline, Money(part.UnitPrice), PdfFont.Regular, 9, right: UnitPriceRight);
                RightText(baseline, Money(part.LineTotal), PdfFont.Regular, 9, right: LineTotalRight);
                _y -= height;
                _page.Line(Left, _y, Right, _y, 0.3, 0.85);
            }

            Ensure(20);
            _page.Text(PartNameX, _y - 14, "Parts total", PdfFont.Bold, 10);
            RightText(_y - 14, Money(total), PdfFont.Bold, 10, right: LineTotalRight);
            _y -= 26;
        }

        private void PartsHeader()
        {
            Ensure(40);
            _page.Rectangle(Left, _y - 18, ContentWidth, 18, fillGrey: 0.93);
            var baseline = _y - 12.5;
            _page.Text(PartNumberX, baseline, "Part no.", PdfFont.Bold, 8.5);
            _page.Text(PartNameX, baseline, "Description", PdfFont.Bold, 8.5);
            RightText(baseline, "Quantity", PdfFont.Bold, 8.5, right: QuantityRight);
            RightText(baseline, "Unit price", PdfFont.Bold, 8.5, right: UnitPriceRight);
            RightText(baseline, "Line total", PdfFont.Bold, 8.5, right: LineTotalRight);
            _y -= 18;
        }

        public void Timeline(IReadOnlyList<ReportTimestamp> timeline)
        {
            foreach (var entry in timeline)
            {
                Ensure(BodyLeading);
                _page.Text(Left, _y - BodySize, entry.Label, PdfFont.Regular, BodySize, Muted);
                _page.Text(Left + 130, _y - BodySize, Date(entry.AtMyt), PdfFont.Regular, BodySize);
                _y -= BodyLeading;
            }
            _y -= 4;
        }

        public void Photos(IReadOnlyList<ReportPhoto> photos, IReadOnlyList<PdfImage?> images)
        {
            const int perRow = 3;
            const double gap = 12;
            const double boxHeight = 118;
            const double rowHeight = boxHeight + 28;
            var boxWidth = (ContentWidth - gap * (perRow - 1)) / perRow;

            for (var i = 0; i < photos.Count; i++)
            {
                var column = i % perRow;
                if (column == 0)
                {
                    if (i > 0) _y -= rowHeight;
                    Ensure(rowHeight);
                }

                var x = Left + column * (boxWidth + gap);
                var top = _y;
                _page.Rectangle(x, top - boxHeight, boxWidth, boxHeight, fillGrey: 0.96, strokeGrey: 0.85);
                if (images[i] is { } image)
                {
                    Fit(image, x + 3, top - boxHeight + 3, boxWidth - 6, boxHeight - 6);
                }
                else
                {
                    var message = Wrap("Not pictured in the PDF — open this photo in the app.", PdfFont.Regular, 8, boxWidth - 16);
                    for (var l = 0; l < message.Count; l++)
                    {
                        _page.Text(x + 8, top - boxHeight / 2 + 4 - l * 10, message[l], PdfFont.Regular, 8, Muted);
                    }
                }

                var photo = photos[i];
                _page.Text(x, top - boxHeight - 11, $"Photo {photo.Number} · {Date(photo.TakenAtMyt)}", PdfFont.Regular, 8, Muted);
            }

            _y -= rowHeight;
        }

        public void Signature(ServiceReportModel model, PdfImage? signature)
        {
            const double boxWidth = 240;
            const double boxHeight = 90;
            Ensure(boxHeight + 30);

            _page.Rectangle(Left, _y - boxHeight, boxWidth, boxHeight, fillGrey: 1, strokeGrey: 0.8);
            if (signature is not null)
            {
                Fit(signature, Left + 4, _y - boxHeight + 4, boxWidth - 8, boxHeight - 8);
            }
            else
            {
                var text = model.HasSignature ? "The stored signature could not be read." : "Not signed.";
                _page.Text(Left + 10, _y - boxHeight / 2 - 3, text, PdfFont.Regular, 9, Muted);
            }

            var signed = model.Timeline.FirstOrDefault(t => t.Label == "Customer signed");
            _y -= boxHeight + 4;
            _page.Text(Left, _y - 9, signed is null
                ? $"Customer: {model.Customer.CompanyName}"
                : $"Signed for {model.Customer.CompanyName} on {Date(signed.AtMyt)} (MYT)", PdfFont.Regular, 8.5, Muted);
            _y -= 20;
        }

        // Scales the image to fit inside the box, centred, keeping its (upright) proportions.
        private void Fit(PdfImage image, double x, double y, double width, double height)
        {
            var aspect = image.DisplayAspect;
            var w = width;
            var h = w / aspect;
            if (h > height)
            {
                h = height;
                w = h * aspect;
            }
            _page.Image(image, x + (width - w) / 2, y + (height - h) / 2, w, h);
        }

        public byte[] Finish()
        {
            var pages = _document.Pages;
            for (var i = 0; i < pages.Count; i++)
            {
                _page = pages[i];
                _page.Line(Left, Bottom - 16, Right, Bottom - 16, 0.5, 0.8);
                _page.Text(Left, Bottom - 28, "All times are Malaysia time (MYT, UTC+8). Amounts are in Malaysian ringgit (RM).",
                    PdfFont.Regular, 7.5, Muted);
                RightText(Bottom - 28, $"{_model.ReportNumber} · Page {i + 1} of {pages.Count}", PdfFont.Regular, 7.5, Muted);
            }

            return _document.Save($"Service report {_model.ReportNumber}", _model.GeneratedAtMyt - ReportRules.MytOffset);
        }
    }
}
