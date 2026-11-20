using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security;
using System.Text;
using MasterFlow.Application;
using MasterFlow.Application.Reports;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using SkiaSharp;

namespace MasterFlow.Reporting;

/// <summary>
/// Renders a <see cref="ReportDocument"/> (drainage report builder) as PDF, HTML or Word (.docx). Body pages are portrait
/// letter, appendix pages landscape letter; one page numbering runs through the whole PDF. Body headings are numbered
/// 1, 1.1 …; appendix headings keep their own text.
/// </summary>
public static class DrainageReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Heading numbers ("1", "1.2", or "" in the appendix), in document order.</summary>
    public static List<(ReportHeading Heading, string Number, bool Appendix)> Number(ReportDocument d)
    {
        var list = new List<(ReportHeading, string, bool)>();
        int a = 0, b = 0;
        foreach (var h in d.Body.OfType<ReportHeading>())
        {
            if (h.Level == 1) { a++; b = 0; list.Add((h, $"{a}", false)); }
            else { b++; list.Add((h, $"{a}.{b}", false)); }
        }
        foreach (var h in d.Appendix.OfType<ReportHeading>()) list.Add((h, "", true));
        return list;
    }

    private static string Label(string number, ReportHeading h) => number.Length == 0 ? h.Text : $"{number}  {h.Text}";

    // ------------------------------------------------------------------ PDF

    public static byte[] ToPdf(ProjectInfo project, ReportDocument d, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        var fonts = new PdfFonts();
        var ctx = PdfReport.Context(options);
        var numbers = Number(d);
        var tocEntries = numbers.Where(n => n.Heading.Level <= 2).ToList();
        const int TocPerPage = 40;
        int front = 1 + (d.TableOfContents ? Math.Max(1, (tocEntries.Count + TocPerPage - 1) / TocPerPage) : 0);

        var pages = new Dictionary<ReportHeading, int>(ReferenceEqualityComparer.Instance);
        var body = new PdfPager(fonts, PdfReport.LetterWidth, PdfReport.LetterHeight) { PageOffset = front };
        body.StartSection($"{d.Title} — {project.Name}", ctx);
        Lay(body, fonts, d.Body, numbers, pages, options);
        PdfPager? app = null;
        if (d.Appendix.Count > 0)
        {
            app = new PdfPager(fonts, PdfReport.LetterHeight, PdfReport.LetterWidth) { PageOffset = front + body.PageCount };
            app.StartSection($"{d.Title}: appendices — {project.Name}", ctx);
            Lay(app, fonts, d.Appendix, numbers, pages, options);
        }
        int total = front + body.PageCount + (app?.PageCount ?? 0);

        var fp = new PdfPager(fonts, PdfReport.LetterWidth, PdfReport.LetterHeight);
        fp.StartSection(d.Title, ctx);
        Cover(fp, fonts, project, d, options);
        if (d.TableOfContents)
        {
            fp.NewPage();
            fp.Add(new TextBlock(fonts, "Contents", fonts.Bold, PdfStyle.H1, PdfStyle.Ink, 10));
            foreach (var (h, num, _) in tocEntries)
            {
                string left = (h.Level == 2 ? "      " : "") + Label(num, h);
                string right = pages.TryGetValue(h, out int pg) ? pg.ToString(Inv) : "";
                var face = h.Level == 1 ? fonts.Bold : fonts.Regular;
                fp.Add(new DrawBlock(PdfStyle.Line(PdfStyle.Body) + 2, (c, x, y, w) =>
                {
                    fonts.Draw(c, left, x, y + PdfStyle.Body, face, PdfStyle.Body, PdfStyle.Ink);
                    float rw = fonts.Measure(right, fonts.Regular, PdfStyle.Body);
                    fonts.Draw(c, right, x + w - rw, y + PdfStyle.Body, fonts.Regular, PdfStyle.Body, PdfStyle.Ink);
                    float lw = fonts.Measure(left, face, PdfStyle.Body);
                    using var dot = new SKPaint { Color = PdfStyle.Rule, StrokeWidth = 0.6f, PathEffect = SKPathEffect.CreateDash([1f, 2.5f], 0) };
                    if (right.Length > 0) c.DrawLine(x + lw + 6, y + PdfStyle.Body, x + w - rw - 6, y + PdfStyle.Body, dot);
                }));
            }
            while (fp.PageCount < front) fp.NewPage();   // the TOC took fewer pages than reserved
        }
        fp.TotalPages = body.TotalPages = total;
        if (app is not null) app.TotalPages = total;

        using var ms = new MemoryStream();
        var when = (options.Timestamp ?? DateTimeOffset.Now).UtcDateTime;
        using (var doc = SKDocument.CreatePdf(ms, new SKDocumentPdfMetadata
        {
            Title = d.Title, Author = project.PreparedBy, Subject = project.Name,
            Creator = "MasterFlow " + options.SoftwareVersion, Producer = "MasterFlow " + options.SoftwareVersion, Creation = when, Modified = when, RasterDpi = 300,
        }))
        {
            fp.Render(doc);
            body.Render(doc);
            app?.Render(doc);
            doc.Close();
        }
        return ms.ToArray();
    }

    private static void Cover(PdfPager p, PdfFonts fonts, ProjectInfo project, ReportDocument d, ReportOptions o)
    {
        if (LogoImage(o.LogoDataUri) is { } logo)
        {
            float h = 60, w = Math.Min(220, logo.Width * h / logo.Height);
            p.Add(new DrawBlock(h + 20, (c, x, y, _) => { c.DrawImage(logo, new SKRect(x, y, x + w, y + h)); }));
        }
        p.Add(new DrawBlock(150, (_, _, _, _) => { }));
        p.Add(new TextBlock(fonts, d.Title, fonts.Bold, 24, PdfStyle.Accent, 8));
        if (d.Subtitle.Length > 0) p.Add(new TextBlock(fonts, d.Subtitle, fonts.Regular, 12, PdfStyle.Muted, 18));
        p.Add(new TextBlock(fonts, project.Name, fonts.Bold, 15, PdfStyle.Ink, 30));
        foreach (var (k, v) in d.Cover)
            p.Add(new DrawBlock(PdfStyle.Line(11) + 3, (c, x, y, _) =>
            {
                fonts.Draw(c, k, x, y + 11, fonts.Regular, 11, PdfStyle.Muted);
                fonts.Draw(c, v, x + 150, y + 11, fonts.Bold, 11, PdfStyle.Ink);
            }));
    }

    private static SKImage? LogoImage(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri) || !dataUri.StartsWith("data:image/", StringComparison.Ordinal) || dataUri.StartsWith("data:image/svg", StringComparison.Ordinal)) return null;
        int comma = dataUri.IndexOf(',');
        try { return comma < 0 ? null : SKImage.FromEncodedData(Convert.FromBase64String(dataUri[(comma + 1)..])); }
        catch (FormatException) { return null; }
    }

    private static void Lay(PdfPager p, PdfFonts fonts, IReadOnlyList<ReportBlock> blocks, List<(ReportHeading H, string N, bool A)> numbers, Dictionary<ReportHeading, int> pages, ReportOptions o)
    {
        foreach (var b in blocks)
            switch (b)
            {
                case ReportHeading h:
                    string num = numbers.First(n => ReferenceEquals(n.H, h)).N;
                    if (h.Level == 1)
                    {
                        p.Space(10);
                        var blk = new TextBlock(fonts, Label(num, h), fonts.Bold, 13.5f, PdfStyle.Accent, 6);
                        p.Add(new DrawBlock(0, (_, _, _, _) => { }), blk.Height(p.Width) + 60);   // keep with what follows
                        pages[h] = p.CurrentPage;
                        p.Add(blk);
                    }
                    else
                    {
                        p.Heading(Label(num, h), 60);
                        pages[h] = p.CurrentPage;
                    }
                    break;
                case ReportParagraph para: p.Paragraph(para.Bullet ? "•  " + para.Text : para.Text); break;
                case ReportTable t: ToolReport.AddTable(p, fonts, t.Table); break;
                case ReportChart ch: p.Space(4); p.Add(new ToolReport.ChartBlock(fonts, ch.Chart)); break;
                case ReportMessages m:
                    p.Paragraph($"Messages: {m.Source}", fonts.Bold, PdfStyle.Small, spaceAfter: 2);
                    ToolReport.AddMessages(p, fonts, m.Messages);
                    break;
                case ReportCode code:
                    p.Heading(code.Title, 30);
                    foreach (var line in code.Text.Replace("\r", "").Split('\n'))
                        p.Paragraph(line.Replace("\t", "    ").Length == 0 ? " " : line.Replace("\t", "    "), fonts.Mono, PdfStyle.Small, PdfStyle.Muted, 0);
                    break;
                case ReportNetwork net: NetworkReport.AddContent(p, fonts, net.Result, o); break;
                case ReportPageBreak: p.NewPage(); break;
                case ReportCertification cert:
                    p.Paragraph(cert.Statement, spaceAfter: 10);
                    p.Add(new DrawBlock(170, (c, x, y, w) =>
                    {
                        using var box = new SKPaint { Color = PdfStyle.Ink, StrokeWidth = 0.8f, Style = SKPaintStyle.Stroke };
                        c.DrawRect(x, y, 144, 144, box);   // 2 in. × 2 in. seal area
                        fonts.Draw(c, "Seal / stamp", x + 40, y + 76, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
                        float lx = x + 170;
                        void Line(float yy, string label, string value)
                        {
                            c.DrawLine(lx, y + yy, x + w, y + yy, box);
                            fonts.Draw(c, label, lx, y + yy + 10, fonts.Regular, PdfStyle.Small, PdfStyle.Muted);
                            if (value.Length > 0) fonts.Draw(c, value, lx + 2, y + yy - 3, fonts.Regular, PdfStyle.Body, PdfStyle.Ink);
                        }
                        Line(40, "Signature", ""); Line(80, "Engineer", cert.Engineer); Line(115, "License no.", cert.License); Line(150, "Date", "");
                        if (cert.Firm.Length > 0) fonts.Draw(c, cert.Firm, lx, y + 165, fonts.Bold, PdfStyle.Body, PdfStyle.Ink);
                    }));
                    break;
            }
    }

    // ------------------------------------------------------------------ HTML

    public static string ToHtml(ProjectInfo project, ReportDocument d, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var numbers = Number(d);
        var sb = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>").Append(E(d.Title)).Append("</title><style>")
            .Append("body{font-family:Segoe UI,Arial,sans-serif;color:#1d2733;margin:32px auto;max-width:980px;font-size:13px;line-height:1.45}h1{font-size:20px;color:#1f5f8b;margin-top:28px}h2{font-size:15px;color:#1f5f8b;border-bottom:1px solid #c9d3dd;padding-bottom:3px;margin-top:20px}")
            .Append("table{border-collapse:collapse;margin:6px 0;font-size:12px}th,td{border-bottom:1px solid #dde3ea;padding:3px 7px;text-align:left;vertical-align:top}th{background:#eef3f8}td.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}tr.fail td{font-weight:700;background:#fff7e0}")
            .Append(".cover{min-height:60vh;padding-top:120px}.cover h1{font-size:30px;margin:0}.cover .sub{color:#5b6876;font-size:16px;margin:6px 0 30px}.cover td{border:none;padding:3px 18px 3px 0;font-size:14px}")
            .Append(".note{color:#5b6876;font-size:12px}.msg{padding:4px 8px;margin:3px 0;border-left:3px solid #1f5f8b;background:#eef5fb}.msg.Warning{border-color:#b7791f;background:#fff7e0}.msg.Error{border-color:#b42318;background:#fdecea}")
            .Append("pre{background:#f5f7f9;padding:8px;font-size:11px;white-space:pre-wrap}.toc a{color:#1d2733;text-decoration:none}.toc .l2{padding-left:22px}.cert{display:flex;gap:28px;margin:12px 0}.seal{width:2in;height:2in;border:1px solid #1d2733;display:flex;align-items:center;justify-content:center;color:#5b6876}")
            .Append(".sig div{border-bottom:1px solid #1d2733;width:300px;height:30px;margin-top:8px}.sig span{font-size:11px;color:#5b6876}.appendix{page-break-before:always}@media print{body{margin:0;max-width:none}.pb{page-break-after:always}}</style></head><body>");
        sb.Append("<section class=\"cover pb\">");
        if (!string.IsNullOrWhiteSpace(options.LogoDataUri)) sb.Append($"<img src=\"{E(options.LogoDataUri)}\" alt=\"logo\" style=\"max-height:70px\"><br>");
        sb.Append($"<h1>{E(d.Title)}</h1><div class=\"sub\">{E(d.Subtitle)}</div><h2 style=\"border:none\">{E(project.Name)}</h2><table>");
        foreach (var (k, v) in d.Cover) sb.Append($"<tr><td>{E(k)}</td><td><strong>{E(v)}</strong></td></tr>");
        sb.Append("</table></section>");
        int id = 0;
        var anchors = new Dictionary<ReportHeading, string>(ReferenceEqualityComparer.Instance);
        foreach (var n in numbers) anchors[n.Heading] = $"h{++id}";
        if (d.TableOfContents)
        {
            sb.Append("<section class=\"toc pb\"><h1>Contents</h1>");
            foreach (var (h, num, _) in numbers.Where(n => n.Heading.Level <= 2))
                sb.Append($"<div class=\"l{h.Level}\"><a href=\"#{anchors[h]}\">{E(Label(num, h))}</a></div>");
            sb.Append("</section>");
        }
        void Blocks(IReadOnlyList<ReportBlock> blocks)
        {
            foreach (var b in blocks)
                switch (b)
                {
                    case ReportHeading h:
                        string num = numbers.First(n => ReferenceEquals(n.Heading, h)).Number;
                        sb.Append($"<h{h.Level} id=\"{anchors[h]}\">{E(Label(num, h))}</h{h.Level}>"); break;
                    case ReportParagraph p: sb.Append(p.Bullet ? $"<ul><li>{E(p.Text)}</li></ul>" : $"<p>{E(p.Text)}</p>"); break;
                    case ReportTable t:
                        sb.Append($"<div><strong>{E(t.Table.Title)}</strong></div><table><tr>");
                        foreach (var hd in t.Table.Headers) sb.Append($"<th>{E(hd)}</th>");
                        sb.Append("</tr>");
                        foreach (var row in t.Table.Rows)
                        {
                            sb.Append(row.Any(c => c is string s && s.StartsWith("FAILS", StringComparison.Ordinal)) ? "<tr class=\"fail\">" : "<tr>");
                            foreach (var c in row) sb.Append(c is double ? $"<td class=\"num\">{E(DesignTools.Cell(c))}</td>" : $"<td>{E(DesignTools.Cell(c))}</td>");
                            sb.Append("</tr>");
                        }
                        sb.Append("</table>");
                        foreach (var n in t.Table.Notes ?? []) sb.Append($"<div class=\"note\">{E(n)}</div>");
                        break;
                    case ReportChart ch: sb.Append(ToolChartLayout.ToSvg(ch.Chart)); break;
                    case ReportMessages m:
                        sb.Append($"<div class=\"note\"><strong>Messages: {E(m.Source)}</strong></div>");
                        foreach (var x in m.Messages) sb.Append($"<div class=\"msg {x.Severity}\"><strong>{x.Severity}</strong> [{E(x.Code)}] {E(x.Text)}</div>");
                        break;
                    case ReportCode code: sb.Append($"<h3>{E(code.Title)}</h3><pre>{E(code.Text)}</pre>"); break;
                    case ReportNetwork net:
                        var r = net.Result; var c2 = r.Constants;
                        sb.Append($"<p class=\"note\">Method: {E(NetworkTables.Method)} Tailwater: {E(NetworkTables.TailwaterText(r.Network))} Criteria: {E(NetworkTables.CriteriaText(r.Network, c2))}</p>");
                        foreach (var start in NetworkProfile.PathStarts(r))
                            if (NetworkProfile.Build(r, start) is { } prof) sb.Append($"<h3>Profile: {E(prof.From)} to {E(prof.To)}</h3>").Append(ProfileSvg.Render(prof, c2.Length));
                        void T(IReadOnlyList<string> head, IReadOnlyList<IReadOnlyList<string>> rows)
                        {
                            sb.Append("<table><tr>");
                            foreach (var hd in head) sb.Append($"<th>{E(hd)}</th>");
                            sb.Append("</tr>");
                            foreach (var row in rows) { sb.Append(row.Contains("FAILS") ? "<tr class=\"fail\">" : "<tr>"); foreach (var x in row) sb.Append($"<td>{E(x)}</td>"); sb.Append("</tr>"); }
                            sb.Append("</table>");
                        }
                        sb.Append("<h3>Pipes</h3>"); T(NetworkTables.PipeHeader(c2, r.Network.Units), NetworkTables.PipeRows(r));
                        sb.Append("<h3>Structures</h3>"); T(NetworkTables.StructureHeader(c2), NetworkTables.StructureRows(r));
                        foreach (var x in r.Messages) sb.Append($"<div class=\"msg {x.Severity}\"><strong>{x.Severity}</strong> [{E(x.Code)}] {E(x.Text)}</div>");
                        break;
                    case ReportPageBreak: sb.Append("<div class=\"pb\"></div>"); break;
                    case ReportCertification cert:
                        sb.Append($"<p>{E(cert.Statement)}</p><div class=\"cert\"><div class=\"seal\">Seal / stamp</div><div class=\"sig\"><div></div><span>Signature</span><div>{E(cert.Engineer)}</div><span>Engineer</span><div>{E(cert.License)}</div><span>License no.</span><div></div><span>Date</span>{(cert.Firm.Length > 0 ? $"<p><strong>{E(cert.Firm)}</strong></p>" : "")}</div></div>");
                        break;
                }
        }
        Blocks(d.Body);
        if (d.Appendix.Count > 0) { sb.Append("<section class=\"appendix\">"); Blocks(d.Appendix); sb.Append("</section>"); }
        sb.Append($"<p class=\"note\">Prepared with MasterFlow {E(options.SoftwareVersion)}, {PdfReport.Stamp(options)}. The responsible engineer must review inputs, assumptions and results before use.</p></body></html>");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Word (.docx)

    /// <summary>
    /// Word document with no third-party dependency: Title / Heading 1 / Heading 2 styles (so Word's navigation pane and
    /// TOC work), a TOC field Word fills in when the document is opened (press F9 / "Update table" if it asks), tables with
    /// repeating header rows, charts and profiles as PNG images, a page-number footer, and a landscape section for the
    /// appendices. Everything stays editable, so the firm can finish the narrative in Word.
    /// </summary>
    public static byte[] ToDocx(ProjectInfo project, ReportDocument d, ReportOptions? options = null)
    {
        options ??= new ReportOptions();
        var fonts = new PdfFonts();
        var numbers = Number(d);
        var media = new List<byte[]>();
        var x = new StringBuilder();
        static string X(string? s) => SecurityElement.Escape(s ?? "") ?? "";
        void P(string text, string? style = null, bool bold = false, int? sizeHalfPts = null, bool pageBreakBefore = false)
        {
            x.Append("<w:p>");
            if (style is not null || pageBreakBefore) x.Append("<w:pPr>").Append(style is null ? "" : $"<w:pStyle w:val=\"{style}\"/>").Append(pageBreakBefore ? "<w:pageBreakBefore/>" : "").Append("</w:pPr>");
            x.Append("<w:r>");
            if (bold || sizeHalfPts is not null) x.Append("<w:rPr>").Append(bold ? "<w:b/>" : "").Append(sizeHalfPts is { } sz ? $"<w:sz w:val=\"{sz}\"/>" : "").Append("</w:rPr>");
            x.Append($"<w:t xml:space=\"preserve\">{X(text)}</w:t></w:r></w:p>");
        }
        void Break() => x.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
        void Image(byte[] png, float wPt, float hPt)
        {
            media.Add(png);
            int n = media.Count;
            long cx = (long)(wPt * 12700), cy = (long)(hPt * 12700);
            x.Append($"<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:docPr id=\"{n}\" name=\"Figure {n}\"/>")
             .Append("<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">")
             .Append($"<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:nvPicPr><pic:cNvPr id=\"{n}\" name=\"image{n}.png\"/><pic:cNvPicPr/></pic:nvPicPr>")
             .Append($"<pic:blipFill><a:blip r:embed=\"rImg{n}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>")
             .Append($"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>");
        }
        string Cell(string text, bool num, bool bold, string? fill = null)
        {
            var c = new StringBuilder("<w:tc>");
            if (fill is not null) c.Append($"<w:tcPr><w:shd w:val=\"clear\" w:fill=\"{fill}\"/></w:tcPr>");
            foreach (var line in text.Split('\n'))
                c.Append($"<w:p><w:pPr><w:pStyle w:val=\"TableText\"/>{(num ? "<w:jc w:val=\"right\"/>" : "")}</w:pPr><w:r>{(bold ? "<w:rPr><w:b/></w:rPr>" : "")}<w:t xml:space=\"preserve\">{X(line)}</w:t></w:r></w:p>");
            return c.Append("</w:tc>").ToString();
        }
        void Table(IReadOnlyList<string> head, IEnumerable<IReadOnlyList<(string Text, bool Num)>> rows, string? title, float widthPt, int? rowHeightTwips = null)
        {
            if (title is not null) P(title, "Caption");
            int cols = Math.Max(1, head.Count), tw = (int)(widthPt * 20);
            x.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"MfTable\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLayout w:type=\"autofit\"/></w:tblPr><w:tblGrid>");
            for (int i = 0; i < cols; i++) x.Append($"<w:gridCol w:w=\"{tw / cols}\"/>");
            x.Append("</w:tblGrid><w:tr><w:trPr><w:tblHeader/></w:trPr>");
            foreach (var h in head) x.Append(Cell(h, false, true, "EEF3F8"));
            x.Append("</w:tr>");
            foreach (var row in rows)
            {
                bool fail = row.Any(c => c.Text.StartsWith("FAILS", StringComparison.Ordinal));
                x.Append("<w:tr>").Append(rowHeightTwips is { } rh ? $"<w:trPr><w:trHeight w:val=\"{rh}\"/></w:trPr>" : "");
                foreach (var (t, num) in row) x.Append(Cell(t, num, fail));
                x.Append("</w:tr>");
            }
            x.Append("</w:tbl>");
            P("");
        }
        void Blocks(IReadOnlyList<ReportBlock> blocks, float widthPt)
        {
            foreach (var b in blocks)
                switch (b)
                {
                    case ReportHeading h: P(Label(numbers.First(n => ReferenceEquals(n.Heading, h)).Number, h), h.Level == 1 ? "Heading1" : "Heading2"); break;
                    case ReportParagraph p: P(p.Bullet ? "•  " + p.Text : p.Text); break;
                    case ReportTable t:
                        Table(t.Table.Headers, t.Table.Rows.Select(r => (IReadOnlyList<(string, bool)>)r.Select(c => (DesignTools.Cell(c), c is double)).ToList()), t.Table.Title, widthPt);
                        foreach (var n in t.Table.Notes ?? []) P(n, "Note");
                        break;
                    case ReportChart ch:
                    {
                        var blk = new ToolReport.ChartBlock(fonts, ch.Chart);
                        Image(Png(blk, widthPt), widthPt, blk.Height(widthPt));
                        break;
                    }
                    case ReportMessages m:
                        P("Messages: " + m.Source, bold: true);
                        foreach (var msg in m.Messages) P($"{msg.Severity} [{msg.Code}] {msg.Text}", "Note");
                        break;
                    case ReportCode code:
                        P(code.Title, "Heading2");
                        foreach (var line in code.Text.Replace("\r", "").Split('\n')) P(line.Replace("\t", "    "), "Code");
                        break;
                    case ReportNetwork net:
                    {
                        var r = net.Result; var c2 = r.Constants;
                        P($"Method: {NetworkTables.Method} Tailwater: {NetworkTables.TailwaterText(r.Network)} Criteria: {NetworkTables.CriteriaText(r.Network, c2)}", "Note");
                        foreach (var start in NetworkProfile.PathStarts(r))
                            if (NetworkProfile.Build(r, start) is { } prof)
                            {
                                P($"Profile: {prof.From} to {prof.To}", "Caption");
                                var blk = new NetworkReport.ProfileBlock(fonts, prof, c2.Length);
                                Image(Png(blk, widthPt), widthPt, blk.Height(widthPt));
                            }
                        static IReadOnlyList<(string, bool)> Row(IReadOnlyList<string> r) => r.Select(c => (c, double.TryParse(c, NumberStyles.Float, Inv, out _))).ToList();
                        Table(NetworkTables.PipeHeader(c2, r.Network.Units), NetworkTables.PipeRows(r).Select(Row), "Pipes", widthPt);
                        Table(NetworkTables.StructureHeader(c2), NetworkTables.StructureRows(r).Select(Row), "Structures (HGL check against the rim)", widthPt);
                        foreach (var msg in r.Messages) P($"{msg.Severity} [{msg.Code}] {msg.Text}", "Note");
                        break;
                    }
                    case ReportPageBreak: Break(); break;
                    case ReportCertification cert:
                        P(cert.Statement);
                        Table(["Seal / stamp", "Signature, engineer, license, date"],
                            [[("", false), ($"\nSignature: ______________________________\n\nEngineer: {cert.Engineer}\n\nLicense no.: {cert.License}\n\nDate: ____________\n\n{cert.Firm}", false)]], null, widthPt, 2880);
                        break;
                }
        }

        const string portrait = "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1080\" w:right=\"1080\" w:bottom=\"1080\" w:left=\"1080\" w:header=\"540\" w:footer=\"540\" w:gutter=\"0\"/>";
        const string landscape = "<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:pgMar w:top=\"1080\" w:right=\"1080\" w:bottom=\"1080\" w:left=\"1080\" w:header=\"540\" w:footer=\"540\" w:gutter=\"0\"/>";
        const string footerRef = "<w:footerReference w:type=\"default\" r:id=\"rFooter\"/>";

        // cover
        P(d.Title, "Title");
        if (d.Subtitle.Length > 0) P(d.Subtitle, "Subtitle");
        P(project.Name, bold: true, sizeHalfPts: 32);
        foreach (var (k, v) in d.Cover) P($"{k}: {v}");
        if (d.TableOfContents)
        {
            Break();
            P("Contents", "TOCHeading");
            x.Append("<w:p><w:r><w:fldChar w:fldCharType=\"begin\" w:dirty=\"true\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-2\" \\h \\z \\u </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>")
             .Append("<w:r><w:t>Right-click and choose Update Field to build the table of contents.</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        }
        Break();
        Blocks(d.Body, 468);
        if (d.Appendix.Count > 0)
        {
            x.Append($"<w:p><w:pPr><w:sectPr>{footerRef}{portrait}</w:sectPr></w:pPr></w:p>");
            Blocks(d.Appendix, 648);
        }
        string finalSect = $"<w:sectPr>{footerRef}{(d.Appendix.Count > 0 ? landscape : portrait)}</w:sectPr>";
        string document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"><w:body>" + x + finalSect + "</w:body></w:document>";

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string path, string text) { using var w = new StreamWriter(Open(path), new UTF8Encoding(false)); w.Write(text); }
            Stream Open(string path) { var e = zip.CreateEntry(path, CompressionLevel.Optimal); e.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero); return e.Open(); }
            Entry("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
                "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>" +
                "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/></Types>");
            Entry("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
            Entry("docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
                $"<dc:title>{X(d.Title)}</dc:title><dc:subject>{X(project.Name)}</dc:subject><dc:creator>{X(project.PreparedBy)}</dc:creator></cp:coreProperties>");
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
                "<Relationship Id=\"rSettings\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
                "<Relationship Id=\"rFooter\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/>");
            for (int i = 0; i < media.Count; i++)
            {
                rels.Append($"<Relationship Id=\"rImg{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image{i + 1}.png\"/>");
                using var s = Open($"word/media/image{i + 1}.png"); s.Write(media[i]);
            }
            Entry("word/_rels/document.xml.rels", rels.Append("</Relationships>").ToString());
            Entry("word/document.xml", document);
            Entry("word/settings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:updateFields w:val=\"true\"/></w:settings>");
            Entry("word/footer1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr>" +
                $"<w:r><w:rPr><w:sz w:val=\"16\"/></w:rPr><w:t xml:space=\"preserve\">{X(d.Title)} · {X(project.Name)} · Page </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p></w:ftr>");
            Entry("word/styles.xml", Styles);
        }
        return ms.ToArray();
    }

    /// <summary>A PDF block drawn on a white raster at 2× (≈144 dpi), as PNG.</summary>
    private static byte[] Png(IPdfBlock block, float widthPt)
    {
        float h = block.Height(widthPt);
        using var surface = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(widthPt * 2), (int)Math.Ceiling(h * 2)));
        var c = surface.Canvas;
        c.Clear(SKColors.White);
        c.Scale(2);
        block.Draw(c, 0, 0, widthPt);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
        <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial" w:cs="Arial"/><w:sz w:val="20"/><w:color w:val="1D2733"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="100" w:line="264" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>
        <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
        <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:before="2400" w:after="120"/></w:pPr><w:rPr><w:b/><w:color w:val="1F5F8B"/><w:sz w:val="52"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="480"/></w:pPr><w:rPr><w:color w:val="5B6876"/><w:sz w:val="26"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="360" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="1F5F8B"/><w:sz w:val="30"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="80"/><w:pBdr><w:bottom w:val="single" w:sz="4" w:space="1" w:color="C9D1DA"/></w:pBdr><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:color w:val="1F5F8B"/><w:sz w:val="24"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="TOCHeading"><w:name w:val="TOC Heading"/><w:basedOn w:val="Heading1"/><w:pPr><w:outlineLvl w:val="9"/></w:pPr></w:style>
        <w:style w:type="paragraph" w:styleId="Caption"><w:name w:val="caption"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="120" w:after="40"/></w:pPr><w:rPr><w:b/><w:sz w:val="18"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Note"><w:name w:val="Note"/><w:basedOn w:val="Normal"/><w:rPr><w:color w:val="5B6876"/><w:sz w:val="16"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="TableText"><w:name w:val="Table Text"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="0"/></w:pPr><w:rPr><w:sz w:val="15"/></w:rPr></w:style>
        <w:style w:type="paragraph" w:styleId="Code"><w:name w:val="Code"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr><w:rPr><w:rFonts w:ascii="Consolas" w:hAnsi="Consolas"/><w:sz w:val="15"/></w:rPr></w:style>
        <w:style w:type="table" w:styleId="MfTable"><w:name w:val="MasterFlow Table"/><w:tblPr><w:tblBorders><w:top w:val="single" w:sz="4" w:color="C9D1DA"/><w:bottom w:val="single" w:sz="4" w:color="C9D1DA"/><w:insideH w:val="single" w:sz="4" w:color="DDE3EA"/></w:tblBorders><w:tblCellMar><w:left w:w="60" w:type="dxa"/><w:right w:w="60" w:type="dxa"/></w:tblCellMar></w:tblPr></w:style>
        </w:styles>
        """;
}
