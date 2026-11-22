using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace MasterFlow.Application.Documents;

/// <summary>Text pulled from a document, one entry per page (a Word or text file is one page).</summary>
public sealed record DocumentPages(string Source, IReadOnlyList<string> Pages)
{
    public string All => string.Join("\n", Pages);
    /// <summary>Readable characters (letters and digits) — a scanned PDF gives almost none.</summary>
    public int Letters => Pages.Sum(p => p.Count(char.IsLetterOrDigit));
}

/// <summary>
/// Reads the text of a drainage report or plan set: .txt/.md as is, .docx from word/document.xml, and .pdf with a
/// small built-in reader (content streams with Flate compression, object streams, literal and hex strings, and
/// ToUnicode maps for embedded fonts). Scanned PDFs have no text layer; the caller says so. No external library
/// is used, so nothing leaves the computer (D-221).
/// </summary>
public static class DocumentText
{
    public static DocumentPages Read(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var bytes = File.ReadAllBytes(path);
        bool pdf = bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';
        bool zip = bytes.Length > 2 && bytes[0] == 'P' && bytes[1] == 'K';
        return ext switch
        {
            _ when pdf => new(path, Pdf(bytes)),
            _ when zip || ext == ".docx" => new(path, [Docx(bytes)]),
            _ => new(path, [Encoding.UTF8.GetString(bytes)]),
        };
    }

    // ---------------- Word ----------------

    public static string Docx(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var e = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("not a Word document (no word/document.xml)");
        using var r = new StreamReader(e.Open());
        string xml = r.ReadToEnd();
        xml = Regex.Replace(xml, "</w:p>|<w:br/>|<w:tab/>|</w:tc>", m => m.Value == "</w:tc>" || m.Value == "<w:tab/>" ? "\t" : "\n");
        xml = Regex.Replace(xml, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(xml);
    }

    // ---------------- PDF ----------------

    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>Page texts of a PDF, in page order where the page tree can be followed, else in stream order.</summary>
    public static List<string> Pdf(byte[] bytes)
    {
        var objs = PdfObjects(bytes);
        // Fonts: object number → ToUnicode map; resource name → object number (last definition wins, D-221).
        var cmaps = new Dictionary<int, Dictionary<int, string>>();
        var codeBytes = new Dictionary<int, int>();
        foreach (var (num, body) in objs)
        {
            var tu = Regex.Match(body.Dict, @"/ToUnicode\s+(\d+)\s+0\s+R");
            if (!tu.Success || !body.Dict.Contains("/Font")) continue;
            if (objs.TryGetValue(int.Parse(tu.Groups[1].Value), out var cm) && cm.Stream is { } s)
            {
                var (map, nb) = ParseCMap(Latin1.GetString(s));
                cmaps[num] = map; codeBytes[num] = nb;
            }
        }
        var pages = new List<string>();
        var pageObjs = objs.Where(o => Regex.IsMatch(o.Value.Dict, @"/Type\s*/Page\b")).OrderBy(o => o.Key).ToList();
        var order = PageOrder(objs) ?? pageObjs.Select(p => p.Key).ToList();
        foreach (var pn in order)
        {
            if (!objs.TryGetValue(pn, out var page)) continue;
            var fonts = FontResources(page.Dict, objs);
            var sb = new StringBuilder();
            foreach (var cref in ContentRefs(page.Dict, objs))
                if (objs.TryGetValue(cref, out var c) && c.Stream is { } st)
                    sb.Append(ContentText(Latin1.GetString(st), fonts, cmaps, codeBytes));
            pages.Add(sb.ToString());
        }
        if (pages.Count == 0)   // no page tree found: every stream that looks like content
            foreach (var o in objs.Values.Where(o => o.Stream is not null && !o.Dict.Contains("/Font") && !o.Dict.Contains("/Image")))
            {
                var t = ContentText(Latin1.GetString(o.Stream!), [], cmaps, codeBytes);
                if (t.Length > 0) pages.Add(t);
            }
        return pages;
    }

    private sealed record PdfObj(string Dict, byte[]? Stream);

    private static Dictionary<int, PdfObj> PdfObjects(byte[] bytes)
    {
        string s = Latin1.GetString(bytes);
        var objs = new Dictionary<int, PdfObj>();
        foreach (Match m in Regex.Matches(s, @"(\d+)\s+(\d+)\s+obj\b"))
        {
            int start = m.Index + m.Length;
            int end = s.IndexOf("endobj", start, StringComparison.Ordinal);
            if (end < 0) continue;
            string body = s[start..end];
            byte[]? stream = null;
            int si = body.IndexOf("stream", StringComparison.Ordinal);
            string dict = body;
            if (si >= 0 && (si + 6 >= body.Length || body[si + 6] is '\r' or '\n'))
            {
                dict = body[..si];
                int ds = si + 6; if (ds < body.Length && body[ds] == '\r') ds++; if (ds < body.Length && body[ds] == '\n') ds++;
                int de = body.LastIndexOf("endstream", StringComparison.Ordinal);
                if (de > ds)
                {
                    var raw = bytes.AsSpan(start + ds, de - ds).ToArray();
                    stream = Decode(dict, raw);
                }
            }
            objs[int.Parse(m.Groups[1].Value)] = new PdfObj(dict, stream);
        }
        // Object streams (PDF 1.5+): objects packed inside a compressed stream.
        foreach (var o in objs.Values.Where(o => o.Stream is not null && Regex.IsMatch(o.Dict, @"/Type\s*/ObjStm")).ToList())
        {
            int n = int.Parse(Regex.Match(o.Dict, @"/N\s+(\d+)").Groups[1].Value), first = int.Parse(Regex.Match(o.Dict, @"/First\s+(\d+)").Groups[1].Value);
            string data = Latin1.GetString(o.Stream!);
            var head = data[..Math.Min(first, data.Length)].Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < head.Length && i / 2 < n; i += 2)
            {
                int num = int.Parse(head[i]), off = first + int.Parse(head[i + 1]);
                int next = i + 3 < head.Length ? first + int.Parse(head[i + 3]) : data.Length;
                if (off < data.Length && !objs.ContainsKey(num)) objs[num] = new PdfObj(data[off..Math.Min(next, data.Length)], null);
            }
        }
        return objs;
    }

    private static byte[]? Decode(string dict, byte[] raw)
    {
        if (!dict.Contains("/FlateDecode")) return dict.Contains("/Filter") ? null : raw;
        try
        {
            using var z = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
            using var o = new MemoryStream();
            z.CopyTo(o);
            return o.ToArray();
        }
        catch { return null; }
    }

    private static List<int>? PageOrder(Dictionary<int, PdfObj> objs)
    {
        var root = objs.Values.Select(o => Regex.Match(o.Dict, @"/Type\s*/Catalog")).Any(m => m.Success)
            ? objs.Values.First(o => Regex.IsMatch(o.Dict, @"/Type\s*/Catalog")) : null;
        if (root is null) return null;
        var pr = Regex.Match(root.Dict, @"/Pages\s+(\d+)\s+0\s+R");
        if (!pr.Success) return null;
        var list = new List<int>();
        void Walk(int n, int depth)
        {
            if (depth > 50 || !objs.TryGetValue(n, out var o)) return;
            if (Regex.IsMatch(o.Dict, @"/Type\s*/Page\b")) { list.Add(n); return; }
            var kids = Regex.Match(o.Dict, @"/Kids\s*\[([^\]]*)\]");
            foreach (Match k in Regex.Matches(kids.Groups[1].Value, @"(\d+)\s+0\s+R")) Walk(int.Parse(k.Groups[1].Value), depth + 1);
        }
        Walk(int.Parse(pr.Groups[1].Value), 0);
        return list.Count > 0 ? list : null;
    }

    private static IEnumerable<int> ContentRefs(string pageDict, Dictionary<int, PdfObj> objs)
    {
        var m = Regex.Match(pageDict, @"/Contents\s*(\[[^\]]*\]|\d+\s+0\s+R)");
        if (!m.Success) yield break;
        foreach (Match r in Regex.Matches(m.Groups[1].Value, @"(\d+)\s+0\s+R"))
        {
            int n = int.Parse(r.Groups[1].Value);
            // /Contents may point to an array object.
            if (objs.TryGetValue(n, out var o) && o.Stream is null && o.Dict.TrimStart().StartsWith('['))
                foreach (Match r2 in Regex.Matches(o.Dict, @"(\d+)\s+0\s+R")) yield return int.Parse(r2.Groups[1].Value);
            else yield return n;
        }
    }

    /// <summary>Resource name (F1) → font object number for a page (resources inline, by reference, or inherited are tried).</summary>
    private static Dictionary<string, int> FontResources(string pageDict, Dictionary<int, PdfObj> objs)
    {
        var d = new Dictionary<string, int>();
        string res = pageDict;
        var rr = Regex.Match(pageDict, @"/Resources\s+(\d+)\s+0\s+R");
        if (rr.Success && objs.TryGetValue(int.Parse(rr.Groups[1].Value), out var ro)) res = ro.Dict;
        var fr = Regex.Match(res, @"/Font\s+(\d+)\s+0\s+R");
        if (fr.Success && objs.TryGetValue(int.Parse(fr.Groups[1].Value), out var fo)) res = fo.Dict;
        else { var fi = Regex.Match(res, @"/Font\s*<<(.*?)>>", RegexOptions.Singleline); if (fi.Success) res = fi.Groups[1].Value; }
        foreach (Match m in Regex.Matches(res, @"/([A-Za-z0-9_.+-]+)\s+(\d+)\s+0\s+R")) d[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        return d;
    }

    private static (Dictionary<int, string> Map, int Bytes) ParseCMap(string cmap)
    {
        var map = new Dictionary<int, string>();
        int nb = 1;
        var cs = Regex.Match(cmap, @"begincodespacerange\s*<([0-9A-Fa-f]+)>");
        if (cs.Success) nb = Math.Max(1, cs.Groups[1].Value.Length / 2);
        foreach (Match blk in Regex.Matches(cmap, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
            foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                map[Convert.ToInt32(m.Groups[1].Value, 16)] = Utf16(m.Groups[2].Value);
        foreach (Match blk in Regex.Matches(cmap, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
            foreach (Match m in Regex.Matches(blk.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(<([0-9A-Fa-f]+)>|\[([^\]]*)\])"))
            {
                int lo = Convert.ToInt32(m.Groups[1].Value, 16), hi = Convert.ToInt32(m.Groups[2].Value, 16);
                if (hi - lo > 65535) continue;
                if (m.Groups[4].Success)
                {
                    string dst = m.Groups[4].Value;
                    int baseCp = Convert.ToInt32(dst.Length > 4 ? dst[^4..] : dst, 16);
                    string prefix = dst.Length > 4 ? Utf16(dst[..^4]) : "";
                    for (int c = lo; c <= hi; c++) { int cp = baseCp + (c - lo); map[c] = prefix + (cp is (>= 0xD800 and <= 0xDFFF) or > 0x10FFFF ? "\uFFFD" : char.ConvertFromUtf32(cp)); }
                }
                else
                {
                    var arr = Regex.Matches(m.Groups[5].Value, @"<([0-9A-Fa-f]+)>").Select(x => Utf16(x.Groups[1].Value)).ToList();
                    for (int c = lo; c <= hi && c - lo < arr.Count; c++) map[c] = arr[c - lo];
                }
            }
        return (map, nb);
    }

    private static string Utf16(string hex)
    {
        if (hex.Length % 4 != 0) hex = hex.PadLeft((hex.Length + 3) / 4 * 4, '0');
        var b = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(2 * i, 2), 16)).ToArray();
        return Encoding.BigEndianUnicode.GetString(b);
    }

    /// <summary>Text shown by a content stream: Tj, TJ, ' and " strings, with line breaks at Td/TD/T*/Tm moves and spaces for wide TJ gaps.</summary>
    private static string ContentText(string content, Dictionary<string, int> fonts, Dictionary<int, Dictionary<int, string>> cmaps, Dictionary<int, int> codeBytes)
    {
        var sb = new StringBuilder();
        Dictionary<int, string>? map = null; int nb = 1;
        int i = 0, n = content.Length;
        var operands = new List<object>();
        while (i < n)
        {
            char c = content[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '%') { while (i < n && content[i] != '\n' && content[i] != '\r') i++; continue; }
            if (c == '(') { operands.Add(ReadLiteral(content, ref i)); continue; }
            if (c == '<' && i + 1 < n && content[i + 1] != '<') { int e = content.IndexOf('>', i); operands.Add(HexBytes(content[(i + 1)..e])); i = e + 1; continue; }
            if (c == '[') { operands.Add("["); i++; continue; }
            if (c == ']') { operands.Add("]"); i++; continue; }
            if (c == '/') { int s = ++i; while (i < n && !char.IsWhiteSpace(content[i]) && "/[]()<>".IndexOf(content[i]) < 0) i++; operands.Add("/" + content[s..i]); continue; }
            if (c == '<' || c == '>') { i += 2; continue; }   // << >> dictionaries (inline images, marked content)
            int st = i;
            while (i < n && !char.IsWhiteSpace(content[i]) && "/[]()<>%".IndexOf(content[i]) < 0) i++;
            string tok = content[st..i];
            if (tok.Length == 0) { i++; continue; }
            if (double.TryParse(tok, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double num)) { operands.Add(num); continue; }
            switch (tok)
            {
                case "Tf":
                    var fname = operands.OfType<string>().LastOrDefault(o => o.StartsWith('/'));
                    map = null; nb = 1;
                    if (fname is not null && fonts.TryGetValue(fname[1..], out int fo) && cmaps.TryGetValue(fo, out var m0)) { map = m0; nb = codeBytes[fo]; }
                    break;
                case "Tj": case "'": case "\"":
                    if (tok != "Tj") sb.Append('\n');
                    if (operands.LastOrDefault() is byte[] b1) sb.Append(Show(b1, map, nb));
                    break;
                case "TJ":
                    foreach (var o in operands)
                        if (o is byte[] b2) sb.Append(Show(b2, map, nb));
                        else if (o is double d && d < -250) sb.Append(' ');
                    break;
                case "Td": case "TD":
                    if (operands.Count >= 2 && operands[^1] is double dy && Math.Abs(dy) > 0.1) sb.Append('\n'); else sb.Append(' ');
                    break;
                case "T*": case "Tm": case "ET": sb.Append('\n'); break;
                case "BI": { int e = content.IndexOf("EI", i, StringComparison.Ordinal); i = e < 0 ? n : e + 2; break; }
            }
            operands.Clear();
        }
        return Regex.Replace(Regex.Replace(sb.ToString(), "[ \t]+", " "), @"\n\s*\n+", "\n");
    }

    private static string Show(byte[] b, Dictionary<int, string>? map, int nb)
    {
        if (map is null) return Latin1.GetString(b);
        var sb = new StringBuilder();
        for (int k = 0; k + nb <= b.Length; k += nb)
        {
            int code = nb == 2 ? b[k] << 8 | b[k + 1] : b[k];
            sb.Append(map.TryGetValue(code, out var s) ? s : nb == 1 ? ((char)code).ToString() : "");
        }
        return sb.ToString();
    }

    private static byte[] HexBytes(string hex)
    {
        hex = Regex.Replace(hex, @"\s", "");
        if (hex.Length % 2 == 1) hex += "0";
        return Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(2 * i, 2), 16)).ToArray();
    }

    private static byte[] ReadLiteral(string s, ref int i)
    {
        var o = new List<byte>();
        int depth = 0;
        i++;
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '\\' && i < s.Length)
            {
                char e = s[i++];
                switch (e)
                {
                    case 'n': o.Add(10); break; case 'r': o.Add(13); break; case 't': o.Add(9); break;
                    case 'b': o.Add(8); break; case 'f': o.Add(12); break;
                    case '\r': if (i < s.Length && s[i] == '\n') i++; break;
                    case '\n': break;
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int v = e - '0';
                            for (int k = 0; k < 2 && i < s.Length && s[i] >= '0' && s[i] <= '7'; k++) v = v * 8 + (s[i++] - '0');
                            o.Add((byte)v);
                        }
                        else o.Add((byte)e);
                        break;
                }
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')') { if (depth == 0) break; depth--; }
            o.Add((byte)c);
        }
        return o.ToArray();
    }
}
