using System.Globalization;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using SkiaSharp;

namespace MasterFlow.Reporting;

/// <summary>One calculation for a PDF report.</summary>
public sealed record PdfReportItem(ChannelCalculation Calculation, UniformFlowResult Result, ReportOptions Options);

/// <summary>
/// Native PDF calculation reports (MasterFlow's own layout), written with SkiaSharp's PDF backend
/// (MIT licence). Text is real, selectable text with embedded font subsets. Several calculations
/// make one document, each starting on a new page, numbered "Page n of N" throughout.
/// </summary>
public static class PdfReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const float LetterWidth = 612, LetterHeight = 792; // points (8.5 × 11 in)
    public const string Disclaimer = "Computed by MasterFlow. The responsible engineer must review inputs, assumptions and results before use.";

    public static byte[] ToBytes(ProjectInfo project, IReadOnlyList<PdfReportItem> items)
    {
        using var ms = new MemoryStream();
        Write(ms, project, items);
        return ms.ToArray();
    }

    public static void Write(Stream output, ProjectInfo project, IReadOnlyList<PdfReportItem> items)
    {
        if (items.Count == 0) throw new ArgumentException("At least one calculation is required.", nameof(items));
        var fonts = new PdfFonts();
        var pager = new PdfPager(fonts, LetterWidth, LetterHeight);
        foreach (var item in items) Calculation(pager, project, item);
        var o = items[0].Options;
        Render(output, pager, items.Count == 1 ? items[0].Calculation.Title : project.Name + " — calculations", project, o);
    }

    public static byte[] ComparisonToBytes(ProjectInfo project, ComparisonTable table, ReportOptions? options = null)
    {
        using var ms = new MemoryStream();
        WriteComparison(ms, project, table, options);
        return ms.ToArray();
    }

    /// <summary>Alternative comparison on landscape pages; more than six alternatives continue in further bands.</summary>
    public static void WriteComparison(Stream output, ProjectInfo project, ComparisonTable table, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        var fonts = new PdfFonts();
        var pager = new PdfPager(fonts, LetterHeight, LetterWidth);
        pager.StartSection("Alternative comparison — " + project.Name, Context(options));
        pager.Add(new TextBlock(fonts, "Alternative comparison", fonts.Bold, PdfStyle.H1, PdfStyle.Ink, 2));
        string sub = string.Join(" · ", new[] { project.Name, project.ProjectNumber, string.IsNullOrWhiteSpace(project.PreparedBy) ? "" : "Prepared by " + project.PreparedBy,
            "MasterFlow " + options.SoftwareVersion, Stamp(options) }.Where(s => !string.IsNullOrWhiteSpace(s)));
        pager.Add(new TextBlock(fonts, sub, fonts.Regular, PdfStyle.Body, PdfStyle.Muted, 8));

        const int perBand = 6;
        for (int start = 0; start < Math.Max(1, table.Titles.Count); start += perBand)
        {
            var idx = Enumerable.Range(start, Math.Min(perBand, table.Titles.Count - start)).ToList();
            var cols = new List<PdfColumn> { new(3.2f), new(1.1f) };
            cols.AddRange(idx.Select(_ => new PdfColumn(1.6f, Right: true)));
            var header = new List<string> { "Quantity", "Unit" };
            header.AddRange(idx.Select(k => table.Titles[k]));
            var rows = new List<(IReadOnlyList<PdfCell>, bool)>();
            var status = new List<PdfCell> { "Status", "" };
            status.AddRange(idx.Select(k => new PdfCell(table.Statuses[k])));
            rows.Add((status, false));
            string? group = null;
            foreach (var r in table.Rows)
            {
                if (r.Group != group)
                {
                    group = r.Group;
                    rows.Add(([new PdfCell(r.Group.ToUpperInvariant())], true));
                }
                var cells = new List<PdfCell> { new(r.Label, r.Symbol), r.Unit };
                cells.AddRange(idx.Select(k => new PdfCell(r.Format(k))));
                rows.Add((cells, false));
            }
            if (start > 0) pager.NewPage();
            if (table.Titles.Count > perBand)
                pager.Add(new TextBlock(fonts, $"Alternatives {start + 1}–{start + idx.Count} of {table.Titles.Count}", fonts.Bold, PdfStyle.Body, PdfStyle.Accent));
            pager.AddTable(new TableBlock(fonts, cols, header, rows, PdfStyle.Body - 0.5f));
        }
        foreach (var n in table.Notes) pager.Paragraph(n, color: PdfStyle.Muted, spaceAfter: 2);
        Render(output, pager, project.Name + " — comparison", project, options);
    }

    // ------------------------------------------------------------------ calculation report

    internal static PdfPager.PageContext Context(ReportOptions o) =>
        new(string.IsNullOrWhiteSpace(o.Watermark) ? null : o.Watermark, string.IsNullOrWhiteSpace(o.FooterText) ? null : o.FooterText, Disclaimer);

    internal static string Stamp(ReportOptions o) => (o.Timestamp ?? DateTimeOffset.Now).ToString("yyyy-MM-dd HH:mm", Inv);

    private static void Calculation(PdfPager pager, ProjectInfo project, PdfReportItem item)
    {
        var (calc, result, o) = (item.Calculation, item.Result, item.Options);
        var fonts = pager.Fonts;
        var c = result.Constants;
        pager.StartSection(calc.Title + " — " + project.Name, Context(o));

        // Header: title, project, meta, optional logo at right.
        var logo = DecodeLogo(o.LogoDataUri);
        var meta = new List<(string, string)>();
        void M(string k, string v) { if (!string.IsNullOrWhiteSpace(v)) meta.Add((k, v)); }
        M("Item No.", calc.ItemNumber); M("Client", project.Client); M("Prepared by", project.PreparedBy); M("Checked by", project.CheckedBy);
        M("Date", Stamp(o)); M("Software", "MasterFlow " + o.SoftwareVersion);
        string sub = project.Name + (string.IsNullOrWhiteSpace(project.ProjectNumber) ? "" : " · No. " + project.ProjectNumber);
        pager.Add(new HeaderBlock(fonts, calc.Title, sub, meta, logo));

        if (!string.IsNullOrWhiteSpace(calc.Description)) pager.Paragraph(calc.Description);

        pager.Heading("Method");
        pager.Paragraph($"Steady, uniform open-channel flow by Manning's equation, Q = (k/n)·A·R^(2/3)·S^(1/2), with k = {c.ManningK.ToString(Inv)} and R = A/P. " +
            $"Critical depth satisfies Q²T/(gA³) = 1 with g = {c.Gravity.ToString(Inv)} {c.Length}/s². Froude number Fr = V/√(gD), D = A/T. " +
            "Unknown depths are solved with Brent's bracketed method (relative depth tolerance 1×10⁻¹², discharge residual ≤ 1×10⁻⁸). Gradually varied, unsteady and pressurised flow are not modelled.");

        // Input table beside the cross-section.
        var inputRows = ResultSummary.InputLines(result.Input).Select(l => ((IReadOnlyList<PdfCell>)[new PdfCell(l.Label), new PdfCell(l.Value)], false)).ToList();
        var input = new TableBlock(fonts, [new PdfColumn(1.1f), new PdfColumn(1.2f, Right: true)], null, inputRows, caption: "Input");
        var drawing = CrossSectionDrawing.Create(result.Input.Section, result.Succeeded ? result.Normal.Depth : null, result.Succeeded ? result.Critical.Depth : null);
        var section = new CrossSectionBlock(fonts, drawing, result.Input.Section, c.Length);
        pager.Space(6);
        pager.Add(new ColumnsBlock(input, section));

        pager.Heading("Results");
        if (!result.Succeeded)
            pager.Paragraph("No results: the calculation did not complete. See messages below.", fonts.Bold);
        else
        {
            pager.Paragraph($"Flow regime: {ResultSummary.RegimeText(result)}. Values in bold were solved for.");
            var lines = ResultSummary.Lines(result);
            var groups = lines.Where(l => l.Group != "Critical flow").GroupBy(l => l.Group).Select(g => ResultTable(fonts, g.Key, g)).ToList();
            for (int i = 0; i < groups.Count; i += 2)
                pager.AddTablePair(groups[i], i + 1 < groups.Count ? groups[i + 1] : null);
            var critical = lines.Where(l => l.Group == "Critical flow").ToList();
            if (critical.Count > 0)
            {
                var half = (critical.Count + 1) / 2;
                pager.AddTablePair(ResultTable(fonts, "Critical flow (same discharge)", critical.Take(half)), ResultTable(fonts, " ", critical.Skip(half)));
            }

            var subs = ResultSummary.Subareas(result);
            if (subs.Count > 0)
            {
                pager.Heading("Flow distribution (conveyance subdivision)");
                var rows = subs.Select(sa => ((IReadOnlyList<PdfCell>)[F(sa.StationLeft, "F2"), F(sa.StationRight, "F2"), F(sa.ManningN, "F3"), F(sa.Area, "F3"), F(sa.WettedPerimeter, "F3"),
                    F(sa.Conveyance, "F1"), F(sa.Discharge, "F3"), new PdfCell((100 * sa.Discharge / result.Discharge).ToString("F1", Inv) + " %"), F(sa.Velocity, "F3")], false)).ToList();
                pager.AddTable(new TableBlock(fonts, Enumerable.Repeat(new PdfColumn(1, Right: true), 9).ToList(),
                    [$"From sta. ({c.Length})", $"To sta. ({c.Length})", "n", $"A ({c.Area})", $"P ({c.Length})", $"K ({c.Discharge})", $"Q ({c.Discharge})", "Q share", $"V ({c.Velocity})"], rows, PdfStyle.Body - 0.5f));
            }
        }

        if (result.Input.Section is IrregularSection irs)
        {
            pager.Heading("Section points");
            var rows = new List<(IReadOnlyList<PdfCell>, bool)>();
            for (int i = 0; i < irs.Points.Count; i++)
            {
                var pt = irs.Points[i];
                string nTxt = i == irs.Points.Count - 1 ? "" : (pt.ManningN ?? result.Input.ManningN).ToString("F3", Inv) + (pt.ManningN is null ? " (default)" : "");
                rows.Add(([new PdfCell((i + 1).ToString(Inv)), F(pt.Station, "F2"), F(pt.Elevation, "F3"), new PdfCell(nTxt), new PdfCell(pt.Description ?? "")], false));
            }
            pager.AddTable(new TableBlock(fonts, [new(0.4f, true), new(1, true), new(1, true), new(1.2f, true), new(2.4f)],
                ["#", $"Station ({c.Length})", $"Elevation ({c.Length})", "n (to next point)", "Description"], rows));
        }

        pager.Heading("Warnings, notes and assumptions");
        foreach (var m in result.Messages)
        {
            var (bar, fill) = m.Severity switch
            {
                MessageSeverity.Error => (PdfStyle.Red, SKColor.Parse("#fdecea")),
                MessageSeverity.Warning => (PdfStyle.Amber, SKColor.Parse("#fff7e0")),
                _ => (PdfStyle.Accent, SKColor.Parse("#eef5fb")),
            };
            pager.Add(new MessageBlock(fonts, $"{m.Severity} [{m.Code}]", m.Text, bar, fill));
        }
        pager.Add(new MessageBlock(fonts, "Info", result.Input.Section is IrregularSection { HasPointRoughness: true }
            ? "Assumes a prismatic channel, small slope (depth measured vertically) and hydrostatic pressure. Roughness varies across the section and is combined as stated in the input; the velocity head uses the mean velocity (α = 1). Manning's n values are the engineer's selection; results are only as reliable as n."
            : "Assumes a prismatic channel, uniform roughness across the wetted perimeter, small slope (depth measured vertically) and hydrostatic pressure. Manning's n is the engineer's selection; results are only as reliable as n.",
            PdfStyle.Accent, SKColor.Parse("#eef5fb")));

        if (o.RatingTable is { Rows.Count: > 0 } rt)
        {
            pager.Heading("Rating table", keepWithNext: rt.Rows.Count >= 2 ? 200 : 48);
            if (rt.Rows.Count >= 2) pager.Add(new RatingChartBlock(fonts, rt, c));
            var rows = rt.Rows.Select(r => ((IReadOnlyList<PdfCell>)[F(r.Depth, "F3"), F(r.Discharge, "F3"), F(r.Area, "F3"), F(r.Velocity, "F3"), F(r.TopWidth, "F3"), F(r.Froude, "F3"), F(r.CriticalDepth, "F3")], false)).ToList();
            pager.AddTable(new TableBlock(fonts, Enumerable.Repeat(new PdfColumn(1, Right: true), 7).ToList(),
                [$"Depth ({c.Length})", $"Q ({c.Discharge})", $"A ({c.Area})", $"V ({c.Velocity})", $"T ({c.Length})", "Fr", $"yc ({c.Length})"], rows));
        }

        if (!string.IsNullOrWhiteSpace(project.Notes))
        {
            pager.Heading("Notes");
            pager.Paragraph(project.Notes);
        }

        if (o.IncludeCalculationTrace && result.Trace.Count > 0)
        {
            pager.Heading("Calculation trace");
            foreach (var t in result.Trace) pager.Paragraph(t, fonts.Mono, PdfStyle.Small, PdfStyle.Muted, spaceAfter: 0);
        }
    }

    private static PdfCell F(double v, string fmt) => new(double.IsFinite(v) ? v.ToString(fmt, Inv) : "—");

    private static TableBlock ResultTable(PdfFonts fonts, string title, IEnumerable<ResultLine> lines) =>
        new(fonts, [new PdfColumn(2.3f), new PdfColumn(1.1f, Right: true), new PdfColumn(0.7f)], null,
            lines.Select(l => ((IReadOnlyList<PdfCell>)[new PdfCell(l.Label, l.Symbol), new PdfCell(l.Formatted), new PdfCell(l.Unit)], l.IsSolved)).ToList(),
            caption: title);

    /// <summary>Raster data URI (PNG, JPEG, GIF) → image; SVG and anything undecodable is not drawn in PDF.</summary>
    internal static SKImage? DecodeLogo(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri) || !dataUri.StartsWith("data:image/", StringComparison.Ordinal)) return null;
        int comma = dataUri.IndexOf(',');
        if (comma < 0 || !dataUri[..comma].EndsWith(";base64", StringComparison.Ordinal)) return null;
        try { return SKImage.FromEncodedData(Convert.FromBase64String(dataUri[(comma + 1)..])); }
        catch (FormatException) { return null; }
    }

    public static bool LogoSupported(string? dataUri)
    {
        using var img = DecodeLogo(dataUri);
        return img is not null;
    }

    private static void Render(Stream output, PdfPager pager, string title, ProjectInfo project, ReportOptions o)
    {
        var when = (o.Timestamp ?? DateTimeOffset.Now).UtcDateTime;
        var meta = new SKDocumentPdfMetadata
        {
            Title = title,
            Author = project.PreparedBy,
            Subject = project.Name,
            Creator = "MasterFlow " + o.SoftwareVersion,
            Producer = "MasterFlow " + o.SoftwareVersion,
            Creation = when,
            Modified = when,
            RasterDpi = 300,
        };
        using var doc = SKDocument.CreatePdf(output, meta);
        pager.Render(doc);
        doc.Close();
    }

    // ------------------------------------------------------------------ blocks

    private sealed class HeaderBlock(PdfFonts fonts, string title, string sub, IReadOnlyList<(string K, string V)> meta, SKImage? logo) : IPdfBlock
    {
        private const float LogoMaxH = 48, LogoMaxW = 160;
        private (float W, float H) LogoSize()
        {
            if (logo is null) return (0, 0);
            float s = Math.Min(LogoMaxW / logo.Width, LogoMaxH / logo.Height);
            return (logo.Width * s, logo.Height * s);
        }
        private float TextWidth(float width) => width - (logo is null ? 0 : LogoSize().W + 12);

        public float Height(float width)
        {
            float tw = TextWidth(width);
            float h = fonts.Wrap(title, fonts.Bold, PdfStyle.H1, tw).Count * PdfStyle.Line(PdfStyle.H1)
                    + fonts.Wrap(sub, fonts.Regular, PdfStyle.Body, tw).Count * PdfStyle.Line(PdfStyle.Body) + 2
                    + meta.Count * PdfStyle.Line(PdfStyle.Body);
            return Math.Max(h, LogoSize().H) + 10;
        }

        public void Draw(SKCanvas c, float x, float y, float width)
        {
            float tw = TextWidth(width), ty = y;
            foreach (var l in fonts.Wrap(title, fonts.Bold, PdfStyle.H1, tw)) { fonts.Draw(c, l, x, ty + PdfStyle.H1, fonts.Bold, PdfStyle.H1, PdfStyle.Ink); ty += PdfStyle.Line(PdfStyle.H1); }
            foreach (var l in fonts.Wrap(sub, fonts.Regular, PdfStyle.Body, tw)) { fonts.Draw(c, l, x, ty + PdfStyle.Body, fonts.Regular, PdfStyle.Body, PdfStyle.Muted); ty += PdfStyle.Line(PdfStyle.Body); }
            ty += 2;
            float keyW = meta.Count == 0 ? 0 : meta.Max(m => fonts.Measure(m.K + ":", fonts.Regular, PdfStyle.Body)) + 10;
            foreach (var (k, v) in meta)
            {
                fonts.Draw(c, k + ":", x, ty + PdfStyle.Body, fonts.Regular, PdfStyle.Body, PdfStyle.Muted);
                fonts.Draw(c, v, x + keyW, ty + PdfStyle.Body, fonts.Regular, PdfStyle.Body, PdfStyle.Ink);
                ty += PdfStyle.Line(PdfStyle.Body);
            }
            if (logo is not null)
            {
                var (lw, lh) = LogoSize();
                using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                c.DrawImage(logo, SKRect.Create(x + width - lw, y, lw, lh), paint);
            }
            float bottom = y + Height(width) - 4;
            using var p = new SKPaint { Color = PdfStyle.Accent, StrokeWidth = 1.5f };
            c.DrawLine(x, bottom, x + width, bottom, p);
        }
    }

    /// <summary>Cross-section plot. Closed conduits are drawn to true scale; open channels may be exaggerated (labelled).</summary>
    private sealed class CrossSectionBlock(PdfFonts fonts, CrossSectionDrawing d, ChannelSection section, string unit) : IPdfBlock
    {
        private const float Caption = 16, PlotH = 150, Note = 14;
        private bool TrueScale => section is CircularSection or EllipticalSection;
        public float Height(float width) => Caption + PlotH + Note;

        public void Draw(SKCanvas c, float x, float y, float width)
        {
            fonts.Draw(c, "CROSS-SECTION", x + 4, y + 2.5f + PdfStyle.Body * 0.9f, fonts.Bold, PdfStyle.Body * 0.9f, PdfStyle.Muted);
            float top = y + Caption;
            using (var bg = new SKPaint { Color = PdfStyle.PlotBg }) c.DrawRect(x, top, width, PlotH, bg);
            const float pad = 14, padR = 92;
            if (d.Outline.Count < 2 || !(d.Width > 0) || !(d.Height > 0)) return;
            float sx = (float)((width - pad - padR) / d.Width), sy = (float)((PlotH - 2 * pad) / d.Height);
            if (TrueScale) sx = sy = Math.Min(sx, sy);
            double x0 = d.Outline.Min(p => p.X);
            float plotW = (float)(d.Width * sx);
            float left = x + pad + (TrueScale ? (width - pad - padR - plotW) / 2 : 0);
            float baseY = top + PlotH - pad;
            SKPoint P(double px, double py) => new(left + (float)((px - x0) * sx), baseY - (float)(py * sy));

            if (d.Water.Count > 2)
            {
                using var path = new SKPath();
                path.MoveTo(P(d.Water[0].X, d.Water[0].Y));
                foreach (var p in d.Water.Skip(1)) path.LineTo(P(p.X, p.Y));
                path.Close();
                using var fill = new SKPaint { Color = PdfStyle.Water.WithAlpha(0xD9), IsAntialias = true, Style = SKPaintStyle.Fill };
                c.DrawPath(path, fill);
            }
            using (var path = new SKPath())
            {
                path.MoveTo(P(d.Outline[0].X, d.Outline[0].Y));
                foreach (var p in d.Outline.Skip(1)) path.LineTo(P(p.X, p.Y));
                using var stroke = new SKPaint { Color = PdfStyle.Ink, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, StrokeJoin = SKStrokeJoin.Round };
                c.DrawPath(path, stroke);
            }
            void Level(double? depth, SKColor color, string label, bool dashed)
            {
                if (depth is not > 0) return;
                var (l, r) = d.ExtentAt(section, depth.Value);
                var a = P(l, depth.Value); var b = P(r, depth.Value);
                using var paint = new SKPaint { Color = color, StrokeWidth = 1.1f, IsAntialias = true, Style = SKPaintStyle.Stroke };
                if (dashed) paint.PathEffect = SKPathEffect.CreateDash([4f, 2.5f], 0);
                c.DrawLine(a, b, paint);
                fonts.Draw(c, $"{label} = {depth.Value.ToString("F3", Inv)} {unit}", b.X + 3, b.Y + 3, fonts.Regular, 7.5f, color);
            }
            Level(d.WaterDepth, PdfStyle.Accent, "yn", false);
            Level(d.CriticalDepth, PdfStyle.Red, "yc", true);
            string note = TrueScale ? $"Drawn to true scale. Units: {unit}" : $"Not to scale (horizontal and vertical scales differ). Units: {unit}";
            fonts.Draw(c, note, x + 4, top + PlotH + 10, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
        }
    }

    private sealed class RatingChartBlock(PdfFonts fonts, RatingTable table, UnitConstants units) : IPdfBlock
    {
        public float Height(float width) => 190;

        private static double Nice(double raw)
        {
            if (!(raw > 0)) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / mag;
            return (f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10) * mag;
        }

        public void Draw(SKCanvas c, float x, float y, float width)
        {
            var rows = table.Rows.Where(r => double.IsFinite(r.Discharge) && double.IsFinite(r.Depth)).OrderBy(r => r.Depth).ToList();
            if (rows.Count < 2) return;
            const float padL = 44, padR = 12, padT = 8, padB = 34;
            float w = width - padL - padR, h = Height(width) - padT - padB - 6;
            double gq = Nice(rows.Max(r => r.Discharge) / 5), gy = Nice(rows.Max(r => r.Depth) / 4);
            double qMax = Math.Ceiling(rows.Max(r => r.Discharge) / gq) * gq, yMax = Math.Ceiling(rows.Max(r => r.Depth) / gy) * gy;
            if (!(qMax > 0)) qMax = 1;
            if (!(yMax > 0)) yMax = 1;
            float X(double q) => x + padL + (float)(q / qMax * w);
            float Y(double d) => y + padT + h - (float)(d / yMax * h);
            using var grid = new SKPaint { Color = SKColor.Parse("#e3e8ee"), StrokeWidth = 0.6f };
            for (double d = 0; d <= yMax + 1e-9; d += gy)
            {
                c.DrawLine(X(0), Y(d), X(qMax), Y(d), grid);
                string t = d.ToString("0.##", Inv);
                fonts.Draw(c, t, x + padL - 5 - fonts.Measure(t, fonts.Regular, 7.5f), Y(d) + 2.5f, fonts.Regular, 7.5f, PdfStyle.Muted);
            }
            for (double q = 0; q <= qMax + 1e-9; q += gq)
            {
                c.DrawLine(X(q), Y(0), X(q), Y(yMax), grid);
                string t = q.ToString("0.##", Inv);
                fonts.Draw(c, t, X(q) - fonts.Measure(t, fonts.Regular, 7.5f) / 2, Y(0) + 11, fonts.Regular, 7.5f, PdfStyle.Muted);
            }
            using var line = new SKPaint { Color = PdfStyle.Accent, StrokeWidth = 1.5f, IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var path = new SKPath();
            path.MoveTo(X(rows[0].Discharge), Y(rows[0].Depth));
            foreach (var r in rows.Skip(1)) path.LineTo(X(r.Discharge), Y(r.Depth));
            c.DrawPath(path, line);
            using var dot = new SKPaint { Color = PdfStyle.Accent, IsAntialias = true };
            foreach (var r in rows) c.DrawCircle(X(r.Discharge), Y(r.Depth), 2f, dot);
            string xl = $"Discharge Q ({units.Discharge})";
            fonts.Draw(c, xl, x + padL + w / 2 - fonts.Measure(xl, fonts.Regular, 8) / 2, Y(0) + 25, fonts.Regular, 8, PdfStyle.Muted);
            string yl = $"Depth ({units.Length})";
            c.Save();
            c.Translate(x + 10, y + padT + h / 2);
            c.RotateDegrees(-90);
            fonts.Draw(c, yl, -fonts.Measure(yl, fonts.Regular, 8) / 2, 0, fonts.Regular, 8, PdfStyle.Muted);
            c.Restore();
        }
    }
}
