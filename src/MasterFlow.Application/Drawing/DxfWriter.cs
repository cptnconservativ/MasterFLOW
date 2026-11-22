using System.Globalization;
using System.Text;

namespace MasterFlow.Application.Drawing;

/// <summary>
/// A minimal ASCII DXF (AutoCAD R12, AC1009) writer: layers, CONTINUOUS and DASHED linetypes, lines, 2-D polylines,
/// circles and text. R12 needs no entity handles, and AutoCAD, Civil 3D, BricsCAD and most viewers open it.
/// </summary>
public sealed class DxfWriter
{
    private readonly StringBuilder _ent = new();
    private readonly Dictionary<string, (int Color, string Ltype)> _layers = new(StringComparer.OrdinalIgnoreCase) { ["0"] = (7, "CONTINUOUS") };
    private double _minX = double.PositiveInfinity, _minY = double.PositiveInfinity, _maxX = double.NegativeInfinity, _maxY = double.NegativeInfinity;

    public int EntityCount { get; private set; }

    /// <summary>Adds a layer (ACI colour 1–255; linetype CONTINUOUS or DASHED).</summary>
    public void Layer(string name, int color, string ltype = "CONTINUOUS") => _layers[Clean(name)] = (color, ltype);

    public void Line(string layer, double x1, double y1, double x2, double y2)
    {
        Start("LINE", layer);
        P(10, x1, y1); P(11, x2, y2);
    }

    public void Polyline(string layer, IReadOnlyList<(double X, double Y)> pts, bool closed = false)
    {
        if (pts.Count < 2) return;
        Start("POLYLINE", layer);
        G(66, "1"); P(10, 0, 0); G(70, closed ? "1" : "0");
        foreach (var (x, y) in pts) { G(0, "VERTEX"); G(8, Clean(layer)); P(10, x, y); }
        G(0, "SEQEND"); G(8, Clean(layer));
    }

    public void Rectangle(string layer, double x1, double y1, double x2, double y2) => Polyline(layer, [(x1, y1), (x2, y1), (x2, y2), (x1, y2)], closed: true);

    public void Circle(string layer, double x, double y, double r)
    {
        Start("CIRCLE", layer);
        P(10, x, y); G(40, N(r));
    }

    /// <summary>Text; <paramref name="align"/> 0 = left, 1 = centre, 2 = right (baseline), rotation in degrees.</summary>
    public void Text(string layer, double x, double y, double height, string text, int align = 0, double rotation = 0)
    {
        Start("TEXT", layer);
        P(10, x, y); G(40, N(height)); G(1, Ascii(text));
        if (rotation != 0) G(50, N(rotation));
        if (align != 0) { G(72, align.ToString(CultureInfo.InvariantCulture)); P(11, x, y); }
        Extend(x + text.Length * height * 0.8, y + height);
    }

    /// <summary>R12 text is ASCII: common symbols are spelt out, anything else becomes '?'.</summary>
    private static string Ascii(string t)
    {
        var sb = new StringBuilder();
        foreach (char c in t)
            sb.Append(c switch
            {
                '\n' or '\r' or '\t' => " ", '–' or '—' or '−' => "-", '×' => "x", '²' => "2", '³' => "3", '°' => "%%d", '±' => "%%p", 'Ø' or '⌀' => "%%c",
                '≤' => "<=", '≥' => ">=", '·' => ".", '’' or '‘' => "'", '“' or '”' => "\"",
                _ when c < 128 => c.ToString(), _ => "?",
            });
        return sb.ToString();
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        void W(int code, string v) { sb.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(v).Append('\n'); }
        bool any = double.IsFinite(_minX);
        W(0, "SECTION"); W(2, "HEADER");
        W(9, "$ACADVER"); W(1, "AC1009");
        W(9, "$INSUNITS"); W(70, "2");
        W(9, "$EXTMIN"); W(10, N(any ? _minX : 0)); W(20, N(any ? _minY : 0)); W(30, "0.0");
        W(9, "$EXTMAX"); W(10, N(any ? _maxX : 1)); W(20, N(any ? _maxY : 1)); W(30, "0.0");
        W(9, "$LTSCALE"); W(40, "1.0");
        W(0, "ENDSEC");
        W(0, "SECTION"); W(2, "TABLES");
        W(0, "TABLE"); W(2, "LTYPE"); W(70, "2");
        W(0, "LTYPE"); W(2, "CONTINUOUS"); W(70, "0"); W(3, "Solid line"); W(72, "65"); W(73, "0"); W(40, "0.0");
        W(0, "LTYPE"); W(2, "DASHED"); W(70, "0"); W(3, "Dashed __ __ __"); W(72, "65"); W(73, "2"); W(40, "3.0"); W(49, "2.0"); W(49, "-1.0");
        W(0, "ENDTAB");
        W(0, "TABLE"); W(2, "LAYER"); W(70, _layers.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (name, (color, lt)) in _layers) { W(0, "LAYER"); W(2, name); W(70, "0"); W(62, color.ToString(CultureInfo.InvariantCulture)); W(6, lt); }
        W(0, "ENDTAB");
        W(0, "ENDSEC");
        W(0, "SECTION"); W(2, "ENTITIES");
        sb.Append(_ent);
        W(0, "ENDSEC");
        W(0, "EOF");
        return sb.ToString();
    }

    public byte[] ToBytes() => Encoding.ASCII.GetBytes(ToText());

    private void Start(string type, string layer)
    {
        EntityCount++;
        G(0, type); string l = Clean(layer); G(8, l);
        if (!_layers.ContainsKey(l)) _layers[l] = (7, "CONTINUOUS");
    }

    private void P(int code, double x, double y)
    {
        G(code, N(x)); G(code + 10, N(y)); G(code + 20, "0.0");
        if (code is 10 or 11) Extend(x, y);
    }

    private void Extend(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        _minX = Math.Min(_minX, x); _minY = Math.Min(_minY, y); _maxX = Math.Max(_maxX, x); _maxY = Math.Max(_maxY, y);
    }

    private void G(int code, string v) => _ent.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(v).Append('\n');

    private static string N(double v) => double.IsFinite(v) ? v.ToString("0.0#####", CultureInfo.InvariantCulture) : "0.0";

    /// <summary>R12 layer names: letters, digits, '-', '_' and '$' only, upper case.</summary>
    private static string Clean(string s) => new string(s.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '$' ? c : '_').ToArray());
}
