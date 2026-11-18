using System.Globalization;
using System.Text;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

/// <summary>
/// Calculation titles may contain fields in braces that are replaced with live values, e.g.
///   "{shape} — {D} in RCP, Q = {Q:F1} {Qunit}"  →  "Circular pipe — 36 in RCP, Q = 71.4 cfs".
/// A field may carry a .NET numeric format after a colon (F2, N0, G4…). "{{" and "}}" give literal
/// braces. An unknown field is kept visibly as "{?name}" so a typo is never hidden. Result fields show
/// "—" when there is no valid result.
/// </summary>
public static class TitleExpression
{
    public sealed record Field(string Name, string Meaning);

    public static IReadOnlyList<Field> Fields { get; } =
    [
        new("shape", "Section shape name"),
        new("units", "Unit system (U.S. or SI)"),
        new("folder", "Folder path"), new("item", "Item number"),
        new("Q", "Discharge"), new("Qunit", "Discharge unit"),
        new("yn", "Normal depth"), new("yc", "Critical depth"), new("unit", "Length unit"),
        new("S", "Slope in %"), new("n", "Manning's n"),
        new("V", "Velocity"), new("Fr", "Froude number"), new("regime", "Flow regime"),
        new("A", "Flow area"), new("T", "Top width"),
        new("b", "Bottom width"), new("zL", "Left side slope"), new("zR", "Right side slope"),
        new("D", "Pipe diameter in in/mm"), new("rise", "Ellipse rise in in/mm"), new("span", "Ellipse span in in/mm"),
        new("WSE", "Water-surface elevation (needs an invert or irregular section)"),
        new("pctfull", "Conduit percent full"),
    ];

    public static bool HasFields(string? title) => title is not null && title.Contains('{');

    public static string Evaluate(string? template, UniformFlowResult? result, string folder = "", CultureInfo? culture = null, string item = "")
    {
        if (string.IsNullOrEmpty(template) || !template.Contains('{') && !template.Contains('}')) return template ?? "";
        culture ??= CultureInfo.CurrentCulture;
        var sb = new StringBuilder();
        for (int i = 0; i < template.Length; i++)
        {
            char ch = template[i];
            if (ch == '{' && i + 1 < template.Length && template[i + 1] == '{') { sb.Append('{'); i++; continue; }
            if (ch == '}' && i + 1 < template.Length && template[i + 1] == '}') { sb.Append('}'); i++; continue; }
            if (ch != '{') { sb.Append(ch); continue; }
            int end = template.IndexOf('}', i + 1);
            if (end < 0) { sb.Append(template, i, template.Length - i); break; }
            string token = template[(i + 1)..end];
            int colon = token.IndexOf(':');
            string name = (colon < 0 ? token : token[..colon]).Trim();
            string? format = colon < 0 ? null : token[(colon + 1)..].Trim();
            sb.Append(name == "item" ? item : Value(name, format, result, folder, culture));
            i = end;
        }
        return sb.ToString();
    }

    private static string Value(string name, string? format, UniformFlowResult? r, string folder, CultureInfo culture)
    {
        string Num(double v, string def) => double.IsFinite(v) ? v.ToString(string.IsNullOrEmpty(format) ? def : format, culture) : "—";
        var input = r?.Input;
        var c = input is null ? null : UnitConstants.For(input.Units);
        bool ok = r is { Succeeded: true };
        double small = input?.Units == UnitSystem.SI ? 1000 : 12; // pipe sizes shown in mm / in
        switch (name)
        {
            case "shape": return input?.Section.ShapeName ?? "—";
            case "units": return input is null ? "—" : input.Units == UnitSystem.USCustomary ? "U.S." : "SI";
            case "folder": return folder;
            case "Qunit": return c?.Discharge ?? "—";
            case "unit": return c?.Length ?? "—";
            case "b": return input?.Section switch { RectangularSection s => Num(s.BottomWidth, "0.##"), TrapezoidalSection s => Num(s.BottomWidth, "0.##"), _ => "—" };
            case "zL": return input?.Section switch { TrapezoidalSection s => Num(s.LeftSideSlope, "0.##"), TriangularSection s => Num(s.LeftSideSlope, "0.##"), _ => "—" };
            case "zR": return input?.Section switch { TrapezoidalSection s => Num(s.RightSideSlope, "0.##"), TriangularSection s => Num(s.RightSideSlope, "0.##"), _ => "—" };
            case "D": return input?.Section is CircularSection p ? Num(p.Diameter * small, "0.##") : "—";
            case "rise": return input?.Section is EllipticalSection e1 ? Num(e1.Rise * small, "0.##") : "—";
            case "span": return input?.Section is EllipticalSection e2 ? Num(e2.Span * small, "0.##") : "—";
        }
        if (!ok)
            return Fields.Any(f => f.Name == name) ? "—" : $"{{?{name}}}";
        return name switch
        {
            "Q" => Num(r!.Discharge, "0.###"),
            "yn" => Num(r!.Normal.Depth, "0.###"),
            "yc" => Num(r!.Critical.Depth, "0.###"),
            "S" => Num(r!.Slope * 100, "0.####"),
            "n" => Num(r!.ManningN, "0.0###"),
            "V" => Num(r!.Normal.Velocity, "0.##"),
            "Fr" => Num(r!.Normal.Froude, "0.##"),
            "A" => Num(r!.Normal.Area, "0.##"),
            "T" => Num(r!.Normal.TopWidth, "0.##"),
            "regime" => r!.Regime.ToString(),
            "WSE" => r!.Irregular is { } ir ? Num(ir.WaterSurfaceElevation, "0.###")
                   : input!.InvertElevation is { } z ? Num(z + r.Normal.Depth, "0.###") : "—",
            "pctfull" => r!.Conduit is { } cd ? Num(cd.PercentFull, "0.#") : "—",
            _ => $"{{?{name}}}",
        };
    }
}
