using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace MasterFlow.Reporting;

/// <summary>A worksheet: rows of cells, each a string, a double, or null (empty). Row 0 is bold when <see cref="HeaderRows"/> ≥ 1.</summary>
public sealed record XlsxSheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows, int HeaderRows = 1, IReadOnlyList<double>? ColumnWidths = null);

/// <summary>
/// Minimal Office Open XML (.xlsx) writer with no third-party dependency: inline strings, numbers stored
/// at full double precision (display is Excel's General format), bold header rows and column widths.
/// Non-finite numbers are written as empty cells, never as text pretending to be a value.
/// </summary>
public static class XlsxWriter
{
    public static void Write(Stream output, IReadOnlyList<XlsxSheet> sheets)
    {
        if (sheets.Count == 0) throw new ArgumentException("At least one sheet is required.", nameof(sheets));
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        void Entry(string path, string xml)
        {
            var e = zip.CreateEntry(path, CompressionLevel.Optimal);
            e.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero); // deterministic output
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        var names = UniqueNames(sheets.Select(s => s.Name));

        var ct = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        ct.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        ct.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        ct.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 0; i < sheets.Count; i++)
            ct.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        ct.Append("</Types>");
        Entry("[Content_Types].xml", ct.ToString());

        Entry("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

        var wb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < sheets.Count; i++)
        {
            wb.Append($"<sheet name=\"{X(names[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        }
        wb.Append("</sheets></workbook>");
        rels.Append($"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        Entry("xl/workbook.xml", wb.ToString());
        Entry("xl/_rels/workbook.xml.rels", rels.ToString());

        Entry("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>");

        for (int i = 0; i < sheets.Count; i++) Entry($"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
    }

    public static byte[] ToBytes(IReadOnlyList<XlsxSheet> sheets)
    {
        using var ms = new MemoryStream();
        Write(ms, sheets);
        return ms.ToArray();
    }

    private static string SheetXml(XlsxSheet sheet)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (sheet.ColumnWidths is { Count: > 0 } widths)
        {
            sb.Append("<cols>");
            for (int c = 0; c < widths.Count; c++)
                sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{widths[c].ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (int r = 0; r < sheet.Rows.Count; r++)
        {
            sb.Append($"<row r=\"{r + 1}\">");
            string style = r < sheet.HeaderRows ? " s=\"1\"" : "";
            var row = sheet.Rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                string refName = Column(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                switch (row[c])
                {
                    case null: break;
                    case double d when double.IsFinite(d):
                        sb.Append($"<c r=\"{refName}\"{style}><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>"); break;
                    case int n:
                        sb.Append($"<c r=\"{refName}\"{style}><v>{n.ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                    case double: break; // NaN/∞: leave empty
                    case var v:
                        sb.Append($"<c r=\"{refName}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{X(v.ToString() ?? "")}</t></is></c>"); break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    public static string Column(int index)
    {
        var s = "";
        for (int n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
        return s;
    }

    private static string X(string s)
    {
        // Strip characters XML 1.0 cannot carry, then escape.
        var clean = new string(s.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray());
        return SecurityElement.Escape(clean) ?? "";
    }

    private static List<string> UniqueNames(IEnumerable<string> raw)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var r in raw)
        {
            var baseName = new string((string.IsNullOrWhiteSpace(r) ? "Sheet" : r).Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim('\'');
            if (baseName.Length == 0) baseName = "Sheet";
            if (baseName.Length > 31) baseName = baseName[..31];
            var name = baseName;
            for (int k = 2; !used.Add(name); k++)
            {
                var suffix = $" ({k})";
                name = (baseName.Length + suffix.Length > 31 ? baseName[..(31 - suffix.Length)] : baseName) + suffix;
            }
            list.Add(name);
        }
        return list;
    }
}
