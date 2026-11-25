using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics.Geometry;
using MasterFlow.Hydraulics.Numerics;

namespace MasterFlow.Hydraulics;

/// <summary>One point of the computed profile along a pipe; Station is measured from the upstream end.</summary>
public sealed record PipeProfilePoint(double Station, double Invert, double Crown, double Hgl, double Egl);

public sealed record PipeHydraulicResult
{
    public required NetworkPipe Pipe { get; init; }
    public double Slope { get; init; }
    public double NormalDepth { get; init; }          // NaN: no free-surface normal depth (pressure flow or flat/adverse)
    public double CriticalDepth { get; init; }
    public double FullFlowCapacity { get; init; }     // NaN for flat/adverse pipes
    public double FullFlowVelocity { get; init; }
    public double NormalVelocity { get; init; }       // NaN when there is no normal depth
    /// <summary>Minor-storm discharge (NaN when none) and its mean velocity at normal depth (full-flow velocity if it would flow full).</summary>
    public double MinorDischarge { get; init; } = double.NaN;
    public double MinorVelocity { get; init; } = double.NaN;
    /// <summary>Mild, Steep, Flat/adverse or Pressure (Q above free-surface capacity).</summary>
    public string Regime { get; init; } = "";
    /// <summary>Plain-language flow condition, e.g. "Part full (subcritical backwater)", "Full (pressure)".</summary>
    public string Condition { get; init; } = "";
    public double DepthUp { get; init; }
    public double DepthDown { get; init; }
    public double VelocityUp { get; init; }
    public double VelocityDown { get; init; }
    public double HglUp { get; init; }
    public double HglDown { get; init; }
    public double EglUp { get; init; }
    public double EglDown { get; init; }
    /// <summary>Energy lost in the pipe, EGL up − EGL down (friction; plus a hydraulic jump when there is one).</summary>
    public double EnergyLoss { get; init; }
    public bool FlowsFull { get; init; }
    /// <summary>Station of a hydraulic jump inside the pipe (from the upstream end), when there is one.</summary>
    public double? JumpStation { get; init; }
    /// <summary>Water-surface elevation imposed at the downstream end (tailwater or downstream structure).</summary>
    public double DownstreamControl { get; init; }
    public IReadOnlyList<PipeProfilePoint> Profile { get; init; } = [];
}

public sealed record StructureHydraulicResult
{
    public required NetworkStructure Structure { get; init; }
    public bool IsOutfall { get; init; }
    /// <summary>Water surface in the structure (HGL of the inflow side).</summary>
    public double Hgl { get; init; }
    public double Egl { get; init; }
    public string LossMethod { get; init; } = "";
    public string LossEquation { get; init; } = "";
    /// <summary>HGL rise across the structure, hj (≥ 0).</summary>
    public double Loss { get; init; }
    public double OutletVelocity { get; init; }       // V2 / Vo
    public double InletVelocity { get; init; }        // V1 (main inflow); NaN when none
    public string MainInflow { get; init; } = "";
    public double? HglClearance { get; init; }        // rim − HGL
    public double? EglClearance { get; init; }        // rim − EGL
}

public sealed record StormNetworkResult
{
    public bool Succeeded { get; init; }
    public required StormNetwork Network { get; init; }
    public required UnitConstants Constants { get; init; }
    public IReadOnlyList<PipeHydraulicResult> Pipes { get; init; } = [];
    public IReadOnlyList<StructureHydraulicResult> Structures { get; init; } = [];
    public IReadOnlyList<CalcMessage> Messages { get; init; } = [];
    public IReadOnlyList<string> Trace { get; init; } = [];
    /// <summary>Pipes whose Q was accumulated from structure inflows (not typed).</summary>
    public IReadOnlySet<string> AccumulatedPipes { get; init; } = new HashSet<string>();

    public PipeHydraulicResult? Pipe(string name) => Pipes.FirstOrDefault(p => p.Pipe.Name == name);
    public StructureHydraulicResult? Structure(string name) => Structures.FirstOrDefault(s => s.Structure.Name == name);

    /// <summary>Pipes from <paramref name="upstreamStructure"/> down to its outfall, upstream first.</summary>
    public IReadOnlyList<PipeHydraulicResult> PathToOutfall(string upstreamStructure)
    {
        var list = new List<PipeHydraulicResult>();
        string at = upstreamStructure;
        var seen = new HashSet<string>();
        while (seen.Add(at) && Pipes.FirstOrDefault(p => p.Pipe.From == at) is { } p) { list.Add(p); at = p.Pipe.To; }
        return list;
    }
}

/// <summary>
/// Steady hydraulic and energy grade lines through a dendritic storm sewer network, computed from each
/// outfall upstream (Bernoulli / energy equation, as required by Rapid City IDCM 2012 §4.6.7).
///
/// Pipes: the standard-step energy equation z₁ + E₁ = z₂ + E₂ + S̄f·Δx is integrated upstream from the
/// downstream water surface, with E = y + V²/2g and Sf = (Q n / (k A R^⅔))². When the water surface is
/// above the crown, A and P are the full-pipe values and y is the pressure head, so full, part-full and
/// surcharged reaches use one equation. The depth at the downstream end is never below critical depth. If
/// no subcritical solution exists (steep pipe), the pipe is computed supercritical from critical depth at
/// its entrance, and a hydraulic jump is placed where the momentum functions of the two profiles are equal.
///
/// Structures: the HGL rises by hj across each structure, from Rapid City IDCM Figures 4-6/4-7
/// (hj = V2²/2g − c·V1²/2g; beginning of line 1.25·V2²/2g; bends) or Eq. 4-6-1 (hs = K·Vo²/2g). V2/Vo is
/// the outlet pipe velocity at its upstream end, V1 the main (largest-Q) inflow pipe velocity at its
/// downstream end, found by iteration because V1 depends on the structure water level. A negative hj
/// is taken as 0 and reported. Every inflow pipe starts from the structure water level.
/// Reference: V. T. Chow, Open-Channel Hydraulics (1959), ch. 10 (standard step) and 3-5 (momentum).
/// </summary>
public static class StormNetworkSolver
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static StormNetworkResult Solve(StormNetwork net)
    {
        var c = UnitConstants.For(net.Units);
        var messages = new List<CalcMessage>();
        var trace = new List<string>();
        StormNetworkResult Fail() => new() { Succeeded = false, Network = net, Constants = c, Messages = messages, Trace = trace };

        // ---- validation
        var structs = new Dictionary<string, NetworkStructure>(StringComparer.Ordinal);
        foreach (var s in net.Structures)
        {
            if (string.IsNullOrWhiteSpace(s.Name)) { messages.Add(CalcMessage.Error("E-NW-001", "A structure has no name.")); continue; }
            if (!structs.TryAdd(s.Name, s)) messages.Add(CalcMessage.Error("E-NW-001", $"Structure name \"{s.Name}\" is used twice."));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in net.Pipes)
        {
            string w = $"Pipe \"{p.Name}\"";
            if (string.IsNullOrWhiteSpace(p.Name) || !names.Add(p.Name)) messages.Add(CalcMessage.Error("E-NW-002", $"Pipe name \"{p.Name}\" is missing or used twice."));
            if (!structs.ContainsKey(p.From)) messages.Add(CalcMessage.Error("E-NW-003", $"{w}: upstream structure \"{p.From}\" is not in the structure list."));
            if (!structs.ContainsKey(p.To)) messages.Add(CalcMessage.Error("E-NW-003", $"{w}: downstream structure \"{p.To}\" is not in the structure list."));
            if (p.From == p.To) messages.Add(CalcMessage.Error("E-NW-003", $"{w} starts and ends at the same structure."));
            if (!(p.Rise > 0) || !double.IsFinite(p.Rise)) messages.Add(CalcMessage.Error("E-NW-004", $"{w}: size must be a positive number."));
            if (p.Shape == NetworkPipeShape.Elliptical && !(p.Span > 0)) messages.Add(CalcMessage.Error("E-NW-004", $"{w}: elliptical span must be a positive number."));
            if (!(p.Length > 0) || !double.IsFinite(p.Length)) messages.Add(CalcMessage.Error("E-NW-005", $"{w}: length must be a positive number."));
            if (!(p.ManningN > 0) || !double.IsFinite(p.ManningN)) messages.Add(CalcMessage.Error("E-NW-006", $"{w}: Manning's n must be a positive number."));
            if (double.IsFinite(p.Discharge) && !(p.Discharge >= 0)) messages.Add(CalcMessage.Error("E-NW-007", $"{w}: design discharge must be zero or more."));
            if (!double.IsNaN(p.MinorDischarge) && !(double.IsFinite(p.MinorDischarge) && p.MinorDischarge >= 0)) messages.Add(CalcMessage.Error("E-NW-007", $"{w}: minor-storm discharge must be zero or more."));
            if (!double.IsFinite(p.UpstreamInvert) || !double.IsFinite(p.DownstreamInvert)) messages.Add(CalcMessage.Error("E-NW-008", $"{w}: both inverts are required."));
        }
        if (messages.Any(m => m.Severity == MessageSeverity.Error)) return Fail();

        // A structure with several outlet pipes usually has pipes drawn against the flow: its outlet is the
        // lowest invert, and the flat or adverse ones are inflows (PipeFlowDirection, D-703).
        (net, var orientation) = PipeFlowDirection.Orient(net, allPipes: false);
        foreach (var note in orientation.Notes) messages.Add(CalcMessage.Info("I-NW-005", note));
        var outlet = new Dictionary<string, NetworkPipe>();
        foreach (var g in net.Pipes.GroupBy(p => p.From))
        {
            if (g.Count() > 1) messages.Add(CalcMessage.Error("E-NW-009", $"Structure \"{g.Key}\" has {g.Count()} outlet pipes that slope away from it or share its lowest invert: " +
                $"{PipeFlowDirection.DescribeOutlets(g.Select(p => new PipeFlowDirection.Link(p.Name, p.From, p.To, p.UpstreamInvert, p.DownstreamInvert)))}. Split flows are not supported; check the inverts."));
            else outlet[g.Key] = g.First();
        }
        var inflows = net.Pipes.GroupBy(p => p.To).ToDictionary(g => g.Key, g => g.ToList());
        // cycle check: follow outlets from every structure
        foreach (var start in structs.Keys)
        {
            var seen = new HashSet<string>();
            for (string at = start; outlet.TryGetValue(at, out var o); at = o.To)
                if (!seen.Add(at)) { messages.Add(CalcMessage.Error("E-NW-010", $"The pipes form a loop through \"{at}\"; looped networks are not supported.")); break; }
        }
        var outfalls = structs.Keys.Where(s => !outlet.ContainsKey(s) && inflows.ContainsKey(s)).ToList();
        if (outfalls.Count == 0 && net.Pipes.Count > 0) messages.Add(CalcMessage.Error("E-NW-011", "No outfall: every structure with pipes has an outlet pipe."));
        if (net.Pipes.Count == 0) messages.Add(CalcMessage.Error("E-NW-012", "The network has no pipes."));
        if (net.Tailwater == TailwaterRule.Elevation && net.TailwaterElevation is not { } twe0)
            messages.Add(CalcMessage.Error("E-NW-013", "Tailwater rule is a known elevation, but no tailwater elevation is entered."));
        foreach (var s in net.Structures)
        {
            if (s.Inflow is { } qi && (!double.IsFinite(qi) || qi < 0)) messages.Add(CalcMessage.Error("E-NW-014", $"Structure \"{s.Name}\": inflow must be zero or more."));
            if (s.MinorInflow is { } q2i && (!double.IsFinite(q2i) || q2i < 0)) messages.Add(CalcMessage.Error("E-NW-014", $"Structure \"{s.Name}\": minor-storm inflow must be zero or more."));
        }
        if (messages.Any(m => m.Severity == MessageSeverity.Error)) return Fail();

        // ---- flow accumulation: a pipe without a typed Q carries its upstream structure's inflow plus every pipe into that structure
        var accumulated = new HashSet<string>(StringComparer.Ordinal);
        var qOf = new Dictionary<string, double>(StringComparer.Ordinal);
        double Q(NetworkPipe p)
        {
            if (qOf.TryGetValue(p.Name, out double q)) return q;
            if (double.IsFinite(p.Discharge)) q = p.Discharge;
            else
            {
                double? inflow = structs[p.From].Inflow;
                var ups = inflows.TryGetValue(p.From, out var list) ? list : [];
                q = (inflow ?? 0) + ups.Sum(Q);
                if (inflow is null && ups.Count == 0) q = double.NaN;
                accumulated.Add(p.Name);
                trace.Add(F($"Pipe {p.Name}: Q = {(inflow ?? 0):G6} (inflow at {p.From}){string.Concat(ups.Select(u => F($" + {Q(u):G6} ({u.Name})")))} = {q:G6}."));
            }
            return qOf[p.Name] = q;
        }
        // minor (check) storm, accumulated the same way; optional
        var q2Of = new Dictionary<string, double>(StringComparer.Ordinal);
        double Q2(NetworkPipe p)
        {
            if (q2Of.TryGetValue(p.Name, out double q)) return q;
            if (double.IsFinite(p.MinorDischarge)) q = p.MinorDischarge;
            else
            {
                double? inflow = structs[p.From].MinorInflow;
                var ups = inflows.TryGetValue(p.From, out var list) ? list : [];
                var upq = ups.Select(Q2).ToList();
                q = inflow is null && upq.All(double.IsNaN) ? double.NaN : (inflow ?? 0) + upq.Where(double.IsFinite).Sum();
                if (double.IsFinite(q) && upq.Any(double.IsNaN))
                    messages.Add(CalcMessage.Warning("W-NW-009", F($"Pipe \"{p.Name}\": some pipes upstream have no {net.MinorStormName} flow; its {net.MinorStormName} Q ({q:G6}) counts only those that do.")));
            }
            return q2Of[p.Name] = q;
        }
        net = net with { Pipes = net.Pipes.Select(p => p with { Discharge = Q(p), MinorDischarge = Q2(p) }).ToList() };
        foreach (var p in net.Pipes.Where(p => !double.IsFinite(p.Discharge)))
            messages.Add(CalcMessage.Error("E-NW-007", $"Pipe \"{p.Name}\": no design Q. Type Q for the pipe, or an inflow at \"{p.From}\" or a structure upstream of it."));
        if (messages.Any(m => m.Severity == MessageSeverity.Error)) return Fail();
        outlet = net.Pipes.GroupBy(p => p.From).ToDictionary(g => g.Key, g => g.First());
        inflows = net.Pipes.GroupBy(p => p.To).ToDictionary(g => g.Key, g => g.ToList());

        trace.Add(F($"Units {(net.Units == UnitSystem.SI ? "SI" : "U.S. customary")}: k = {c.ManningK}, g = {c.Gravity}. Outfalls: {string.Join(", ", outfalls)}. Tailwater rule: {net.Tailwater}{(net.Tailwater == TailwaterRule.Elevation ? F($" ({net.TailwaterElevation:G10})") : "")}."));

        var pipeResults = new Dictionary<string, PipeHydraulicResult>();
        var structResults = new Dictionary<string, StructureHydraulicResult>();

        try
        {
            foreach (var of in outfalls)
            {
                double ofHgl = double.NegativeInfinity, ofEgl = double.NegativeInfinity;
                foreach (var p in inflows[of])
                {
                    var h = new PipeHydraulics(p, c);
                    double ws = net.Tailwater switch
                    {
                        TailwaterRule.FreeOutfall => p.DownstreamInvert + h.Yc,
                        TailwaterRule.HalfCriticalPlusDiameter => p.DownstreamInvert + 0.5 * (h.Yc + h.D),
                        TailwaterRule.Crown => p.DownstreamInvert + h.D,
                        _ => net.TailwaterElevation!.Value,
                    };
                    trace.Add(F($"Outfall {of}: pipe {p.Name} starts from water surface {ws:F4} (yc = {h.Yc:F4}, D = {h.D:F4})."));
                    var r = h.Solve(ws, trace);
                    pipeResults[p.Name] = r;
                    ofHgl = Math.Max(ofHgl, net.Tailwater == TailwaterRule.Elevation ? ws : r.HglDown);
                    ofEgl = Math.Max(ofEgl, r.EglDown);
                    Upstream(p.From, r);
                }
                structResults[of] = Check(net, c, new StructureHydraulicResult
                {
                    Structure = structs[of], IsOutfall = true, Hgl = ofHgl, Egl = Math.Max(ofEgl, ofHgl),
                    LossMethod = "Outfall", LossEquation = "Tailwater: " + net.Tailwater, InletVelocity = double.NaN,
                });
            }
        }
        catch (NonConvergenceException ex)
        {
            messages.Add(CalcMessage.Error("E-NW-020", ex.Message));
            return Fail();
        }

        // structures not on any pipe
        foreach (var s in structs.Values.Where(s => !structResults.ContainsKey(s.Name)))
            messages.Add(CalcMessage.Info("I-NW-001", $"Structure \"{s.Name}\" is not connected to any pipe; not computed."));

        var pipes = net.Pipes.Where(p => pipeResults.ContainsKey(p.Name)).Select(p => WithMinor(pipeResults[p.Name], c, trace)).ToList();
        var structures = net.Structures.Where(s => structResults.ContainsKey(s.Name)).Select(s => structResults[s.Name]).ToList();
        messages.AddRange(Checks(net, c, pipes, structures));
        return new StormNetworkResult { Succeeded = true, Network = net, Constants = c, Pipes = pipes, Structures = structures, Messages = messages, Trace = trace, AccumulatedPipes = accumulated };

        // Structure at the upstream end of `outPipe`: loss, then every inflow pipe.
        void Upstream(string name, PipeHydraulicResult outPipe)
        {
            var s = structs[name];
            double v2 = outPipe.VelocityUp, hv2 = v2 * v2 / (2 * c.Gravity);
            if (!inflows.TryGetValue(name, out var ins) || ins.Count == 0)
            {
                var (method, eq, h) = Loss(s, net, 0, hv2, double.NaN);
                structResults[name] = Check(net, c, new StructureHydraulicResult
                {
                    Structure = s, Hgl = outPipe.HglUp + h, Egl = outPipe.HglUp + h, Loss = h, LossMethod = method, LossEquation = eq,
                    OutletVelocity = v2, InletVelocity = double.NaN,
                });
                trace.Add(F($"Structure {name}: {method}, hj = {h:F4}; water surface {outPipe.HglUp + h:F4}."));
                return;
            }
            var main = ins.OrderByDescending(p => p.Discharge).First();
            var mainH = new PipeHydraulics(main, c);
            double v1 = double.IsFinite(mainH.Vn) ? mainH.Vn : mainH.Vfull, hgl = double.NaN, hjRaw = 0, hj = 0;
            PipeHydraulicResult? mainR = null;
            string methodName = "", equation = "";
            bool converged = false;
            for (int it = 0; it < 60; it++)
            {
                (methodName, equation, hjRaw) = Loss(s, net, ins.Count, hv2, v1 * v1 / (2 * c.Gravity));
                hj = Math.Max(0, hjRaw);
                double newHgl = outPipe.HglUp + hj;
                mainR = mainH.Solve(newHgl, null);
                double newV1 = mainR.VelocityDown;
                bool done = double.IsFinite(hgl) && Math.Abs(newHgl - hgl) < 1e-9 * (1 + Math.Abs(newHgl)) && Math.Abs(newV1 - v1) < 1e-9 * (1 + v1);
                hgl = newHgl;
                v1 = it < 20 ? newV1 : 0.5 * (v1 + newV1); // damp if slow
                if (done) { converged = true; break; }
            }
            if (!converged) throw new NonConvergenceException($"Structure \"{name}\": the loss and the inflow velocity did not converge in 60 iterations; no HGL is reported.");
            if (hjRaw < 0) messages.Add(CalcMessage.Info("I-NW-002", F($"Structure \"{name}\": computed hj = {hjRaw:F3} {c.Length} (the inflow is faster than the outflow); taken as 0.")));
            trace.Add(F($"Structure {name}: {methodName}, V2 = {v2:F3}, V1 ({main.Name}) = {v1:F3}, hj = {hj:F4}; water surface {hgl:F4}."));
            pipeResults[main.Name] = mainR!;
            foreach (var p in ins.Where(p => p != main))
                pipeResults[p.Name] = new PipeHydraulics(p, c).Solve(hgl, trace);
            structResults[name] = Check(net, c, new StructureHydraulicResult
            {
                Structure = s, Hgl = hgl, Egl = Math.Max(hgl, mainR!.EglDown), Loss = hj, LossMethod = methodName, LossEquation = equation,
                OutletVelocity = v2, InletVelocity = v1, MainInflow = main.Name,
            });
            foreach (var p in ins) Upstream(p.From, pipeResults[p.Name]);
        }
    }

    /// <summary>(method name, equation text, hj) for a structure. hv2 = V2²/2g, hv1 = V1²/2g of the main inflow.</summary>
    public static (string Method, string Equation, double Loss) Loss(NetworkStructure s, StormNetwork net, int inflowCount, double hv2, double hv1)
    {
        var m = s.LossMethod;
        if (m == StructureLossMethod.Auto)
            m = inflowCount == 0 ? StructureLossMethod.BeginningOfLine
              : inflowCount == 1 ? StructureLossMethod.InletOnMainLine
              : s.Kind.Contains("Inlet", StringComparison.OrdinalIgnoreCase) ? StructureLossMethod.InletWithBranchLateral : StructureLossMethod.ManholeWith90Lateral;
        if (inflowCount == 0 && m is not (StructureLossMethod.StandardK or StructureLossMethod.BeginningOfLine))
            m = StructureLossMethod.BeginningOfLine; // a case with V1 needs an inflow pipe
        double? cc = m switch
        {
            StructureLossMethod.InletOnMainLine => 0.5,
            StructureLossMethod.InletWithBranchLateral => 0.25,
            StructureLossMethod.ManholeWith45Lateral => 0.5,
            StructureLossMethod.ManholeWith90Lateral => 0.25,
            StructureLossMethod.ManholeWith60Lateral => 0.35,
            StructureLossMethod.ManholeWith22Lateral => 0.75,
            StructureLossMethod.Wye45 => 0.75,
            _ => null,
        };
        string label = m switch
        {
            StructureLossMethod.BeginningOfLine => "IDCM Fig. 4-7 Case VI, beginning of line",
            StructureLossMethod.InletOnMainLine => "IDCM Fig. 4-6 Case I, on main line",
            StructureLossMethod.InletWithBranchLateral => "IDCM Fig. 4-6 Case II, inlet with branch lateral",
            StructureLossMethod.ManholeWith45Lateral => "IDCM Fig. 4-6 Case III, 45° lateral",
            StructureLossMethod.ManholeWith90Lateral => "IDCM Fig. 4-6 Case IV, 90° lateral",
            StructureLossMethod.ManholeWith60Lateral => "IDCM Fig. 4-6 note, 60° lateral",
            StructureLossMethod.ManholeWith22Lateral => "IDCM Fig. 4-6 note, 22½° lateral",
            StructureLossMethod.Wye45 => "IDCM Fig. 4-7 Case V, 45° wye",
            StructureLossMethod.Bend90 => "IDCM Fig. 4-7 Case VIII, 90° bend",
            StructureLossMethod.Bend60 => "IDCM Fig. 4-7 Case VIII, 60° bend",
            StructureLossMethod.Bend45 => "IDCM Fig. 4-7 Case VIII, 45° bend",
            StructureLossMethod.Bend22 => "IDCM Fig. 4-7 Case VIII, 22½° bend",
            _ => "IDCM Eq. 4-6-1, standard method",
        };
        if (cc is { } k1) return (label, F($"hj = V2²/2g − {k1}·V1²/2g"), hv2 - k1 * hv1);
        double kb = m switch
        {
            StructureLossMethod.BeginningOfLine => 1.25,
            StructureLossMethod.Bend90 => 0.50,
            StructureLossMethod.Bend60 => 0.43,
            StructureLossMethod.Bend45 => 0.35,
            StructureLossMethod.Bend22 => 0.20,
            _ => s.JunctionK ?? net.DefaultJunctionK,
        };
        return (label, m == StructureLossMethod.StandardK ? F($"hs = K·Vo²/2g, K = {kb}") : F($"hj = {kb}·V2²/2g"), kb * hv2);
    }

    /// <summary>Normal-depth velocity at the minor-storm Q (full-flow velocity if that Q exceeds the full-flow capacity).</summary>
    private static PipeHydraulicResult WithMinor(PipeHydraulicResult r, UnitConstants c, List<string> trace)
    {
        double q2 = r.Pipe.MinorDischarge;
        if (!double.IsFinite(q2)) return r;
        var h = new PipeHydraulics(r.Pipe with { Discharge = q2 }, c);
        double v = double.IsFinite(h.Vn) ? h.Vn : h.Vfull;
        trace.Add(F($"Pipe {r.Pipe.Name}: minor-storm Q = {q2:G6}, mean velocity {v:F4} ({(double.IsFinite(h.Vn) ? "normal depth" : "full flow")})."));
        return r with { MinorDischarge = q2, MinorVelocity = v };
    }

    private static StructureHydraulicResult Check(StormNetwork net, UnitConstants c, StructureHydraulicResult r) =>
        r.Structure.RimElevation is { } rim ? r with { HglClearance = rim - r.Hgl, EglClearance = rim - r.Egl } : r;

    private static IEnumerable<CalcMessage> Checks(StormNetwork net, UnitConstants c, IReadOnlyList<PipeHydraulicResult> pipes, IReadOnlyList<StructureHydraulicResult> structs)
    {
        var k = net.Criteria;
        string L = c.Length, V = c.Velocity;
        foreach (var s in structs)
        {
            if (s.Structure.RimElevation is null)
            {
                if (k.HglBelowRim is not null || k.EglBelowRim is not null)
                    yield return CalcMessage.Info("I-NW-003", $"Structure \"{s.Structure.Name}\" has no rim elevation; HGL/EGL clearance not checked.");
                continue;
            }
            if (k.HglBelowRim is { } need && s.HglClearance < need)
                yield return CalcMessage.Warning("W-NW-001", F($"Structure \"{s.Structure.Name}\": HGL {s.Hgl:F2} is {s.HglClearance:F2} {L} below the rim; at least {need:F2} {L} is required."));
            if (k.EglBelowRim is { } needE && s.EglClearance < needE)
                yield return CalcMessage.Warning("W-NW-002", F($"Structure \"{s.Structure.Name}\": EGL {s.Egl:F2} is {s.EglClearance:F2} {L} below the rim; at least {needE:F2} {L} is required."));
        }
        foreach (var p in pipes)
        {
            string n = p.Pipe.Name;
            double vmax = Math.Max(p.VelocityUp, p.VelocityDown);
            if (k.MaxVelocity is { } mx && vmax > mx)
                yield return CalcMessage.Warning("W-NW-003", F($"Pipe \"{n}\": velocity {vmax:F2} {V} exceeds {mx:F2} {V}."));
            if (k.MinVelocity is { } mn)
            {
                if (double.IsFinite(p.MinorDischarge))
                {
                    if (p.MinorDischarge > 0 && p.MinorVelocity < mn)
                        yield return CalcMessage.Warning("W-NW-004", F($"Pipe \"{n}\": mean velocity {p.MinorVelocity:F2} {V} at the {net.MinorStormName} flow ({p.MinorDischarge:G4} {c.Discharge}) is below {mn:F2} {V}."));
                }
                else
                {
                    double vref = double.IsFinite(p.NormalVelocity) ? p.NormalVelocity : p.FullFlowVelocity;
                    if (p.Pipe.Discharge > 0 && vref < mn)
                        yield return CalcMessage.Warning("W-NW-004", F($"Pipe \"{n}\": mean velocity {vref:F2} {V} at the design flow is below {mn:F2} {V} (no {net.MinorStormName} flow entered; the minimum may apply to a smaller storm)."));
                }
            }
            if (!k.AllowSurcharge && p.FlowsFull)
                yield return CalcMessage.Warning("W-NW-005", F($"Pipe \"{n}\" flows full or under pressure (HGL above the crown); the criteria require open-channel flow."));
            if (k.MinPipeRise is { } mr && p.Pipe.Rise < mr - 1e-9)
                yield return CalcMessage.Warning("W-NW-006", F($"Pipe \"{n}\": size {p.Pipe.Rise:G4} {L} is below the minimum {mr:G4} {L}."));
            if (k.FlagSupercritical && p.Regime == "Steep")
                yield return CalcMessage.Warning("W-NW-007", F($"Pipe \"{n}\" is steeper than critical slope (yn {p.NormalDepth:F3} < yc {p.CriticalDepth:F3} {L}): entrance control, possible jump."));
            if (p.JumpStation is { } js)
                yield return CalcMessage.Info("I-NW-004", F($"Pipe \"{n}\": hydraulic jump about {js:F1} {L} from the upstream end."));
            if (p.Slope <= 0)
                yield return CalcMessage.Warning("W-NW-008", F($"Pipe \"{n}\" is flat or adverse (slope {100 * p.Slope:F3} %)."));
        }
    }

    internal static string F(FormattableString s) => s.ToString(Inv);

    private sealed class NonConvergenceException(string message) : Exception(message);

    /// <summary>Geometry and profile computation for one pipe.</summary>
    private sealed class PipeHydraulics
    {
        private readonly NetworkPipe _p;
        private readonly UnitConstants _c;
        private readonly ISectionGeometry _g;
        private readonly double _af, _pf, _q, _s0;
        public double D { get; }
        public double Yc { get; }
        public double Yn { get; } = double.NaN;
        public double Vn { get; } = double.NaN;
        public double Vfull { get; }
        public double Qfull { get; } = double.NaN;

        public PipeHydraulics(NetworkPipe p, UnitConstants c)
        {
            _p = p; _c = c;
            ChannelSection sec = p.Shape == NetworkPipeShape.Circular ? new CircularSection(p.Rise) : new EllipticalSection(p.Rise, p.Span);
            _g = SectionGeometry.From(sec);
            D = _g.MaxDepth;
            _af = _g.Area(D); _pf = _g.WettedPerimeter(D);
            _q = p.Discharge;
            _s0 = (p.UpstreamInvert - p.DownstreamInvert) / p.Length;
            Vfull = _q / _af;
            Yc = _q > 0 ? CriticalDepth() : 0;
            if (_s0 > 0)
            {
                Qfull = _c.ManningK / p.ManningN * _af * Math.Pow(_af / _pf, 2.0 / 3.0) * Math.Sqrt(_s0);
                if (_q > 0) Yn = NormalDepth();
                if (double.IsFinite(Yn)) Vn = _q / _g.Area(Yn);
            }
        }

        private double A(double y) => y >= D ? _af : _g.Area(Math.Max(y, 1e-12 * D));
        private double P(double y) => y >= D ? _pf : _g.WettedPerimeter(Math.Max(y, 1e-12 * D));
        private double E(double y) { double v = _q / A(y); return y + v * v / (2 * _c.Gravity); }
        private double Sf(double y)
        {
            double a = A(y), r = a / P(y);
            double k = _c.ManningK / _p.ManningN * a * Math.Pow(r, 2.0 / 3.0);
            return _q * _q / (k * k);
        }
        /// <summary>
        /// Momentum function M = Q²/(gA) + ∫₀^y A dη (Chow §3-5). Above the crown the pressure head adds
        /// Af·(y − D) and A stays the full area.
        /// </summary>
        private double M(double y)
        {
            double yy = Math.Min(y, D);
            int n = 64; double h = yy / n, sum = 0;
            for (int i = 0; i <= n; i++) { double w = i == 0 || i == n ? 1 : i % 2 == 1 ? 4 : 2; sum += w * _g.Area(i * h); }
            return _q * _q / (_c.Gravity * A(y)) + sum * h / 3 + (y > D ? _af * (y - D) : 0);
        }

        private double CriticalDepth()
        {
            double Fr2(double y) { double a = _g.Area(y), t = _g.TopWidth(y); return _q * _q * t / (_c.Gravity * a * a * a) - 1; }
            var r = RootFinder.Brent(Fr2, 1e-9 * D, D * (1 - 1e-9), 1e-13);
            if (!r.Converged) throw new NonConvergenceException(F($"Pipe \"{_p.Name}\": critical depth did not converge ({r.FailureReason}); no HGL is reported."));
            return r.Root;
        }

        private double NormalDepth()
        {
            double k = _c.ManningK / _p.ManningN * Math.Sqrt(_s0);
            double Qy(double y) { double a = _g.Area(y); return k * a * Math.Pow(a / _g.WettedPerimeter(y), 2.0 / 3.0); }
            var peak = RootFinder.GoldenMaximum(Qy, 0.5 * D, D, 1e-12 * D);
            if (_q > peak.Value) return double.NaN; // above free-surface capacity: pressure flow
            var r = RootFinder.Brent(y => Qy(y) - _q, 1e-9 * D, peak.X, 1e-13);
            if (!r.Converged) throw new NonConvergenceException(F($"Pipe \"{_p.Name}\": normal depth did not converge ({r.FailureReason}); no HGL is reported."));
            return r.Root;
        }

        private double Z(double x) => _p.UpstreamInvert - _s0 * x;

        public PipeHydraulicResult Solve(double wsDown, List<string>? trace)
        {
            double L = _p.Length;
            string regime = !(_s0 > 0) ? "Flat/adverse" : !double.IsFinite(Yn) ? (_q > 0 ? "Pressure" : "Dry") : Yn < Yc ? "Steep" : "Mild";
            if (_q <= 0)
            {
                double w = Math.Max(wsDown, _p.UpstreamInvert);
                return Result(regime, "No flow", [new(0, Z(0), Z(0) + D, w, w), new(L, Z(L), Z(L) + D, Math.Max(wsDown, Z(L)), Math.Max(wsDown, Z(L)))], null, wsDown);
            }
            int n = (int)Math.Clamp(Math.Ceiling(L / (0.25 * D)), 40, 2000);
            double dx = L / n;
            var xs = Enumerable.Range(0, n + 1).Select(i => i * dx).ToArray();

            // subcritical (downstream control) from x = L upstream
            var ySub = new double[n + 1];
            for (int i = 0; i <= n; i++) ySub[i] = double.NaN;
            ySub[n] = Math.Max(wsDown - Z(L), Yc);
            int failAt = -1;
            for (int i = n; i > 0; i--)
            {
                double yi = ySub[i], z = Z(xs[i]), zu = Z(xs[i - 1]), target = z + E(yi), sf = Sf(yi);
                double f(double yy) => zu + E(yy) - target - 0.5 * (sf + Sf(yy)) * dx;
                if (f(Yc) > 0) { failAt = i; break; }
                double hi = Math.Max(yi, D) + dx + 1;
                int guard = 0;
                while (f(hi) < 0 && guard++ < 200) hi = 2 * hi + 1;
                var r = RootFinder.Brent(f, Yc, hi, 1e-12);
                if (!r.Converged) throw new NonConvergenceException(F($"Pipe \"{_p.Name}\": the backwater step at {xs[i - 1]:F2} did not converge; no HGL is reported."));
                ySub[i - 1] = r.Root;
            }

            double[] y;
            double? jump = null;
            string condition;
            if (failAt < 0)
            {
                y = ySub;
                bool full = y.Any(v => v >= D - 1e-9), allFull = y.All(v => v >= D - 1e-9);
                condition = allFull ? "Full (pressure flow)" : full ? "Partly full, pressurised at the outlet end" : "Part full (subcritical)";
            }
            else
            {
                // supercritical from critical depth at the entrance, downstream
                var ySup = new double[n + 1];
                ySup[0] = Yc;
                for (int i = 0; i < n; i++)
                {
                    double yy0 = ySup[i], z = Z(xs[i]), zd = Z(xs[i + 1]), avail = z + E(yy0), sf = Sf(yy0);
                    double f(double yy) => zd + E(yy) + 0.5 * (sf + Sf(yy)) * dx - avail;
                    double lo = 1e-6 * D;
                    if (f(Yc) > 0 || f(lo) < 0) { ySup[i + 1] = Yc; continue; } // cannot stay supercritical: hold at critical
                    var r = RootFinder.Brent(f, lo, Yc, 1e-12);
                    if (!r.Converged) throw new NonConvergenceException(F($"Pipe \"{_p.Name}\": the supercritical step at {xs[i + 1]:F2} did not converge; no HGL is reported."));
                    ySup[i + 1] = r.Root;
                }
                y = (double[])ySup.Clone();
                int j = -1;
                for (int i = failAt; i <= n; i++)
                    if (double.IsFinite(ySub[i]) && M(ySub[i]) >= M(ySup[i])) { j = i; break; }
                if (j >= 0)
                {
                    for (int i = j; i <= n; i++) y[i] = ySub[i];
                    jump = xs[j];
                    condition = "Supercritical from the entrance, hydraulic jump in the pipe";
                }
                else condition = "Supercritical (entrance control)";
            }

            var prof = new List<PipeProfilePoint>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double z = Z(xs[i]);
                prof.Add(new(xs[i], z, z + D, z + y[i], z + E(y[i])));
            }
            trace?.Add(F($"Pipe {_p.Name}: Q = {_q:G6}, D = {D:G6}, S0 = {_s0:G6}, yn = {Yn:F4}, yc = {Yc:F4}, {regime}; downstream control {wsDown:F4}; {condition}; HGL {prof[^1].Hgl:F4} → {prof[0].Hgl:F4}."));
            return Result(regime, condition, prof, jump, wsDown);
        }

        private PipeHydraulicResult Result(string regime, string condition, List<PipeProfilePoint> prof, double? jump, double control)
        {
            var up = prof[0]; var dn = prof[^1];
            double yu = up.Hgl - up.Invert, yd = dn.Hgl - dn.Invert;
            return new PipeHydraulicResult
            {
                Pipe = _p, Slope = _s0, NormalDepth = Yn, CriticalDepth = Yc, FullFlowCapacity = Qfull, FullFlowVelocity = Vfull, NormalVelocity = Vn,
                Regime = regime, Condition = condition,
                DepthUp = yu, DepthDown = yd, VelocityUp = _q > 0 ? _q / A(yu) : 0, VelocityDown = _q > 0 ? _q / A(yd) : 0,
                HglUp = up.Hgl, HglDown = dn.Hgl, EglUp = up.Egl, EglDown = dn.Egl, EnergyLoss = up.Egl - dn.Egl,
                FlowsFull = prof.Any(p => p.Hgl >= p.Crown - 1e-9), JumpStation = jump, DownstreamControl = control, Profile = prof,
            };
        }
    }
}
