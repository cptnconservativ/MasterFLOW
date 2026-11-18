using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Reporting;

/// <summary>Builds an .xlsx workbook: a comparison sheet across calculations, one sheet per calculation and an optional rating table.</summary>
public static class ProjectWorkbook
{
    public static IReadOnlyList<XlsxSheet> Build(ProjectInfo info, IReadOnlyList<(ChannelCalculation Calc, UniformFlowResult Result)> items, RatingTable? rating = null, string? ratingTitle = null)
        => Build(info, items, rating is null ? [] : [(ratingTitle ?? "Rating table", rating)]);

    /// <summary>Comparison sheet, one sheet per calculation, then one sheet per rating table.</summary>
    public static IReadOnlyList<XlsxSheet> Build(ProjectInfo info, IReadOnlyList<(ChannelCalculation Calc, UniformFlowResult Result)> items, IReadOnlyList<(string Title, RatingTable Table)> ratings, IReadOnlyList<DrainageStructure>? structures = null)
    {
        var sheets = new List<XlsxSheet>();
        var cmp = Comparison.Build(items.Select(i => (i.Calc.Title, i.Result)).ToList());
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Quantity", "Symbol", "Unit" }.Concat(cmp.Titles).ToArray(),
            new object?[] { "Status", "", "" }.Concat(cmp.Statuses).ToArray(),
        };
        string? group = null;
        foreach (var r in cmp.Rows)
        {
            if (r.Group != group) { rows.Add([r.Group]); group = r.Group; }
            rows.Add(new object?[] { r.Label, r.Symbol, r.Unit }.Concat(r.Values.Select(v => (object?)v)).ToArray());
        }
        foreach (var n in cmp.Notes) rows.Add([n]);
        rows.Add([$"Project: {info.Name}  {info.ProjectNumber}  Prepared by: {info.PreparedBy}  Checked by: {info.CheckedBy}"]);
        sheets.Add(new XlsxSheet("Comparison", rows, 1, [28, 14, 10, .. cmp.Titles.Select(_ => 18.0)]));

        foreach (var (calc, r) in items)
        {
            var s = new List<IReadOnlyList<object?>> { new object?[] { calc.Title } };
            if (!string.IsNullOrWhiteSpace(calc.Description)) s.Add([calc.Description]);
            s.Add([]);
            s.Add(["Input", "Value"]);
            foreach (var (label, value) in ResultSummary.InputLines(r.Input)) s.Add([label, value]);
            s.Add([]);
            s.Add(["Result", "Symbol", "Value", "Unit"]);
            if (!r.Succeeded) s.Add(["No result (see messages)"]);
            foreach (var l in ResultSummary.Lines(r)) s.Add([l.Label, l.Symbol, l.Value, l.Unit]);
            var checks = DesignChecks.Evaluate(calc.Criteria, r);
            if (checks.Count > 0)
            {
                s.Add([]);
                s.Add(["Design check", "Criterion", "Computed", "Limit", "Unit", "Result", "Basis"]);
                foreach (var k in checks) s.Add([k.Name, k.Symbol, double.IsFinite(k.Actual) ? k.Actual : null, k.Limit, k.Unit, k.StatusText, k.Basis]);
            }
            if (r.Input.Section is IrregularSection ir)
            {
                s.Add([]);
                s.Add(["Station", "Elevation", "n (to next point)", "Description"]);
                foreach (var p in ir.Points) s.Add([p.Station, p.Elevation, p.ManningN, p.Description]);
            }
            if (r.Irregular is { Subareas.Count: > 0 } irr)
            {
                s.Add([]);
                s.Add(["From station", "To station", "n", "Area", "Wetted perimeter", "Conveyance", "Discharge", "Velocity"]);
                foreach (var sa in irr.Subareas) s.Add([sa.StationLeft, sa.StationRight, sa.ManningN, sa.Area, sa.WettedPerimeter, sa.Conveyance, sa.Discharge, sa.Velocity]);
            }
            s.Add([]);
            s.Add(["Severity", "Code", "Message"]);
            foreach (var m in r.Messages.Concat(DesignChecks.Messages(checks))) s.Add([m.Severity.ToString(), m.Code, m.Text]);
            sheets.Add(new XlsxSheet(calc.Title, s, 1, [30, 14, 16, 14, 16, 14, 14, 14]));
        }

        foreach (var (ratingTitle, rating) in ratings)
        {
            if (rating.Rows.Count == 0) continue;
            var c = UnitConstants.For(rating.Request.Base.Units);
            var t = new List<IReadOnlyList<object?>>
            {
                new object?[] { $"Depth ({c.Length})", $"Q ({c.Discharge})", $"A ({c.Area})", $"V ({c.Velocity})", $"T ({c.Length})", "Fr", $"yc ({c.Length})" },
            };
            foreach (var row in rating.Rows) t.Add([row.Depth, row.Discharge, row.Area, row.Velocity, row.TopWidth, row.Froude, row.CriticalDepth]);
            sheets.Add(new XlsxSheet(ratingTitle, t, 1, [12, 12, 12, 12, 12, 10, 12]));
        }
        if (structures is { Count: > 0 })
        {
            var t = new List<IReadOnlyList<object?>>
            {
                new object?[] { "Drainage structures (values as in the imported file; no unit conversion)" },
                new object?[] { "Network", "Structure", "Type", "Northing", "Easting", "Rim elev.", "Sump elev.", "Inverts (pipe, in/out, elevation)", "Description" },
            };
            foreach (var st in structures)
                t.Add([st.Network, st.Name, st.Kind, st.Northing, st.Easting, st.RimElevation, st.SumpElevation,
                    string.Join("; ", st.Inverts.Select(v => $"{v.Pipe} {v.FlowDirection} {v.Elevation.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}")), st.Description]);
            sheets.Add(new XlsxSheet("Structures", t, 2, [14, 16, 18, 14, 14, 11, 11, 44, 24]));
        }
        return sheets;
    }
}
