using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

/// <summary>One labelled output value with its unit and display precision.</summary>
public sealed record ResultLine(string Group, string Label, string Symbol, double Value, string Unit, int Decimals, bool IsSolved = false)
{
    public string Formatted => UnitFormat.Number(Value, Decimals);
}

/// <summary>
/// The single, ordered list of labelled results used by the desktop UI, reports and exports, so that
/// every surface shows identical labels, units and precision.
/// </summary>
public static class ResultSummary
{
    public static IReadOnlyList<ResultLine> Lines(UniformFlowResult r)
    {
        if (!r.Succeeded) return [];
        var c = r.Constants;
        var solved = r.Input.SolveFor;
        const string G = "Governing", N = "Normal flow", C = "Critical flow", P = "Conduit";
        var lines = new List<ResultLine>
        {
            new(G, "Discharge", "Q", r.Discharge, c.Discharge, 3, solved == SolveFor.Discharge),
            new(G, "Normal depth", "yn", r.Normal.Depth, c.Length, 3, solved == SolveFor.NormalDepth),
            new(G, "Channel slope", "S", r.Slope * 100, "%", 4, solved == SolveFor.Slope),
            new(G, "Manning's n", "n", r.ManningN, "", 4, solved == SolveFor.Roughness),

            new(N, "Flow area", "A", r.Normal.Area, c.Area, 3),
            new(N, "Wetted perimeter", "P", r.Normal.WettedPerimeter, c.Length, 3),
            new(N, "Hydraulic radius", "R", r.Normal.HydraulicRadius, c.Length, 4),
            new(N, "Top width", "T", r.Normal.TopWidth, c.Length, 3),
            new(N, "Hydraulic depth", "D", r.Normal.HydraulicDepth, c.Length, 4),
            new(N, "Velocity", "V", r.Normal.Velocity, c.Velocity, 3),
            new(N, "Velocity head", "V²/2g", r.Normal.VelocityHead, c.Length, 4),
            new(N, "Specific energy", "E", r.Normal.SpecificEnergy, c.Length, 4),
            new(N, "Froude number", "Fr", r.Normal.Froude, "", 3),
            new(N, "Conveyance", "K", r.Conveyance, c.Conveyance, 2),
            new(N, "Section factor", "AR²ᐟ³", r.UniformFlowSectionFactor, c.Length + "⁸ᐟ³", 4),

            new(C, "Critical depth", "yc", r.Critical.Depth, c.Length, 3),
            new(C, "Critical velocity", "Vc", r.Critical.Velocity, c.Velocity, 3),
            new(C, "Critical area", "Ac", r.Critical.Area, c.Area, 3),
            new(C, "Critical top width", "Tc", r.Critical.TopWidth, c.Length, 3),
            new(C, "Critical energy", "Ec", r.Critical.SpecificEnergy, c.Length, 4),
            new(C, "Critical slope", "Sc", r.CriticalSlope * 100, "%", 4),
            new(C, "Critical sect. factor", "Z", r.CriticalSectionFactor, c.Length + "⁵ᐟ²", 4),
        };
        if (r.Conduit is { } p)
        {
            if (double.IsFinite(p.AlternateNormalDepth))
                lines.Insert(2, new(G, "Alt. normal depth", "yn₂", p.AlternateNormalDepth, c.Length, 3));
            lines.AddRange(
            [
                new(P, "Depth / diameter", "y/D", p.PercentFull / 100, "", 4),
                new(P, "Percent full", "", p.PercentFull, "%", 2),
                new(P, "Full-flow Q", "Qfull", p.FullFlowDischarge, c.Discharge, 3),
                new(P, "Full-flow V", "Vfull", p.FullFlowVelocity, c.Velocity, 3),
                new(P, "Slope for Q just full", "Sfull", p.FullFlowSlope * 100, "%", 4),
                new(P, "Max. free-surface Q", "Qmax", p.MaxDischarge, c.Discharge, 3),
                new(P, "Depth at max. Q", "y(Qmax)", p.DepthAtMaxDischarge, c.Length, 3),
                new(P, "Q / Qfull", "", r.Discharge / p.FullFlowDischarge, "", 4),
            ]);
        }
        return lines;
    }

    public static string RegimeText(UniformFlowResult r) => r.Regime switch
    {
        FlowRegime.Subcritical => "Subcritical (Fr < 0.86)",
        FlowRegime.Supercritical => "Supercritical (Fr > 1.13)",
        FlowRegime.Undefined => "Conduit flowing full — Froude number undefined",
        _ => "Near-critical (0.86 ≤ Fr ≤ 1.13) — unstable",
    };

    public static IReadOnlyList<(string Label, string Value)> InputLines(UniformFlowInput i)
    {
        var c = UnitConstants.For(i.Units);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string Num(double v, int d = 3) => v.ToString("F" + d, inv);
        var list = new List<(string, string)>
        {
            ("Section", i.Section.ShapeName),
            ("Unit system", i.Units == UnitSystem.USCustomary ? $"U.S. customary (k = {c.ManningK}, g = {c.Gravity} ft/s²)" : $"SI (k = {c.ManningK}, g = {c.Gravity} m/s²)"),
            ("Solve for", i.SolveFor switch { SolveFor.Discharge => "Discharge", SolveFor.NormalDepth => "Normal depth", SolveFor.Slope => "Channel slope", _ => "Manning's n" }),
        };
        switch (i.Section)
        {
            case RectangularSection r: list.Add(("Bottom width b", $"{Num(r.BottomWidth)} {c.Length}")); break;
            case TrapezoidalSection t:
                list.Add(("Bottom width b", $"{Num(t.BottomWidth)} {c.Length}"));
                list.Add(("Left side slope zL", $"{Num(t.LeftSideSlope)} H : 1 V"));
                list.Add(("Right side slope zR", $"{Num(t.RightSideSlope)} H : 1 V"));
                break;
            case TriangularSection v:
                list.Add(("Left side slope zL", $"{Num(v.LeftSideSlope)} H : 1 V"));
                list.Add(("Right side slope zR", $"{Num(v.RightSideSlope)} H : 1 V"));
                break;
            case CircularSection p:
                list.Add(("Diameter D", i.Units == UnitSystem.USCustomary
                    ? $"{Num(p.Diameter * 12, 2)} in ({Num(p.Diameter, 4)} ft)"
                    : $"{Num(p.Diameter * 1000, 0)} mm ({Num(p.Diameter, 4)} m)"));
                break;
        }
        if (i.Section.ChannelDepth is { } cd) list.Add(("Channel depth", $"{Num(cd)} {c.Length}"));
        if (i.SolveFor != SolveFor.Roughness) list.Add(("Manning's n", Num(i.ManningN, 4)));
        if (i.SolveFor != SolveFor.Slope) list.Add(("Channel slope S", $"{Num(i.Slope * 100, 4)} % ({i.Slope.ToString("G6", inv)} {c.Length}/{c.Length})"));
        if (i.SolveFor != SolveFor.Discharge) list.Add(("Discharge Q", $"{Num(i.Discharge)} {c.Discharge}"));
        if (i.SolveFor != SolveFor.NormalDepth) list.Add(("Flow depth y", $"{Num(i.Depth)} {c.Length}"));
        return list;
    }
}
