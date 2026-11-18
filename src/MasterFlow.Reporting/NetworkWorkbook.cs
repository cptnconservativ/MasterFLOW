using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Reporting;

/// <summary>
/// Excel sheets for a solved storm network: pipes, structures (with the rim criterion check) and the
/// HGL/EGL profile points of every flow path, as numbers so a checker can recompute or chart them.
/// </summary>
public static class NetworkWorkbook
{
    public static IReadOnlyList<XlsxSheet> Sheets(StormNetworkResult r)
    {
        var c = r.Constants;
        var n = r.Network;
        string name = string.IsNullOrWhiteSpace(n.Name) ? "Network" : n.Name;
        double k = NetworkText.SizeToBase(n.Units);
        var sheets = new List<XlsxSheet>();

        var pipes = new List<IReadOnlyList<object?>>
        {
            new object?[] { $"{name}: pipes" },
            new object?[]
            {
                "Pipe", "From", "To", "Shape", $"Rise ({NetworkText.SizeUnit(n.Units)})", $"Span ({NetworkText.SizeUnit(n.Units)})", $"Length ({c.Length})",
                $"Inv up ({c.Length})", $"Inv down ({c.Length})", "Slope", "n", $"Q ({c.Discharge})", "Q source", $"Q {n.MinorStormName} ({c.Discharge})", $"V {n.MinorStormName} ({c.Velocity})", $"Qfull ({c.Discharge})", $"Vfull ({c.Velocity})",
                $"yn ({c.Length})", $"yc ({c.Length})", $"Vn ({c.Velocity})", "Regime", "Condition", "Downstream control",
                $"Depth down ({c.Length})", $"Depth up ({c.Length})", $"V down ({c.Velocity})", $"V up ({c.Velocity})",
                $"HGL down ({c.Length})", $"HGL up ({c.Length})", $"EGL down ({c.Length})", $"EGL up ({c.Length})", $"Energy loss ({c.Length})",
                "Flows full", $"Jump station from upstream ({c.Length})",
            },
        };
        foreach (var p in r.Pipes)
            pipes.Add([p.Pipe.Name, p.Pipe.From, p.Pipe.To, p.Pipe.Shape.ToString(), p.Pipe.Rise / k, p.Pipe.Span / k, p.Pipe.Length,
                p.Pipe.UpstreamInvert, p.Pipe.DownstreamInvert, p.Slope, p.Pipe.ManningN, p.Pipe.Discharge, r.AccumulatedPipes.Contains(p.Pipe.Name) ? "Accumulated" : "Typed", p.MinorDischarge, p.MinorVelocity, p.FullFlowCapacity, p.FullFlowVelocity,
                p.NormalDepth, p.CriticalDepth, p.NormalVelocity, p.Regime, p.Condition, p.DownstreamControl,
                p.DepthDown, p.DepthUp, p.VelocityDown, p.VelocityUp, p.HglDown, p.HglUp, p.EglDown, p.EglUp, p.EnergyLoss,
                p.FlowsFull ? "Yes" : "No", p.JumpStation]);
        pipes.Add([]);
        pipes.Add([$"Tailwater: {NetworkTables.TailwaterText(n)}"]);
        pipes.Add([$"Criteria: {NetworkTables.CriteriaText(n, c)}"]);
        pipes.Add([$"Method: {NetworkTables.Method}"]);
        sheets.Add(new XlsxSheet($"{name} pipes", pipes, 2, [10, 10, 10, 10, 9, 9, 10, 11, 11, 9, 8, 10, 10, 10, 9, 9, 10, 10, 22, 22, 11, 11, 10, 10, 11, 11, 11, 11, 11, 8, 12]));

        var need = n.Criteria.HglBelowRim;
        var needE = n.Criteria.EglBelowRim;
        var st = new List<IReadOnlyList<object?>>
        {
            new object?[] { $"{name}: structures" },
            new object?[]
            {
                "Structure", "Type", "Outfall", $"Inflow ({c.Discharge})", $"Inflow {n.MinorStormName} ({c.Discharge})", $"Rim ({c.Length})", $"HGL ({c.Length})", $"EGL ({c.Length})", $"Rim − HGL ({c.Length})", $"Required ({c.Length})", "HGL check",
                $"Rim − EGL ({c.Length})", $"EGL required ({c.Length})", "EGL check", "Loss method", "Equation", $"hj ({c.Length})", $"V2 out ({c.Velocity})", $"V1 in ({c.Velocity})", "Main inflow",
            },
        };
        foreach (var s in r.Structures)
            st.Add([s.Structure.Name, s.Structure.Kind, s.IsOutfall ? "Yes" : "No", s.Structure.Inflow, s.Structure.MinorInflow, s.Structure.RimElevation, s.Hgl, s.Egl, s.HglClearance, need,
                Check(need, s.HglClearance), s.EglClearance, needE, Check(needE, s.EglClearance), s.LossMethod, s.LossEquation,
                s.IsOutfall ? null : s.Loss, s.IsOutfall ? null : s.OutletVelocity, s.InletVelocity, s.MainInflow]);
        sheets.Add(new XlsxSheet($"{name} structures", st, 2, [12, 14, 8, 10, 10, 10, 12, 12, 10, 12, 12, 10, 26, 30, 10, 11, 11, 12]));

        var prof = new List<IReadOnlyList<object?>> { new object?[] { $"{name}: profile points (station 0 at the outfall, increasing upstream)" } };
        prof.Add(["Path from", "Pipe", $"Station ({c.Length})", $"Invert ({c.Length})", $"Crown ({c.Length})", $"HGL ({c.Length})", $"EGL ({c.Length})"]);
        foreach (var start in NetworkProfile.PathStarts(r))
        {
            var path = r.PathToOutfall(start);
            double sd = 0;
            foreach (var p in path.AsEnumerable().Reverse())
            {
                double su = sd + p.Pipe.Length;
                for (int i = p.Profile.Count - 1; i >= 0; i--)
                {
                    var pt = p.Profile[i];
                    prof.Add([start, p.Pipe.Name, su - pt.Station, pt.Invert, pt.Crown, pt.Hgl, pt.Egl]);
                }
                sd = su;
            }
        }
        sheets.Add(new XlsxSheet($"{name} profile", prof, 2, [12, 10, 12, 12, 12, 12, 12]));

        var starts = NetworkProfile.PathStarts(r);
        var first = starts.Count > 0 ? NetworkProfile.Build(r, starts[0]) : null;
        double interval = NetworkProfile.DefaultInterval(n.Units);
        var rd = new List<IReadOnlyList<object?>>
        {
            new object?[] { $"{name}: readings every {interval.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} {c.Length} (as labelled under the profile). " +
                (first is null ? "" : ProfileFrame.BandCaption(first, c.Length).Replace('\n', ' ')) },
            new object?[] { "Path from", $"Station ({c.Length})", $"HGL ({c.Length})", $"EGL ({c.Length})", $"Ground/rim ({c.Length})", $"{first?.LimitLabel ?? "HGL limit"} ({c.Length})", $"Limit − HGL ({c.Length})", "Check" },
        };
        foreach (var start in starts)
            if (NetworkProfile.Build(r, start) is { } pathProfile)
                foreach (var x in pathProfile.Readings())
                    rd.Add([start, x.Station, x.Hgl, x.Egl, x.Ground, x.Limit, x.Limit - x.Hgl, x.Limit is null ? "" : x.MeetsCriteria ? "OK" : "FAILS"]);
        sheets.Add(new XlsxSheet($"{name} readings", rd, 2, [12, 12, 12, 12, 14, 18, 14, 10]));

        var msg = new List<IReadOnlyList<object?>> { new object?[] { "Severity", "Code", "Message" } };
        foreach (var m in r.Messages) msg.Add([m.Severity.ToString(), m.Code, m.Text]);
        sheets.Add(new XlsxSheet($"{name} messages", msg, 1, [10, 10, 100]));
        return sheets;
    }

    private static string Check(double? need, double? have) =>
        need is null || have is null ? "" : have >= need - 1e-9 ? "OK" : "FAILS";
}
