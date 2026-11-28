using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Gravity sanitary sewer network design and check by the City of Rapid City Infrastructure Design Criteria Manual (2012)
/// §3.12, which adopts Ten States Standards (§3.3.2): land-use design flows with peaking and reduction factors accumulated
/// down the network, Manning partial-flow depth and velocity at peak and average flow, pipe sizing for d/D ≤ 0.70,
/// invert design for minimum cover, manhole drops and crown matching, manhole spacing and type, and the §3.12.14
/// hydrogen sulfide Z analysis. Blank diameters and inverts are designed; entered ones are checked.
/// </summary>
public sealed class SanitarySewerTool : IDesignTool
{
    public string Id => "sanitary-network";
    public string Name => "Sanitary sewer network design";
    public string Description => "Gravity sanitary sewer design and check (Rapid City IDCM 2012 §3.12, Ten States Standards): land-use average and peak flows (IDCM PF and reduction factors, or Harmon), accumulated through the manholes; Manning depth and velocity at peak and average flow; sizes for d/D ≤ 0.70 and designs inverts for minimum cover where cells are blank; checks slopes, velocities, manhole drops, crowns, spacing and drop manholes; H2S Z analysis (§3.12.14). Pipes and rims are read from the selected network when the tables are left out.";
    public bool UsesNetwork => true;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double GpmPerCfs = 448.831;
    private const double K = 1.486;

    /// <summary>Ten States Standards (2014) §33.41 minimum slopes (ft/100 ft) for n = 0.013, by diameter (in.).</summary>
    public static readonly IReadOnlyDictionary<int, double> TenStatesMinSlopePct = new Dictionary<int, double>
    {
        [8] = 0.40, [10] = 0.28, [12] = 0.22, [14] = 0.17, [15] = 0.15, [16] = 0.14, [18] = 0.12, [21] = 0.10, [24] = 0.08,
        [27] = 0.067, [30] = 0.058, [33] = 0.052, [36] = 0.046, [39] = 0.041, [42] = 0.037,
    };

    /// <summary>Ten States minimum slope (ft/ft) for a diameter in inches: the table value, or the slope giving 2 ft/s flowing full with n = 0.013 for sizes not in the table.</summary>
    public static double TenStatesMinSlope(double dIn)
    {
        int key = (int)Math.Round(dIn);
        if (Math.Abs(dIn - key) < 0.01 && TenStatesMinSlopePct.TryGetValue(key, out double pct)) return pct / 100;
        return SlopeForFullVelocity(dIn / 12, 0.013, 2.0);
    }

    /// <summary>Slope (ft/ft) at which a circular pipe flowing full (or half full) runs at velocity <paramref name="v"/>.</summary>
    public static double SlopeForFullVelocity(double dFt, double n, double v) => Math.Pow(v * n / (K * Math.Pow(dFt / 4, 2.0 / 3.0)), 2);

    // ---- circular partial-flow geometry (D and y in ft) ----
    public static double Theta(double y, double d) => 2 * Math.Acos(Math.Clamp(1 - 2 * y / d, -1, 1));
    public static double Area(double y, double d) { double t = Theta(y, d); return d * d / 8 * (t - Math.Sin(t)); }
    public static double Perimeter(double y, double d) => d * Theta(y, d) / 2;
    public static double TopWidth(double y, double d) => d * Math.Sin(Theta(y, d) / 2);
    public static double Discharge(double y, double d, double n, double s)
    {
        double a = Area(y, d), p = Perimeter(y, d);
        return a <= 0 ? 0 : K / n * a * Math.Pow(a / p, 2.0 / 3.0) * Math.Sqrt(s);
    }
    public static double QFull(double d, double n, double s) => K / n * Math.PI * d * d / 4 * Math.Pow(d / 4, 2.0 / 3.0) * Math.Sqrt(s);

    /// <summary>Normal depth (ft) for <paramref name="q"/> cfs; NaN when Q exceeds the pipe's greatest open-channel capacity (at y = 0.938 D).</summary>
    public static double NormalDepth(double q, double d, double n, double s)
    {
        if (!(q > 0)) return 0;
        double hi = 0.9382 * d;
        if (q > Discharge(hi, d, n, s)) return double.NaN;
        double lo = 0;
        for (int i = 0; i < 100; i++) { double m = 0.5 * (lo + hi); if (Discharge(m, d, n, s) < q) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    /// <summary>Rapid City IDCM §3.12.2 residential reduction factor by population served.</summary>
    public static double ResidentialRf(double pop) => pop <= 500 ? 1.0 : pop <= 1000 ? 0.955 : pop <= 2000 ? 0.905 : pop <= 5000 ? 0.86 : pop <= 10000 ? 0.775 : 0.72;
    /// <summary>Rapid City IDCM §3.12.2 commercial/industrial reduction factor by acres served.</summary>
    public static double CommercialRf(double acres) => acres <= 1000 ? 1.0 : acres <= 2000 ? 0.95 : acres <= 5000 ? 0.85 : 0.71;
    /// <summary>Harmon peaking factor, P in thousands: 1 + 14/(4 + √P) (Ten States Standards Fig. 1 basis).</summary>
    public static double Harmon(double pThousands) => 1 + 14 / (4 + Math.Sqrt(Math.Max(0, pThousands)));

    /// <summary>IDCM §3.12.14 Z as printed: EBOD/(S^½·Q^⅓)·P/B (S ft/ft, Q cfs); Pomeroy (EPA 1974): 3·EBOD/(S^½·Q^⅓)·P/B with S in ft/100 ft.</summary>
    public static double ZValue(double ebod, double s, double qCfs, double p, double b, bool pomeroy)
        => pomeroy ? 3 * ebod / (Math.Sqrt(s * 100) * Math.Cbrt(qCfs)) * p / b : ebod / (Math.Sqrt(s) * Math.Cbrt(qCfs)) * p / b;

    public static readonly double[] H2SFractions = [0.01, 0.05, 0.25, 0.75, 0.95, 1.00, 1.05];

    private enum Use { Residential, MultiFamily, Commercial, Industrial, Public, Flow }

    private sealed record Load(string Manhole, Use Use, double Area, double Population, double QaveGpm, double Pf);

    private sealed class Pipe
    {
        public string Name = "", From = "", To = "";
        public double Length, N;
        public double? D;                 // inches
        public double? Up, Dn;
        public bool DesignD, DesignInv;
        // results
        public double Run, Slope, QaveGpm, QpeakGpm, PopRes, AreaCi, AreaTotal, AreaIncr, QaveRes, QaveCi, QavePub, QaveFlow, QpkFlow, Ii, RfRes = 1, RfCi = 1;
        public double Ebod;
        public bool Surcharged;
    }

    private sealed record Manhole(string Name, double? Rim, double? Deflection, double? N, double? E);

    public string SampleInput(DesignToolContext context)
    {
        return """
            # Sanitary sewer network design. U.S. units: diameters in inches, lengths and elevations in ft, flows in gpm.
            # Blank Diameter and invert cells are designed by MasterFlow; entered values are checked.
            # Leave out [Pipes] and [Manholes] to use the selected network's pipes and rims (e.g. a Civil 3D sanitary network from LandXML).
            criteria = Rapid City          # Rapid City (IDCM 2012 §3.12) or Ten States
            peaking = Rapid City           # Rapid City (PF 4.2 / 1.7 / 1.2 with reduction factors), Harmon, or a fixed number
            gpcd = 100                     # IDCM QP, gallons per capita per day
            persons per household = 2.65   # IDCM PH
            commercial rate = 3            # gpm per acre (industrial uses the same unless "industrial rate" is given)
            public rate = 2                # gpm per acre
            infiltration = 0               # gpd per inch-diameter per mile of sewer (IDCM gives none)
            inflow = 0                     # gpd per acre served
            minimum diameter = 8           # in. (IDCM 3.12.4)
            max d/D = 0.70                 # at peak flow (IDCM 3.12.4)
            min velocity = 2.0             # ft/s at peak flow (IDCM 3.12.6)
            max velocity = 10              # ft/s at half depth (IDCM 3.12.6)
            minimum cover = 5              # ft over top of pipe (collectors 5, interceptors 4; IDCM 3.12.5)
            manhole diameter = 4           # ft inside; deducted from centre-to-centre lengths for the slope (IDCM 3.12.6 item 7)
            pipe length = center to center # or "inside to inside"
            sizes = 8, 10, 12, 15, 18, 21, 24, 27, 30, 36, 42, 48

            [Manholes]
            Name	Rim	Deflection (deg)
            MH-1	3412.50	0
            MH-2	3410.20	0
            MH-3	3407.80	90
            MH-4	3409.60	0
            MH-5	3405.10	0
            MH-6	3402.40	0

            [Loads]
            Manhole	Land use	Area (ac)	DU/ac	Population	Flow (gpm)	PF
            MH-1	Residential	12.0	4.0
            MH-2	Residential	9.5	4.0
            MH-4	Multi-family	4.0	12.0
            MH-4	Commercial	3.5
            MH-5	Public	6.0
            MH-5	Flow				15	1.5

            [Pipes]
            Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert	n
            S-1	MH-1	MH-2	350
            S-2	MH-2	MH-3	380
            S-3	MH-4	MH-3	300
            S-4	MH-3	MH-5	400
            S-5	MH-5	MH-6	320
            """;
    }

    public ToolResult Run(string input, DesignToolContext context) => Design(input, context).Result;

    /// <summary>
    /// Runs the design and also returns the designed network (sizes, inverts, flows, loads), for the tools that build on it
    /// (wet-weather flows, bypass pumping). The design is null when the run has errors.
    /// </summary>
    public static (ToolResult Result, SanitaryFlowDesign? Design) FlowDesign(string input, DesignToolContext context)
    {
        SanitaryFlowDesign? d = null;
        var r = new SanitarySewerTool().RunCore(input, context, out _, out _, x => d = x);
        return (r, d);
    }

    /// <summary>One designed or checked sewer, for tools that build on this one (trunk HGL, build-out phasing). Flows in gpm include I/I; lengths and elevations in ft (Run = the length the slope is measured over, inside wall to inside wall); D in inches.</summary>
    public sealed record DesignedSewer(string Name, string From, string To, double Length, double Run, double DiameterIn, double UpInvert, double DownInvert, double N, double Slope, double QaveGpm, double QpeakGpm, double Population, bool SizeDesigned);
    /// <summary>A manhole of the design: rim (null when not given), deflection (deg, null when not given), coordinates.</summary>
    public sealed record DesignedManhole(string Name, double? Rim, double? Deflection, double? Northing, double? Easting);
    /// <summary>The sewers and manholes after design, upstream first (the hydraulic layout the trunk HGL and build-out tools route).</summary>
    public sealed record SewerLayout(IReadOnlyList<DesignedSewer> Sewers, IReadOnlyList<DesignedManhole> Manholes);

    /// <summary>Runs the tool and also returns the designed sewers as a layout (null when the run failed).</summary>
    public ToolResult Run(string input, DesignToolContext context, out SewerLayout? layout)
    {
        var r = RunCore(input, context, out _, out var l);
        layout = r.Succeeded ? l : null;
        return r;
    }

    /// <summary>
    /// Runs the design and also returns the designed network as data (sizes, inverts, peak water surface, d/D, cover,
    /// flags), for the tools that build on it: the sanitary plan and profile sheets and the deep-sewer structural check.
    /// The design is null when the run failed.
    /// </summary>
    public (ToolResult Result, SanitaryDesign? Design) Design(string input, DesignToolContext context)
    {
        var r = RunCore(input, context, out var d, out _);
        return (r, r.Succeeded ? d : null);
    }

    private ToolResult RunCore(string input, DesignToolContext context, out SanitaryDesign? design, out SewerLayout? layout, Action<SanitaryFlowDesign>? capture = null)
    {
        layout = null;
        design = null;
        const string title = "Sanitary sewer network design";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-SS-001", "The sanitary sewer tool works in U.S. units (gpm, in., ft) as the Rapid City IDCM and Ten States Standards are written; switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        string criteria = (p.Text("criteria") ?? "Rapid City").Trim().ToLowerInvariant();
        bool rc = criteria.StartsWith("rapid");
        if (!rc && !criteria.StartsWith("ten")) msgs.Add(CalcMessage.Error("E-SS-009", $"criteria \"{p.Text("criteria")}\" is not Rapid City or Ten States."));
        string peakText = (p.Text("peaking") ?? (rc ? "Rapid City" : "Harmon")).Trim();
        double? fixedPf = ToolInput.TryNumber(peakText, out double fpf) ? fpf : null;
        bool rcPeaking = peakText.StartsWith("rapid", StringComparison.OrdinalIgnoreCase);
        bool harmon = peakText.StartsWith("harmon", StringComparison.OrdinalIgnoreCase) || peakText.StartsWith("ten", StringComparison.OrdinalIgnoreCase);
        if (fixedPf is null && !rcPeaking && !harmon) msgs.Add(CalcMessage.Error("E-SS-009", $"peaking \"{peakText}\" is not Rapid City, Harmon or a number."));
        if (fixedPf is <= 0) msgs.Add(CalcMessage.Error("E-SS-009", "A fixed peaking factor must be above zero."));
        bool rfOn = p.Flag("reduction factors", true);

        double gpcd = p.Number("gpcd", 100), pph = p.Number("persons per household", 2.65);
        double comRate = p.Number("commercial rate", 3), indRate = p.Number("industrial rate", comRate), pubRate = p.Number("public rate", 2);
        double resPf = p.Number("residential PF", 4.2), comPf = p.Number("commercial PF", 1.7), pubPf = p.Number("public PF", 1.2);
        double infil = p.Number("infiltration", 0), inflowRate = p.Number("inflow", 0);
        double minD = p.Number("minimum diameter", 8), maxDd = p.Number("max d/D", 0.70);
        double vMin = p.Number("min velocity", 2.0), vMax = p.Number("max velocity", 10);
        double cover = p.Number("minimum cover", 5), mhDia = p.Number("manhole diameter", 4), wallIn = p.Number("wall thickness", 0);
        double nDefault = p.Number("n", 0.013), dropMhHeight = p.Number("drop manhole height", 2.0);
        double spacingSmall = p.Number("max spacing small", 400), spacingLarge = p.Number("max spacing large", 500);
        bool c2c = !(p.Text("pipe length") ?? "center to center").Trim().StartsWith("inside", StringComparison.OrdinalIgnoreCase);
        string vBasis = (p.Text("min velocity basis") ?? (rc ? "peak" : "full")).Trim().ToLowerInvariant();
        bool vAtPeak = !vBasis.StartsWith("full");
        bool pomeroy = (p.Text("h2s formula") ?? "IDCM").Trim().StartsWith("pom", StringComparison.OrdinalIgnoreCase);
        bool h2sOn = p.Flag("h2s", true);
        double ebodRes = p.Number("EBOD residential", 300), ebodCom = p.Number("EBOD commercial", 500);
        string redesign = (p.Text("redesign") ?? "no").Trim().ToLowerInvariant();

        var sizes = new List<double>();
        foreach (var t in (p.Text("sizes") ?? "8,10,12,15,18,21,24,27,30,36,42,48").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ToolInput.TryNumber(t, out double v) && v > 0) sizes.Add(v);
            else msgs.Add(CalcMessage.Error("E-SS-009", $"Pipe size \"{t}\" is not a positive number."));
        sizes.Sort();
        foreach (var (val, name) in new[] { (gpcd, "gpcd"), (pph, "persons per household"), (maxDd, "max d/D"), (mhDia + 1e-12, "manhole diameter"), (nDefault, "n"), (minD, "minimum diameter") })
            if (!(val > 0)) msgs.Add(CalcMessage.Error("E-SS-009", $"{name} must be above zero."));
        if (maxDd > 0.938) msgs.Add(CalcMessage.Error("E-SS-009", "max d/D cannot exceed 0.938 (a circular pipe's greatest open-channel capacity)."));
        if (!(cover >= 0) || !(infil >= 0) || !(inflowRate >= 0) || !(wallIn >= 0)) msgs.Add(CalcMessage.Error("E-SS-009", "minimum cover, wall thickness, infiltration and inflow cannot be negative."));

        // ---- manholes and pipes, from the tables or the selected network ----
        var net = context.Network;
        bool fromNetwork = !p.HasSection("Pipes") && net is { Pipes.Count: > 0 };
        var mhs = new Dictionary<string, Manhole>(StringComparer.OrdinalIgnoreCase);
        if (net is not null)
            foreach (var s in net.Structures) mhs[s.Name] = new Manhole(s.Name, s.RimElevation, null, s.Northing, s.Easting);
        foreach (var r in p.Rows("Manholes"))
        {
            if (r.Length == 0 || r[0].Length == 0) { msgs.Add(CalcMessage.Error("E-SS-003", "Every row under [Manholes] needs a name.")); continue; }
            mhs.TryGetValue(r[0], out var old);
            mhs[r[0]] = new Manhole(r[0], p.Cell(r, 1, "rim", r[0]) ?? old?.Rim, p.Cell(r, 2, "deflection", r[0]), old?.N, old?.E);
        }

        var pipes = new List<Pipe>();
        if (fromNetwork)
        {
            foreach (var np in net!.Pipes)
            {
                bool rD = redesign.Contains("size"), rI = redesign.Contains("invert");
                pipes.Add(new Pipe
                {
                    Name = np.Name, From = np.From, To = np.To, Length = np.Length, N = np.ManningN > 0 ? np.ManningN : nDefault,
                    D = rD ? null : np.Rise * 12, Up = rI ? null : np.UpstreamInvert, Dn = rI ? null : np.DownstreamInvert, DesignD = rD, DesignInv = rI,
                });
                if (np.Shape != NetworkPipeShape.Circular) msgs.Add(CalcMessage.Warning("W-SS-017", $"{np.Name}: elliptical pipe; treated as circular with D = rise."));
            }
            msgs.Add(CalcMessage.Info("I-SS-006", $"Pipes, lengths, inverts and rims read from the selected network \"{net.Name}\"" + (redesign is "no" ? "; set redesign = sizes, or sizes and inverts, to have MasterFlow design them." : ".")));
        }
        else
        {
            foreach (var r in p.Rows("Pipes"))
            {
                string label = r.Length > 0 && r[0].Length > 0 ? r[0] : "pipe";
                if (r.Length < 4 || r[0].Length == 0 || r[1].Length == 0 || r[2].Length == 0) { msgs.Add(CalcMessage.Error("E-SS-003", $"{label}: each pipe needs Name, From, To and Length.")); continue; }
                double? len = p.Cell(r, 3, "length", label), d = p.Cell(r, 4, "diameter", label), up = p.Cell(r, 5, "up invert", label), dn = p.Cell(r, 6, "down invert", label), n = p.Cell(r, 7, "n", label);
                if (len is not > 0) { msgs.Add(CalcMessage.Error("E-SS-003", $"{label}: length must be above zero.")); continue; }
                if (up.HasValue != dn.HasValue) { msgs.Add(CalcMessage.Error("E-SS-004", $"{label}: give both inverts, or leave both blank to have them designed.")); continue; }
                if (d is <= 0 || n is <= 0) { msgs.Add(CalcMessage.Error("E-SS-003", $"{label}: diameter and n must be above zero.")); continue; }
                pipes.Add(new Pipe { Name = r[0], From = r[1], To = r[2], Length = len.Value, D = d, Up = up, Dn = dn, N = n ?? nDefault, DesignD = d is null, DesignInv = up is null });
            }
        }
        if (pipes.Count == 0 && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-SS-002", "Give the sewers under [Pipes] (Name, From, To, Length, Diameter, Up invert, Down invert, n), or select a network."));
        foreach (var g in pipes.GroupBy(x => x.From, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            msgs.Add(CalcMessage.Error("E-SS-005", $"Manhole {g.Key} has {g.Count()} outlet pipes ({string.Join(", ", g.Select(x => x.Name))}); a gravity sanitary network needs one outlet per manhole."));
        foreach (var g in pipes.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            msgs.Add(CalcMessage.Error("E-SS-003", $"Pipe name {g.Key} is used {g.Count()} times."));
        foreach (var x in pipes.Where(x => x.From.Equals(x.To, StringComparison.OrdinalIgnoreCase)))
            msgs.Add(CalcMessage.Error("E-SS-003", $"{x.Name}: From and To are the same manhole."));
        foreach (var x in pipes)
            foreach (var mh in new[] { x.From, x.To }) if (!mhs.ContainsKey(mh)) mhs[mh] = new Manhole(mh, null, null, null, null);

        // ---- loads ----
        var loads = new List<Load>();
        // Columns are found by their header words, so unused ones can be left out; otherwise the sample's order.
        var lh = p.Header("Loads");
        int LCol(string word, int fallback)
        {
            for (int i = 2; i < lh.Count; i++) if (lh[i].Contains(word, StringComparison.OrdinalIgnoreCase)) return i;
            return lh.Count > 2 && lh.Skip(2).Any(h => new[] { "area", "du", "pop", "flow", "pf" }.Any(w => h.Contains(w, StringComparison.OrdinalIgnoreCase))) ? -1 : fallback;
        }
        int cA = LCol("area", 2), cDu = LCol("du", 3), cPop = LCol("pop", 4), cQ = LCol("flow", 5), cPf = LCol("pf", 6);
        foreach (var r in p.Rows("Loads"))
        {
            string label = r.Length > 0 ? r[0] : "load";
            if (r.Length < 2 || r[0].Length == 0) { msgs.Add(CalcMessage.Error("E-SS-007", "Every row under [Loads] needs a manhole and a land use.")); continue; }
            if (!mhs.ContainsKey(r[0])) { msgs.Add(CalcMessage.Error("E-SS-007", $"Load at {r[0]}: no such manhole.")); continue; }
            string u = r[1].Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "");
            Use? use = u switch
            {
                "residential" or "res" or "singlefamily" => Use.Residential,
                "multifamily" or "mf" => Use.MultiFamily,
                "commercial" or "com" => Use.Commercial,
                "industrial" or "ind" => Use.Industrial,
                "public" or "pub" or "institutional" => Use.Public,
                "flow" or "direct" or "point" => Use.Flow,
                _ => null,
            };
            double? area = C(cA, "area"), du = C(cDu, "DU/ac"), pop = C(cPop, "population"), q = C(cQ, "flow"), pf = C(cPf, "PF");
            double? C(int i, string what) => i < 0 ? null : p.Cell(r, i, what, label);
            if (use is null) { msgs.Add(CalcMessage.Error("E-SS-007", $"Load at {label}: land use \"{r[1]}\" is not Residential, Multi-family, Commercial, Industrial, Public or Flow.")); continue; }
            if (area is < 0 || du is < 0 || pop is < 0 || q is < 0 || pf is <= 0) { msgs.Add(CalcMessage.Error("E-SS-007", $"Load at {label}: values cannot be negative (PF above zero).")); continue; }
            switch (use)
            {
                case Use.Residential or Use.MultiFamily:
                    if (pop is null && (area is null || du is null)) { msgs.Add(CalcMessage.Error("E-SS-007", $"Load at {label}: residential loads need Area and DU/ac, or Population.")); continue; }
                    double people = pop ?? pph * du!.Value * area!.Value;
                    loads.Add(new Load(r[0], use.Value, area ?? 0, people, gpcd * people / 1440, pf ?? resPf));
                    break;
                case Use.Commercial or Use.Industrial or Use.Public:
                    if (area is null && q is null) { msgs.Add(CalcMessage.Error("E-SS-007", $"Load at {label}: give the Area (or an average Flow).")); continue; }
                    double rate = use == Use.Public ? pubRate : use == Use.Industrial ? indRate : comRate;
                    loads.Add(new Load(r[0], use.Value, area ?? 0, 0, q ?? rate * area!.Value, pf ?? (use == Use.Public ? pubPf : comPf)));
                    break;
                default:
                    if (q is null) { msgs.Add(CalcMessage.Error("E-SS-007", $"Load at {label}: a Flow load needs its average Flow (gpm).")); continue; }
                    loads.Add(new Load(r[0], Use.Flow, area ?? 0, pop ?? 0, q.Value, pf ?? 1.0));
                    break;
            }
        }

        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // ---- topology ----
        var byName = pipes.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var netPipes = pipes.Select(x => new NetworkPipe { Name = x.Name, From = x.From, To = x.To }).ToList();
        var order = NetworkSizer.UpstreamFirst(netPipes);
        // A loop shows up as a pipe reachable from itself.
        foreach (var x in pipes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string at = x.To;
            while (pipes.FirstOrDefault(y => y.From.Equals(at, StringComparison.OrdinalIgnoreCase)) is { } nx)
            {
                if (!seen.Add(nx.Name)) { msgs.Add(CalcMessage.Error("E-SS-006", $"The pipes downstream of {x.Name} form a loop ({string.Join(" → ", seen)}).")); return new(title, [], [], msgs); }
                at = nx.To;
            }
        }
        List<Pipe> Incoming(string mh) => pipes.Where(y => y.To.Equals(mh, StringComparison.OrdinalIgnoreCase)).ToList();
        // Manholes upstream of (and including) each pipe's From manhole.
        HashSet<string> UpstreamManholes(Pipe x)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { x.From };
            var stack = new Stack<string>([x.From]);
            while (stack.Count > 0) foreach (var u in Incoming(stack.Pop())) if (set.Add(u.From)) stack.Push(u.From);
            return set;
        }
        HashSet<string> UpstreamPipes(Pipe x)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { x.Name };
            var stack = new Stack<string>([x.From]);
            while (stack.Count > 0) foreach (var u in Incoming(stack.Pop())) if (set.Add(u.Name)) stack.Push(u.From);
            return set;
        }
        double Run(Pipe x) => c2c ? x.Length - mhDia : x.Length;

        // ---- flows (all except I/I, which depends on the sizes) ----
        foreach (var x in pipes)
        {
            var up = UpstreamManholes(x);
            var ls = loads.Where(l => up.Contains(l.Manhole)).ToList();
            x.PopRes = ls.Where(l => l.Use is Use.Residential or Use.MultiFamily).Sum(l => l.Population);
            x.AreaCi = ls.Where(l => l.Use is Use.Commercial or Use.Industrial).Sum(l => l.Area);
            x.AreaTotal = ls.Sum(l => l.Area);
            x.AreaIncr = loads.Where(l => l.Manhole.Equals(x.From, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Area);
            x.QaveRes = ls.Where(l => l.Use is Use.Residential or Use.MultiFamily).Sum(l => l.QaveGpm);
            x.QaveCi = ls.Where(l => l.Use is Use.Commercial or Use.Industrial).Sum(l => l.QaveGpm);
            x.QavePub = ls.Where(l => l.Use == Use.Public).Sum(l => l.QaveGpm);
            x.QaveFlow = ls.Where(l => l.Use == Use.Flow).Sum(l => l.QaveGpm);
            x.QpkFlow = ls.Where(l => l.Use == Use.Flow).Sum(l => l.QaveGpm * l.Pf);
            double sanitaryAve = x.QaveRes + x.QaveCi + x.QavePub + x.QaveFlow;
            double peak;
            if (rcPeaking)
            {
                x.RfRes = rfOn ? ResidentialRf(x.PopRes) : 1; x.RfCi = rfOn ? CommercialRf(x.AreaCi) : 1;
                // Each load keeps its own PF (column, or the land-use default); the RF applies to the accumulated land use.
                double res = ls.Where(l => l.Use is Use.Residential or Use.MultiFamily).Sum(l => l.QaveGpm * l.Pf) * x.RfRes;
                double ci = ls.Where(l => l.Use is Use.Commercial or Use.Industrial).Sum(l => l.QaveGpm * l.Pf) * x.RfCi;
                double pub = ls.Where(l => l.Use == Use.Public).Sum(l => l.QaveGpm * l.Pf);
                peak = res + ci + pub + x.QpkFlow;
            }
            else
            {
                double pf = fixedPf ?? Harmon(sanitaryAve * 1440 / gpcd / 1000);
                peak = (x.QaveRes + x.QaveCi + x.QavePub) * pf + x.QpkFlow;
            }
            x.QaveGpm = sanitaryAve; x.QpeakGpm = peak;
            x.Ebod = sanitaryAve > 0 ? (ebodRes * (x.QaveRes + x.QavePub + x.QaveFlow) + ebodCom * x.QaveCi) / sanitaryAve : ebodRes;
        }

        // I/I for a pipe at diameter d (in.), given the sizes upstream.
        double InfilGpm(Pipe x, double dIn) =>
            infil * UpstreamPipes(x).Sum(nm => (nm.Equals(x.Name, StringComparison.OrdinalIgnoreCase) ? dIn : byName[nm].D ?? dIn) * byName[nm].Length / 5280) / 1440
            + inflowRate * x.AreaTotal / 1440;

        // ---- design / check, upstream first ----
        var designed = new List<IReadOnlyList<object?>>();
        foreach (var name in order)
        {
            var x = byName[name];
            x.Run = Run(x);
            if (!(x.Run > 0)) { msgs.Add(CalcMessage.Error("E-SS-011", $"{x.Name}: length {x.Length:F1} ft less the manhole diameter leaves no run for the slope; enter inside-to-inside lengths (pipe length = inside to inside).")); return new(title, [], [], msgs); }
            var ins = Incoming(x.From);
            double maxUpD = ins.Count == 0 ? 0 : ins.Max(y => y.D ?? 0);
            var mFrom = mhs[x.From]; var mTo = mhs[x.To];

            // Upstream invert limit from the manhole drop and crown-match rules (for a designed invert).
            double DesignUp(double dIn, double s)
            {
                double lim = mFrom.Rim is { } rim ? rim - cover - (dIn + 2 * wallIn) / 12 : double.PositiveInfinity;
                foreach (var y in ins)
                {
                    if (y.Dn is not { } yDn) continue;
                    lim = Math.Min(lim, yDn - RequiredDrop(Deflection(mFrom, y, x), y.Slope, s));
                    lim = Math.Min(lim, yDn + (y.D ?? dIn) / 12 - dIn / 12);
                }
                return lim;
            }

            if (x.DesignInv && (mFrom.Rim is null && ins.Count == 0 || mTo.Rim is null))
            {
                msgs.Add(CalcMessage.Error("E-SS-008", $"{x.Name}: give the rim elevations of {x.From} and {x.To} under [Manholes] so the inverts can be designed (or enter the inverts)."));
                return new(title, [], [], msgs);
            }

            var candidates = x.DesignD ? sizes.Where(s => s >= minD - 1e-9 && s >= maxUpD - 1e-9).ToList() : [x.D!.Value];
            if (candidates.Count == 0) candidates = [sizes.Count > 0 ? sizes[^1] : Math.Max(minD, maxUpD)];
            bool found = false;
            foreach (var dIn in candidates)
            {
                double dFt = dIn / 12;
                double ii = InfilGpm(x, dIn);
                double qPk = (x.QpeakGpm + ii) / GpmPerCfs;
                double s, up, dn;
                if (x.DesignInv)
                {
                    double sMin = TenStatesMinSlope(dIn);
                    // The IDCM 2 ft/s at peak flow is checked, not designed for (Kyle, 2026-11-26): a short pipe is flagged with the
                    // slope it would need and a design exception is suggested, instead of steepening the line.
                    if (!vAtPeak) sMin = Math.Max(sMin, SlopeForFullVelocity(dFt, x.N, vMin));
                    double sMaxV = SlopeForFullVelocity(dFt, x.N, vMax);
                    up = DesignUp(dIn, sMin);
                    double dnMax = mTo.Rim!.Value - cover - (dIn + 2 * wallIn) / 12;
                    s = Math.Max(sMin, (up - dnMax) / x.Run);
                    if (s > sMaxV && sMin <= sMaxV) s = sMaxV;
                    up = DesignUp(dIn, s);            // drop rule uses the slope out
                    if (double.IsPositiveInfinity(up)) { msgs.Add(CalcMessage.Error("E-SS-008", $"{x.Name}: give the rim of {x.From} so the upstream invert can be set.")); return new(title, [], [], msgs); }
                    s = Math.Max(s, Math.Max(sMin, (up - dnMax) / x.Run));
                    if (s > sMaxV && sMin <= sMaxV) s = sMaxV;
                    dn = up - s * x.Run;
                }
                else { up = x.Up!.Value; dn = x.Dn!.Value; s = (up - dn) / x.Run; }
                if (!(s > 0)) { msgs.Add(CalcMessage.Error("E-SS-010", $"{x.Name}: the inverts ({up:F2} to {dn:F2}) do not fall downstream.")); return new(title, [], [], msgs); }
                double y = NormalDepth(qPk, dFt, x.N, s);
                bool ok = double.IsFinite(y) && y / dFt <= maxDd + 1e-9;
                if (ok || dIn == candidates[^1])
                {
                    x.D = dIn; x.Slope = s; x.Up = up; x.Dn = dn; x.Ii = ii; found = ok;
                    break;
                }
            }
            if (x.DesignD && !found) msgs.Add(CalcMessage.Warning("W-SS-016", $"{x.Name}: no listed size up to {x.D:G3} in. carries the peak flow at d/D ≤ {maxDd:G2}; add larger sizes or steepen the sewer."));
            if (x.DesignD || x.DesignInv)
                designed.Add([x.Name, x.DesignD ? x.D : null, x.DesignInv ? R(x.Up!.Value, 2) : null, x.DesignInv ? R(x.Dn!.Value, 2) : null, R(x.Slope * 100, 3)]);
        }

        // ---- hydraulics and checks ----
        var formRows = new List<IReadOnlyList<object?>>();
        var flowRows = new List<IReadOnlyList<object?>>();
        var checkRows = new List<IReadOnlyList<object?>>();
        var exceptions = new List<IReadOnlyList<object?>>();
        var pipeResults = new List<SanitaryPipeResult>();
        var mhResults = new List<SanitaryManholeResult>();
        double flattest = pipes.Min(x => x.Slope);
        foreach (var name in order)
        {
            var x = byName[name];
            double dIn = x.D!.Value, dFt = dIn / 12, s = x.Slope;
            double qPkGpm = x.QpeakGpm + x.Ii, qAvGpm = x.QaveGpm + x.Ii;
            double qPk = qPkGpm / GpmPerCfs, qAv = qAvGpm / GpmPerCfs;
            double qf = QFull(dFt, x.N, s), vFull = qf / (Math.PI * dFt * dFt / 4);
            double yPk = NormalDepth(qPk, dFt, x.N, s), yAv = NormalDepth(qAv, dFt, x.N, s);
            x.Surcharged = !double.IsFinite(yPk);
            double vPk = qPk <= 0 ? 0 : x.Surcharged ? qPk / (Math.PI * dFt * dFt / 4) : qPk / Area(yPk, dFt);
            double vAv = qAv <= 0 || !double.IsFinite(yAv) ? 0 : qAv / Area(yAv, dFt);
            double sTs = TenStatesMinSlope(dIn);
            double sMax = SlopeForFullVelocity(dFt, x.N, vMax);
            var mFrom = mhs[x.From]; var mTo = mhs[x.To];
            double? covUp = mFrom.Rim is { } r1 ? r1 - x.Up!.Value - (dIn + 2 * wallIn) / 12 : null;
            double? covDn = mTo.Rim is { } r2 ? r2 - x.Dn!.Value - (dIn + 2 * wallIn) / 12 : null;
            double maxSpacing = dIn <= 15 ? spacingSmall : spacingLarge;
            var issues = new List<string>();

            if (x.Surcharged) { issues.Add("surcharged"); msgs.Add(CalcMessage.Warning("W-SS-014", $"{x.Name}: the peak flow {qPkGpm:F0} gpm exceeds the pipe's open-channel capacity ({Discharge(0.9382 * dFt, dFt, x.N, s) * GpmPerCfs:F0} gpm); it surcharges.")); }
            else if (yPk / dFt > maxDd + 1e-9) { issues.Add("d/D"); msgs.Add(CalcMessage.Warning("W-SS-001", $"{x.Name}: d/D = {yPk / dFt:F2} at peak flow exceeds {maxDd:G2} (IDCM 3.12.4).")); }
            if (vAtPeak)
            {
                if (vPk < vMin - 1e-9 && qPk > 0)
                {
                    issues.Add("V peak");
                    double sNeed = SlopeForVelocityAtQ(qPk, dFt, x.N, vMin);
                    exceptions.Add([x.Name, dIn, R(s * 100, 3), R(vPk, 2), R(sNeed * 100, 3), R(vAv, 2), vAv >= 1.8 ? "1.8 ft/s at average flow met (item 2)" : "below 1.8 ft/s at average flow too"]);
                    msgs.Add(CalcMessage.Warning("W-SS-002", $"{x.Name}: {vPk:F2} ft/s at peak flow is below {vMin:G3} ft/s (IDCM 3.12.6 item 1); it needs {sNeed * 100:F2} % instead of {s * 100:F2} %. The slope is left as designed: request a design exception from the Public Works Director" + (vAv >= 1.8 ? $" (the average-flow velocity {vAv:F2} ft/s meets the 1.8 ft/s deviation of item 2)." : $" (the average-flow velocity {vAv:F2} ft/s is also below the 1.8 ft/s deviation of item 2).")));
                }
            }
            else if (vFull < vMin - 1e-9) { issues.Add("V full"); msgs.Add(CalcMessage.Warning("W-SS-002", $"{x.Name}: {vFull:F2} ft/s flowing full is below {vMin:G3} ft/s (Ten States §33.41).")); }
            if (s < sTs - 1e-9) { issues.Add("min slope"); msgs.Add(CalcMessage.Warning("W-SS-003", $"{x.Name}: slope {s * 100:F3} % is below the Ten States minimum {sTs * 100:F3} % for {dIn:G3} in.")); }
            if (vFull > vMax + 1e-9) { issues.Add("V max"); msgs.Add(CalcMessage.Warning("W-SS-004", $"{x.Name}: {vFull:F2} ft/s at half depth exceeds {vMax:G3} ft/s (IDCM 3.12.6 item 3: written approval, vortex energy-dissipating manholes and anchors).")); }
            if (dIn < minD - 1e-9) { issues.Add("min size"); msgs.Add(CalcMessage.Warning("W-SS-005", $"{x.Name}: {dIn:G3} in. is below the {minD:G3} in. minimum (IDCM 3.12.4).")); }
            foreach (var y in Incoming(x.From).Where(y => y.D > dIn + 1e-9))
            { issues.Add("larger upstream"); msgs.Add(CalcMessage.Warning("W-SS-006", $"{y.Name} ({y.D:G3} in.) flows into the smaller {x.Name} ({dIn:G3} in.); IDCM 3.12.4 item 3 needs a written exception.")); }
            if (x.N < 0.013 - 1e-9) { issues.Add("n"); msgs.Add(CalcMessage.Warning("W-SS-007", $"{x.Name}: n = {x.N:G3}; values below 0.013 are not permitted (IDCM 3.12.6 item 4).")); }
            if (x.Length > maxSpacing + 1e-9) { issues.Add("spacing"); msgs.Add(CalcMessage.Warning("W-SS-008", $"{x.Name}: manhole spacing {x.Length:F0} ft exceeds {maxSpacing:F0} ft for {dIn:G3} in. (IDCM 3.12.9 item 2" + (dIn <= 15 ? "; up to 450 ft with justification and approval)." : ")."))); }
            if (covUp < cover - 0.005 || covDn < cover - 0.005) { issues.Add("cover"); msgs.Add(CalcMessage.Warning("W-SS-009", $"{x.Name}: cover over the pipe is {Math.Min(covUp ?? 99, covDn ?? 99):F2} ft, less than {cover:G3} ft (IDCM 3.12.5); use insulation or berms, or lower the sewer.")); }
            if (s > 0.20) { issues.Add("anchors"); msgs.Add(CalcMessage.Warning("W-SS-013", $"{x.Name}: slope {s * 100:F1} % is over 20 %; anchor the sewer (IDCM 3.12.6 item 3, Ten States §33.45).")); }

            pipeResults.Add(new SanitaryPipeResult(x.Name, x.From, x.To, x.Length, dIn, x.N, x.Up!.Value, x.Dn!.Value, s, qPkGpm, qAvGpm,
                x.Surcharged ? double.NaN : yPk, vPk, vAv, x.Surcharged, covUp, covDn, issues.ToList(), issues.Contains("V peak"),
                issues.Contains("V peak") ? SlopeForVelocityAtQ(qPk, dFt, x.N, vMin) : null));
            formRows.Add([x.Name, $"{x.From} – {x.To}", x.From, DropAt(x.From, x), x.To, x.Length, x.N, R(x.AreaIncr, 2), R(x.AreaTotal, 2),
                R(qPkGpm, 1), R(qAvGpm, 1), R(s * 100, 3), dIn, Dep(yPk, dFt), R(vPk, 2), x.Surcharged ? double.NaN : R(yPk / dFt, 3), Dep(yAv, dFt), R(vAv, 2)]);
            double pfEff = x.QaveGpm > 0 ? (x.QpeakGpm) / x.QaveGpm : double.NaN;
            flowRows.Add([x.Name, R(x.PopRes, 0), R(x.QaveRes, 2), rcPeaking ? x.RfRes : null, R(x.AreaCi, 2), R(x.QaveCi, 2), rcPeaking ? x.RfCi : null, R(x.QavePub, 2), R(x.QaveFlow, 2),
                R(x.Ii, 2), R(qAvGpm, 2), R(pfEff, 3), R(qPkGpm, 2), R(qPk, 4)]);
            checkRows.Add([x.Name, dIn, R(s * 100, 3), R(sTs * 100, 3), R(sMax * 100, 3), R(qf * GpmPerCfs, 0), R(vFull, 2), R(vPk, 2), x.Surcharged ? double.NaN : R(yPk / dFt, 3),
                covUp is { } cu ? R(cu, 2) : null, covDn is { } cd ? R(cd, 2) : null, issues.Count == 0 ? "OK" : string.Join(", ", issues)]);
        }

        // ---- manholes ----
        var mhRows = new List<IReadOnlyList<object?>>();
        foreach (var m in mhs.Values.Where(m => pipes.Any(x => x.From.Equals(m.Name, StringComparison.OrdinalIgnoreCase) || x.To.Equals(m.Name, StringComparison.OrdinalIgnoreCase))))
        {
            var outP = pipes.FirstOrDefault(x => x.From.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
            var ins = Incoming(m.Name);
            double low = Math.Min(outP?.Up ?? double.PositiveInfinity, ins.Count > 0 ? ins.Min(y => y.Dn!.Value) : double.PositiveInfinity);
            double? depth = m.Rim is { } rim && double.IsFinite(low) ? rim - low : null;
            double maxD = Math.Max(outP?.D ?? 0, ins.Count > 0 ? ins.Max(y => y.D ?? 0) : 0);
            string type = maxD >= 24 ? "precast tee, 48 in. riser" : "48 in. round (Rinker)";
            string top = depth is null ? "" : depth >= 5.5 ? "eccentric cone" : "flat top (H-20)";
            string drops = "", req = "", note = outP is null ? "outlet" : ins.Count == 0 ? "upstream end" : "";
            var mhFlags = new List<string>();
            if (outP is not null && ins.Count > 0)
            {
                var dl = new List<string>(); var rl = new List<string>();
                foreach (var y in ins)
                {
                    double drop = y.Dn!.Value - outP.Up!.Value;
                    double defl = Deflection(m, y, outP);
                    double need = RequiredDrop(defl, y.Slope, outP.Slope);
                    dl.Add($"{y.Name} {drop:F2}"); rl.Add($"{need:F2}");
                    if (drop < need - 0.005) mhFlags.Add($"drop {y.Name} {drop:F2} < {need:F2}");
                    if (drop < need - 0.005) msgs.Add(CalcMessage.Warning("W-SS-010", $"{m.Name}: the drop from {y.Name} to {outP.Name} is {drop:F2} ft, less than the {need:F2} ft required (IDCM 3.12.9 item 5: average of the slopes across the {mhDia:G3} ft manhole, at least {(defl > 45 ? "0.15 ft for a deflection over 45°" : "0.10 ft")})."));
                    if (drop >= dropMhHeight - 1e-9)
                    {
                        note = "drop manhole";
                        mhFlags.Add("drop manhole");
                        msgs.Add(CalcMessage.Warning("W-SS-012", $"{m.Name}: {y.Name} enters {drop:F2} ft above the outlet invert; a drop connection is needed at {dropMhHeight:G3} ft or more (Ten States §34.2). Rapid City allows drop manholes only with the Public Works Director's written permission and never internal drops (IDCM 3.12.10)."));
                    }
                }
                double lowCrown = ins.Min(y => y.Dn!.Value + y.D!.Value / 12);
                if (outP.Up!.Value + outP.D!.Value / 12 > lowCrown + 0.005) mhFlags.Add("crown above inlet crown");
                if (outP.Up!.Value + outP.D!.Value / 12 > lowCrown + 0.005)
                    msgs.Add(CalcMessage.Warning("W-SS-011", $"{m.Name}: the crown of {outP.Name} ({outP.Up.Value + outP.D.Value / 12:F2}) is above the lowest inlet crown ({lowCrown:F2}); match crowns at least (IDCM 3.12.6 item 6)."));
                drops = string.Join("; ", dl); req = string.Join("; ", rl);
            }
            mhResults.Add(new SanitaryManholeResult(m.Name, m.Rim, m.N, m.E, double.IsFinite(low) ? low : null, depth, type, top, note, mhFlags));
            mhRows.Add([m.Name, m.Rim, double.IsFinite(low) ? R(low, 2) : null, depth is { } dp ? R(dp, 2) : null, type, top, drops, req, note]);
        }

        // ---- H2S (IDCM 3.12.14) ----
        var h2sRows = new List<IReadOnlyList<object?>>();
        if (h2sOn)
        {
            foreach (var name in order)
            {
                var x = byName[name];
                if (x.D > 24) continue;
                double dFt = x.D!.Value / 12, qPk = (x.QpeakGpm + x.Ii) / GpmPerCfs;
                if (!(qPk > 0)) continue;
                var row = new List<object?> { x.Name, x.D, R(x.Ebod, 0) };
                double zMax = 0;
                foreach (var f in H2SFractions)
                {
                    double q = f * qPk, y = NormalDepth(q, dFt, x.N, flattest);
                    if (!double.IsFinite(y)) { row.Add(double.NaN); continue; }
                    double z = ZValue(x.Ebod, flattest, q, Perimeter(y, dFt), TopWidth(y, dFt), pomeroy);
                    zMax = Math.Max(zMax, z);
                    row.Add(R(z, 0));
                }
                string cond = zMax < 5000 ? "rare" : zMax <= 10000 ? "marginal" : "common";
                row.Add(R(zMax, 0)); row.Add(cond);
                h2sRows.Add(row);
                if (cond != "rare") msgs.Add(CalcMessage.Warning("W-SS-015", $"{x.Name}: Z = {zMax:F0} — {cond} H2S generation potential; specify added protection (coatings, venting, chemical addition or aeration; IDCM 3.12.14 item 4)."));
            }
            msgs.Add(CalcMessage.Info("I-SS-004", pomeroy
                ? "H2S: Pomeroy (EPA 1974, ASCE MOP 60) Z = 3·EBOD/(S^½·Q^⅓)·P/B with S in ft/100 ft."
                : "H2S: Z = EBOD/(S^½·Q^⅓)·P/B exactly as printed in IDCM 3.12.14 (S ft/ft, Q cfs). The original Pomeroy (EPA 1974) form, with 3·EBOD and S in ft/100 ft, gives values 3.3 times smaller; set h2s formula = Pomeroy to use it. The IDCM table prints \"Z < 8,000 rare\" and \"5,000 ≤ Z ≤ 10,000 marginal\"; MasterFlow takes 5,000 as the upper limit of rare."));
            if (pipes.Any(x => x.D > 24)) msgs.Add(CalcMessage.Info("I-SS-005", "Sewers larger than 24 in. need the ASCE MOP 60 sulfide analysis (IDCM 3.12.14 item 5); they are not in the Z table."));
        }

        // ---- summary ----
        var outlet = pipes.Where(x => !pipes.Any(y => y.From.Equals(x.To, StringComparison.OrdinalIgnoreCase))).ToList();
        var summary = new List<string>
        {
            $"Criteria: {(rc ? "City of Rapid City IDCM (Jan. 2012) §3.12 with Ten States Standards minimum slopes" : "Ten States Standards (2014) §11, §33–34")}; peaking: {(rcPeaking ? $"IDCM PF {resPf:G3}/{comPf:G3}/{pubPf:G3}" + (rfOn ? " with reduction factors" : "") : fixedPf is { } fp ? $"fixed {fp:G3}" : "Harmon 1 + 14/(4 + √P)")}.",
        };
        foreach (var o in outlet)
            summary.Add($"At the outlet {o.To} ({o.Name}): average {o.QaveGpm + o.Ii:F1} gpm, peak {o.QpeakGpm + o.Ii:F1} gpm ({(o.QpeakGpm + o.Ii) / GpmPerCfs:F3} cfs); {o.D:G3} in. at {o.Slope * 100:F2} %.");
        int warn = msgs.Count(m => m.Severity == MessageSeverity.Warning);
        summary.Add(warn == 0 ? "Every sewer and manhole meets the criteria." : $"{warn} criteria warning(s); see Messages.");
        if (designed.Count > 0) summary.Add($"MasterFlow designed {designed.Count} sewer(s) (blank sizes or inverts); see \"Designed sizes and inverts\".");

        var tables = new List<ToolTable>
        {
            new("Sanitary sewer design computations (IDCM Table 3-3)",
                ["Line", "Location", "From MH", "Invert drop (ft)", "To MH", "Length (ft)", "n", "Area incr. (ac)", "Area total (ac)", "Q peak (gpm)", "Q ave (gpm)", "Slope (%)", "D (in)",
                 "Peak d (in)", "Peak V (ft/s)", "d/D", "Ave d (in)", "Ave V (ft/s)"], formRows,
                [c2c ? $"Slope = (up invert − down invert)/(length − {mhDia:G3} ft manhole), inside wall to inside wall (IDCM 3.12.6 item 7)." : "Slope = (up invert − down invert)/length; lengths entered inside to inside.",
                 "Invert drop = drop across the From manhole from the lowest incoming invert. Q includes infiltration and inflow."]),
            new("Design flows (accumulated upstream)",
                ["Pipe", "Res. population", "Res. Qave (gpm)", "RF res.", "Com./ind. area (ac)", "Com./ind. Qave (gpm)", "RF com.", "Public Qave (gpm)", "Point Qave (gpm)", "I/I (gpm)", "Qave (gpm)", "PF (sanitary)", "Qpeak (gpm)", "Qpeak (cfs)"], flowRows,
                [rcPeaking ? $"IDCM 3.12.2: residential Qave = {gpcd:G3} gpcd × {pph:G3} pph × DU/ac × A / 1440; commercial {comRate:G3}, industrial {indRate:G3}, public {pubRate:G3} gpm/ac. Qpeak = Σ Qave × PF × RF, RF from the population (residential) or acres (commercial and industrial) served above each pipe."
                            : $"Qave as IDCM 3.12.2; Qpeak = Qave × PF with P = Qave·1440/{gpcd:G3} gpcd equivalent population.",
                 $"I/I = {infil:G3} gpd/in-mi × Σ(D·L) upstream + {inflowRate:G3} gpd/ac × area served; added to Qave and Qpeak. Point loads (Flow) use their own PF."]),
            new("Pipe checks",
                ["Pipe", "D (in)", "Slope (%)", "Ten States min (%)", $"Max for {vMax:G3} ft/s (%)", "Q full (gpm)", "V full = V half (ft/s)", "V peak (ft/s)", "d/D peak", "Cover up (ft)", "Cover down (ft)", "Status"], checkRows,
                [$"Limits: d/D ≤ {maxDd:G2} at peak; V ≥ {vMin:G3} ft/s at {(vAtPeak ? "peak flow (IDCM 3.12.6)" : "full flow (Ten States)")}; V ≤ {vMax:G3} ft/s at half depth; slope ≥ Ten States §33.41; cover ≥ {cover:G3} ft over the top of pipe{(wallIn > 0 ? $" (wall {wallIn:G3} in.)" : " (wall thickness 0: cover to the inside crown)")}; spacing ≤ {spacingSmall:G3} ft (≤ 15 in.) / {spacingLarge:G3} ft."]),
            new("Manholes",
                ["Manhole", "Rim", "Lowest invert", "Depth (ft)", "Structure", "Top", "Drop by inlet (ft)", "Required (ft)", "Note"], mhRows,
                ["IDCM 3.12.9: 48 in. minimum (Rinker precast round), precast tees with a 48 in. riser on 24 in. and larger mains; eccentric cone at 5.5 ft deep and over, flat top below; no steps.",
                 "Required drop = the greater of the average of the slopes in and out across the manhole and 0.10 ft (deflection ≤ 45°) or 0.15 ft (over 45°). Deflection from the [Manholes] column, or from coordinates in the network."]),
        };
        if (h2sRows.Count > 0)
            tables.Add(new("Hydrogen sulfide Z (IDCM 3.12.14)", ["Pipe", "D (in)", "EBOD (mg/L)", .. H2SFractions.Select(f => $"Z at {f * 100:G3} %"), "Max Z", "Condition"], h2sRows,
                [$"Flattest slope in the system: {flattest * 100:F3} %, used for every pipe as IDCM 3.12.14 item 2 directs. EBOD = {ebodRes:G3} mg/L residential and public, {ebodCom:G3} mg/L commercial and industrial, weighted by average flow.",
                 "Condition: Z < 5,000 rare; 5,000 to 10,000 marginal; over 10,000 common."]));
        if (designed.Count > 0)
            tables.Add(new("Designed sizes and inverts", ["Pipe", "D (in)", "Up invert", "Down invert", "Slope (%)"], designed,
                [$"Upstream invert = the lowest of: rim − {cover:G3} ft cover − D; each inlet invert − required drop; crown matched to the lowest inlet crown. Slope = the steeper of the Ten States minimum{(vAtPeak ? "" : $" ({vMin:G3} ft/s full)")} and the slope keeping {cover:G3} ft cover at the next manhole, capped at {vMax:G3} ft/s. The {vMin:G3} ft/s at peak flow is checked, not designed for: see \"Design exceptions\".",
                 "Smallest listed size (not below the minimum or any upstream pipe) with d/D within the limit at that slope."]));
        if (exceptions.Count > 0)
        {
            tables.Add(new("Design exceptions: velocity at peak flow (IDCM 3.12.6)", ["Pipe", "D (in)", "Slope (%)", "V peak (ft/s)", $"Slope for {vMin:G3} ft/s (%)", "V average (ft/s)", "Item 2 deviation"], exceptions,
                [$"These sewers do not reach {vMin:G3} ft/s at the design peak flow (IDCM 3.12.6 item 1). MasterFlow leaves their slopes as designed and lists the slope that would be needed. Steepen them, or request a design exception in writing from the Public Works Director or his designee; item 2 allows down to 1.8 ft/s at average daily flow with that approval."]));
            summary.Add($"{exceptions.Count} sewer(s) need an increased slope for {vMin:G3} ft/s at peak flow or a design exception; see \"Design exceptions\".");
        }

        msgs.Add(CalcMessage.Info("I-SS-002", rc
            ? "Rapid City IDCM 2012 §3.12 governs, with Ten States Standards (adopted by §3.3.2) where the IDCM refers to them. Master planning documents override the per-capita and per-acre rates (§3.12.2)."
            : "Ten States Standards (2014): 100 gpcd including normal infiltration (§11.243), Harmon peaking, minimum slopes for 2 ft/s full (§33.41), spacing 400/500 ft (§34.1)."));
        if (infil == 0 && inflowRate == 0) msgs.Add(CalcMessage.Info("I-SS-003", "No infiltration or inflow allowance added (the IDCM rates include none separately); add infiltration (gpd/in-mi) or inflow (gpd/ac) if the master plan calls for it."));

        // ---- profile of the longest path ----
        var figures = new List<ToolFigure>();
        var path = LongestPath(pipes, byName);
        if (path.Count > 0)
        {
            double xAt = 0;
            var ground = new List<(double, double)>(); var inv = new List<(double, double)>(); var crown = new List<(double, double)>(); var ws = new List<(double, double)>();
            foreach (var x in path)
            {
                double dFt = x.D!.Value / 12, y = NormalDepth((x.QpeakGpm + x.Ii) / GpmPerCfs, dFt, x.N, x.Slope);
                if (!double.IsFinite(y)) y = dFt;
                if (mhs[x.From].Rim is { } r1) ground.Add((xAt, r1));
                inv.Add((xAt, x.Up!.Value)); crown.Add((xAt, x.Up.Value + dFt)); ws.Add((xAt, x.Up.Value + y));
                xAt += x.Length;
                if (mhs[x.To].Rim is { } r2) ground.Add((xAt, r2));
                inv.Add((xAt, x.Dn!.Value)); crown.Add((xAt, x.Dn.Value + dFt)); ws.Add((xAt, x.Dn.Value + y));
            }
            var poly = inv.Concat(Enumerable.Reverse(ws)).ToList();
            var series = new List<ToolFigureSeries>();
            if (ground.Count >= 2) series.Add(new("Ground (rim)", ToolSeriesKind.Ground, ground));
            series.Add(new("Peak flow", ToolSeriesKind.Water, poly));
            series.Add(new("Invert", ToolSeriesKind.Limit, inv));
            series.Add(new("Crown", ToolSeriesKind.Limit, crown));
            figures.Add(new ToolFigure($"Profile {path[0].From} to {path[^1].To}", series, "Distance (ft)", "Elevation (ft)",
                [$"Longest flow path: {string.Join(", ", path.Select(x => x.Name))}. Water surface at peak-flow normal depth."]));
        }

        StormNetwork? updated = null;
        if (fromNetwork && designed.Count > 0)
            updated = net! with
            {
                Pipes = net.Pipes.Select(np => byName.TryGetValue(np.Name, out var x)
                    ? np with { Rise = x.D!.Value / 12, Span = x.D.Value / 12, UpstreamInvert = R(x.Up!.Value, 2), DownstreamInvert = R(x.Dn!.Value, 2), Discharge = R((x.QpeakGpm + x.Ii) / GpmPerCfs, 4) }
                    : np).ToList(),
            };

        capture?.Invoke(new SanitaryFlowDesign(
            pipes.Select(x => new SanitaryDesignPipe(x.Name, x.From, x.To, x.Length, x.D!.Value, x.N, x.Up!.Value, x.Dn!.Value, x.Slope,
                x.QaveGpm, x.QpeakGpm, x.Ii, inflowRate * x.AreaTotal / 1440)).ToList(),
            mhs.Values.Where(m => pipes.Any(x => x.From.Equals(m.Name, StringComparison.OrdinalIgnoreCase) || x.To.Equals(m.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(m => new SanitaryDesignManhole(m.Name, m.Rim, m.N, m.E)).ToList(),
            loads.Select(l => new SanitaryDesignLoad(l.Manhole, (SanitaryLandUse)(int)l.Use, l.Area, l.Population, l.QaveGpm, l.Pf)).ToList(),
            mhDia, cover));

        layout = new SewerLayout(
            order.Select(nm => byName[nm]).Select(x => new DesignedSewer(x.Name, x.From, x.To, x.Length, Run(x), x.D!.Value, x.Up!.Value, x.Dn!.Value, x.N, x.Slope,
                x.QaveGpm + x.Ii, x.QpeakGpm + x.Ii, x.PopRes, x.DesignD)).ToList(),
            mhs.Values.Select(m => new DesignedManhole(m.Name, m.Rim, m.Deflection, m.N, m.E)).ToList());
        design = new SanitaryDesign(pipeResults, mhResults, maxDd, vMin, vAtPeak, cover, wallIn, rc ? "Rapid City IDCM 2012 §3.12" : "Ten States Standards (2014)");
        return new ToolResult(title, summary, tables, msgs)
        {
            Figures = figures,
            UpdatedNetwork = updated,
            UpdateDescription = updated is null ? null : $"Write the designed sizes, inverts and sanitary peak flows into {net!.Name}.",
        };

        // ---- local helpers ----
        double Deflection(Manhole m, Pipe inP, Pipe outP)
        {
            if (m.Deflection is { } d) return d;
            var a = mhs[inP.From]; var b = mhs[outP.To];
            if (m.N is not { } n0 || m.E is not { } e0 || a.N is not { } n1 || a.E is not { } e1 || b.N is not { } n2 || b.E is not { } e2) return 0;
            double ax = e0 - e1, ay = n0 - n1, bx = e2 - e0, by = n2 - n0;
            double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
            if (la < 1e-9 || lb < 1e-9) return 0;
            return Math.Acos(Math.Clamp((ax * bx + ay * by) / (la * lb), -1, 1)) * 180 / Math.PI;
        }
        double RequiredDrop(double defl, double sIn, double sOut) => Math.Max((sIn + sOut) / 2 * mhDia, defl > 45 ? 0.15 : 0.10);
        object? DropAt(string mh, Pipe outP)
        {
            var ins = Incoming(mh);
            return ins.Count == 0 ? null : R(ins.Min(y => y.Dn!.Value) - outP.Up!.Value, 2);
        }
        static object? Dep(double y, double dFt) => double.IsFinite(y) ? R(y * 12, 2) : double.NaN;
    }

    /// <summary>Slope (ft/ft) at which the normal-depth velocity of <paramref name="q"/> cfs equals <paramref name="v"/> in a pipe of diameter <paramref name="d"/> ft.</summary>
    public static double SlopeForVelocityAtQ(double q, double d, double n, double v)
    {
        // Velocity at a given Q rises with slope; bisection on log S.
        double lo = Math.Log(1e-6), hi = Math.Log(1.0);
        double Vel(double s) { double y = NormalDepth(q, d, n, s); return double.IsFinite(y) ? q / Area(y, d) : q / (Math.PI * d * d / 4); }
        if (Vel(Math.Exp(hi)) < v) return 1.0;
        for (int i = 0; i < 80; i++) { double m = 0.5 * (lo + hi); if (Vel(Math.Exp(m)) < v) lo = m; else hi = m; }
        return Math.Exp(hi);
    }

    private static List<Pipe> LongestPath(List<Pipe> pipes, Dictionary<string, Pipe> byName)
    {
        List<Pipe> best = [];
        double bestLen = -1;
        foreach (var head in pipes.Where(x => !pipes.Any(y => y.To.Equals(x.From, StringComparison.OrdinalIgnoreCase))))
        {
            var path = new List<Pipe> { head };
            var at = head;
            while (pipes.FirstOrDefault(y => y.From.Equals(at.To, StringComparison.OrdinalIgnoreCase)) is { } nx && path.Count <= pipes.Count) { path.Add(nx); at = nx; }
            double len = path.Sum(x => x.Length);
            if (len > bestLen) { bestLen = len; best = path; }
        }
        return best;
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}

/// <summary>Land use of a sanitary load (same order as the tool's own list).</summary>
public enum SanitaryLandUse { Residential, MultiFamily, Commercial, Industrial, Public, Flow }

/// <summary>A designed sanitary sewer: size (in.), inverts (ft), slope (ft/ft) and accumulated flows (gpm).</summary>
/// <param name="IiGpm">Infiltration and inflow allowance in the pipe (not included in Qave or Qpeak).</param>
/// <param name="InflowGpm">The inflow (gpd/ac) part of <paramref name="IiGpm"/>; the rest is infiltration.</param>
public sealed record SanitaryDesignPipe(string Name, string From, string To, double Length, double DiameterIn, double N, double UpInvert, double DownInvert,
    double Slope, double QaveGpm, double QpeakGpm, double IiGpm, double InflowGpm);

public sealed record SanitaryDesignManhole(string Name, double? Rim, double? Northing, double? Easting);

/// <summary>A load at a manhole: average flow (gpm) and its land use, area (ac) and population.</summary>
public sealed record SanitaryDesignLoad(string Manhole, SanitaryLandUse Use, double AreaAc, double Population, double QaveGpm, double Pf);

/// <summary>The flows and loads of a sanitary sewer design, for the tools that route them (wet-weather flows, bypass pumping).</summary>
public sealed record SanitaryFlowDesign(IReadOnlyList<SanitaryDesignPipe> Pipes, IReadOnlyList<SanitaryDesignManhole> Manholes, IReadOnlyList<SanitaryDesignLoad> Loads,
    double ManholeDiameterFt, double MinimumCoverFt)
{
    /// <summary>The outlet pipes (no pipe downstream).</summary>
    public IEnumerable<SanitaryDesignPipe> Outlets => Pipes.Where(x => !Pipes.Any(y => y.From.Equals(x.To, StringComparison.OrdinalIgnoreCase)));
    public SanitaryDesignPipe? Pipe(string name) => Pipes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public SanitaryDesignManhole? Manhole(string name) => Manholes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    /// <summary>Pipes upstream of and including <paramref name="pipe"/>.</summary>
    public List<SanitaryDesignPipe> Upstream(SanitaryDesignPipe pipe)
    {
        var list = new List<SanitaryDesignPipe> { pipe };
        var stack = new Stack<string>([pipe.From]);
        while (stack.Count > 0)
        {
            string at = stack.Pop();
            foreach (var y in Pipes.Where(y => y.To.Equals(at, StringComparison.OrdinalIgnoreCase))) { list.Add(y); stack.Push(y.From); }
        }
        return list;
    }

    /// <summary>
    /// The saved sanitary sewer design of the project (the sanitary-network tool's input), or null when there is none.
    /// </summary>
    public static (ToolResult Result, SanitaryFlowDesign? Design)? FromProject(DesignToolContext context) =>
        context.ToolInputs.TryGetValue("sanitary-network", out var text) && !string.IsNullOrWhiteSpace(text) ? SanitarySewerTool.FlowDesign(text, context) : null;
}

/// <summary>One sewer of a sanitary design: geometry as designed or entered, peak and average flows (gpm, I/I included), peak
/// normal depth (ft, NaN when surcharged), velocities (ft/s), cover over the top of pipe at each end (ft) and the check flags.</summary>
public sealed record SanitaryPipeResult(string Name, string From, string To, double Length, double DiameterIn, double N, double UpInvert, double DownInvert,
    double Slope, double QpeakGpm, double QaveGpm, double PeakDepth, double PeakVelocity, double AverageVelocity, bool Surcharged,
    double? CoverUp, double? CoverDown, IReadOnlyList<string> Issues, bool DesignException, double? SlopeForMinVelocity)
{
    public double DiameterFt => DiameterIn / 12;
    public double PeakDd => Surcharged ? double.NaN : PeakDepth / DiameterFt;
}

/// <summary>One manhole of a sanitary design: rim, coordinates, lowest invert, depth, structure type and flags.</summary>
public sealed record SanitaryManholeResult(string Name, double? Rim, double? Northing, double? Easting, double? LowestInvert, double? Depth,
    string Structure, string Top, string Note, IReadOnlyList<string> Flags);

/// <summary>A sanitary sewer design as data, with the limits it was checked against.</summary>
public sealed record SanitaryDesign(IReadOnlyList<SanitaryPipeResult> Pipes, IReadOnlyList<SanitaryManholeResult> Manholes,
    double MaxDd, double MinVelocity, bool VelocityAtPeak, double MinCover, double WallIn, string Criteria)
{
    public SanitaryPipeResult? Outlet(string manhole) => Pipes.FirstOrDefault(p => p.From.Equals(manhole, StringComparison.OrdinalIgnoreCase));
    public SanitaryManholeResult? Manhole(string name) => Manholes.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pipes from <paramref name="start"/> down to the outlet, upstream first.</summary>
    public List<SanitaryPipeResult> PathToOutlet(string start)
    {
        var path = new List<SanitaryPipeResult>();
        string at = start;
        while (Outlet(at) is { } p && path.Count <= Pipes.Count) { path.Add(p); at = p.To; }
        return path;
    }

    /// <summary>Upstream ends: manholes with an outlet pipe and no inlet.</summary>
    public IEnumerable<string> Heads => Pipes.Where(p => !Pipes.Any(q => q.To.Equals(p.From, StringComparison.OrdinalIgnoreCase))).Select(p => p.From);
}
