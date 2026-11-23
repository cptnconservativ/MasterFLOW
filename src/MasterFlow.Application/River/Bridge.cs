using MasterFlow.Application.Tools;

namespace MasterFlow.Application.River;

/// <summary>A bridge between two adjacent cross-sections (HEC-RAS sections 3 upstream and 2 downstream).</summary>
public sealed record Bridge(string Name, double UpstreamRs, double LowChord, double HighChord, double DeckWidth, double LeftAbutment, double RightAbutment,
    IReadOnlyList<(double Station, double Width)> Piers, string PierShape, double WeirC, double Contraction, double Expansion, double SluiceCd, double OrificeC,
    IReadOnlyList<(double Station, double Elevation)> Road)
{
    /// <summary>Yarnell pier shape coefficient K (HEC-RAS Hydraulic Reference Manual Table 5-1).</summary>
    public static double? YarnellK(string shape) => shape.ToLowerInvariant() switch
    {
        "semicircular" or "semi-circular" or "round" or "semicircular nose and tail" => 0.90,
        "twin cylinder with diaphragm" or "twin-cylinder with diaphragm" => 0.95,
        "twin cylinder" or "twin-cylinder" or "twin cylinder without diaphragm" or "cylinder" or "circular" => 1.05,
        "triangular" or "90° triangular" or "90 triangular" => 1.05,
        "square" or "square nose and tail" => 1.25,
        _ => null,
    };

    /// <summary>Weir crest elevation at a station: the road profile if given, else the deck (high chord), never below the ground.</summary>
    public double Crest(double x, RiverSection ground)
    {
        double road = HighChord;
        if (Road.Count > 0)
        {
            if (x <= Road[0].Station) road = Road[0].Elevation;
            else if (x >= Road[^1].Station) road = Road[^1].Elevation;
            else
                for (int i = 1; i < Road.Count; i++)
                    if (x <= Road[i].Station) { road = Road[i - 1].Elevation + (Road[i].Elevation - Road[i - 1].Elevation) * (x - Road[i - 1].Station) / (Road[i].Station - Road[i - 1].Station); break; }
        }
        return Math.Max(road, ground.Interp(x));
    }
}

/// <summary>The bridge solution for one profile.</summary>
public sealed record BridgeResult(Bridge Bridge, string Profile, string FlowType, RiverProfileTool.Result Upstream, RiverProfileTool.Result Downstream,
    double QOpening, double QWeir, double OpeningVelocity, double? EnergyWs, double? YarnellWs, double? InsideUpWs, double? InsideDownWs, string Note);

/// <summary>
/// Bridge hydraulics by the HEC-RAS methods (Hydraulic Reference Manual Ch. 5): low flow by the energy method (standard
/// step through the bridge opening, sections BD and BU, with the deck width as the reach length) and/or Yarnell's pier
/// equation; high flow by the sluice-gate or fully submerged orifice equation for the opening plus broad-crested weir flow
/// over the road, split by iteration so that the two carry the total flow at one upstream energy grade.
/// </summary>
public static class BridgeHydraulics
{
    public static BridgeResult Solve(Bridge b, RiverSection up, RiverSection down, RiverProfileTool.Result d, double q, double g, string lowFlowMethod, string profile, List<Hydraulics.CalcMessage> msgs)
    {
        double deck = b.DeckWidth;
        var bd = down.Opening($"{b.Name} BD", b.LeftAbutment, b.RightAbutment, b.HighChord, b.Piers, 0, 0, 0, b.Contraction, b.Expansion, b.LowChord);
        var bu = up.Opening($"{b.Name} BU", b.LeftAbutment, b.RightAbutment, b.HighChord, b.Piers, deck, deck, deck, b.Contraction, b.Expansion, b.LowChord);
        var upRest = up.WithLengths(Math.Max(0, up.LengthLob - deck), Math.Max(0, up.LengthCh - deck), Math.Max(0, up.LengthRob - deck));
        double openArea = bu.At(b.LowChord).Area;
        double bed = Math.Min(bu.MinElevation, bd.MinElevation);

        // Low flow by energy for a given opening flow, with the downstream water surface held.
        (RiverProfileTool.Result U, RiverProfileTool.Result BU, RiverProfileTool.Result BD) Energy(double qo)
        {
            var dd = RiverProfileTool.Make(down, qo, d.Ws, d.CritWs, g, 0, "", "");
            var rbd = RiverProfileTool.StepUp(bd, qo, dd, g, out _);
            var rbu = RiverProfileTool.StepUp(bu, qo, rbd, g, out _);
            var ru = RiverProfileTool.StepUp(upRest, qo, rbu, g, out _);
            return (RiverProfileTool.Make(up, qo, ru.Ws, ru.CritWs, g, 0, "", ""), rbu, rbd);
        }

        // Yarnell (class A low flow): ΔH = 2K(K + 10ω − 0.6)(α + 15α⁴)·V2²/2g, ω = V2²/2g / y2, α = pier area / unobstructed area.
        double? Yarnell()
        {
            if (Bridge.YarnellK(b.PierShape) is not { } k || b.Piers.Count == 0) return null;
            var h2 = d.H;
            double v2 = q / h2.Area, hv = v2 * v2 / (2 * g), y2 = d.Ws - down.MinChannelElevation;
            double pierArea = b.Piers.Sum(pp => pp.Width * Math.Max(0, d.Ws - down.Interp(pp.Station)));
            double alpha = pierArea / h2.Area, omega = hv / y2;
            return d.Ws + 2 * k * (k + 10 * omega - 0.6) * (alpha + 15 * Math.Pow(alpha, 4)) * hv;
        }

        var low = Energy(q);
        double? yws = Yarnell();
        double ews = low.U.Ws;
        double lowWs = lowFlowMethod switch
        {
            "yarnell" when yws is { } y => y,
            "highest" when yws is { } y => Math.Max(y, ews),
            _ => ews,
        };
        if (lowFlowMethod is "yarnell" or "highest" && yws is null)
            msgs.Add(Hydraulics.CalcMessage.Warning("W-BR-001", $"{profile}, {b.Name}: Yarnell needs piers and a pier shape it knows; the energy method is used."));
        var lowU = RiverProfileTool.Make(up, q, lowWs, low.U.CritWs, g, 0, "subcritical", "");
        double minCrest = MinCrest(b, up);

        // High flow when the upstream energy reaches the low chord, or the road is overtopped.
        if (lowU.Eg < b.LowChord && lowU.Eg <= minCrest)
        {
            bool classB = low.BU.Note.Contains("critical") || low.BD.Note.Contains("critical") || low.BU.Ws <= low.BU.CritWs + 1e-4;
            string ft = lowWs == ews ? "low flow, energy" : "low flow, Yarnell";
            if (classB) ft += " (class B: critical depth in the opening)";
            return new(b, profile, ft, lowU with { Note = ft }, d, q, 0, q / low.BU.H.Area, ews, yws, low.BU.Ws, low.BD.Ws, "");
        }

        double QOpen(double eg)
        {
            if (eg <= bed) return 0;
            if (eg >= b.LowChord)
            {
                if (d.Ws > b.LowChord) return b.OrificeC * openArea * Math.Sqrt(2 * g * Math.Max(0, eg - d.Ws));
                double z = b.LowChord - bed;
                return b.SluiceCd * openArea * Math.Sqrt(2 * g * Math.Max(0, eg - bed - z / 2));
            }
            // Free surface in the opening: the opening flow whose energy solution reaches this upstream energy.
            double lo = 0, hi = q;
            if (Energy(hi).U.Eg <= eg) return hi * Math.Sqrt(Math.Max(1, (eg - bed) / Math.Max(Energy(hi).U.Eg - bed, 1e-9)));
            for (int i = 0; i < 40; i++) { double m = 0.5 * (lo + hi); if (Energy(Math.Max(m, 1e-6)).U.Eg < eg) lo = m; else hi = m; }
            return 0.5 * (lo + hi);
        }
        double QWeir(double eg) => WeirFlow(b, up, eg, d.Ws);

        double F(double ws) { double eg = up.Energy(ws, q, g); return QOpen(eg) + QWeir(eg) - q; }
        var (crit, _) = up.Critical(q, g);
        double a = Math.Max(crit, d.Ws) + 1e-4, step = Math.Max(0.1, 0.05 * (b.HighChord - bed));
        double bb = a;
        int guard = 0;
        while (F(bb) < 0 && guard++ < 400) { a = bb; bb += step; }
        for (int i = 0; i < 60 && bb - a > 1e-5; i++) { double m = 0.5 * (a + bb); if (F(m) < 0) a = m; else bb = m; }
        double wsU = 0.5 * (a + bb), egU = up.Energy(wsU, q, g);
        double qw = QWeir(egU), qo = q - qw;
        // HEC-RAS compares the pressure answer with the low flow answer and keeps the higher energy (Ch. 5, "high flow").
        if (qw <= 1e-6 * q && lowU.Eg > egU)
        {
            string ft2 = Math.Max(low.BU.Ws, low.BD.Ws) > b.LowChord ? "energy method, opening under pressure (above the sluice/orifice answer)" : "low flow, energy (above the pressure flow answer)";
            return new(b, profile, ft2, lowU with { Note = ft2 }, d, q, 0, q / low.BU.H.Area, ews, yws, low.BU.Ws, low.BD.Ws, $"pressure flow gave W.S. {wsU:F2}");
        }
        string type = egU >= b.LowChord ? (d.Ws > b.LowChord ? "pressure (orifice)" : "pressure (sluice gate)") : "low flow";
        if (qw > 1e-6 * q) type += " + weir";
        var hu = RiverProfileTool.Make(up, q, wsU, crit, g, 0, "subcritical", type);
        return new(b, profile, type, hu, d, qo, qw, qo / Math.Max(openArea, 1e-9), ews, yws, null, null, "");
    }

    /// <summary>Lowest weir crest along the upstream section.</summary>
    public static double MinCrest(Bridge b, RiverSection up)
    {
        var pts = up.Points;
        double m = double.PositiveInfinity;
        int n = 400;
        for (int i = 0; i <= n; i++) { double x = pts[0].X + (pts[^1].X - pts[0].X) * i / n; m = Math.Min(m, b.Crest(x, up)); }
        return m;
    }

    /// <summary>
    /// Weir flow over the road: Q = C·∫(EG − crest)^1.5 dx where EG is above the crest, along the upstream section's width,
    /// reduced for submergence by Villemonte's (1 − (hd/hu)^1.5)^0.385 with hd the downstream water surface above the crest.
    /// </summary>
    public static double WeirFlow(Bridge b, RiverSection up, double eg, double wsDown)
    {
        var pts = up.Points;
        int n = 600;
        double x0 = pts[0].X, x1 = pts[^1].X, dx = (x1 - x0) / n, sum = 0;
        for (int i = 0; i < n; i++)
        {
            double x = x0 + (i + 0.5) * dx, crest = b.Crest(x, up), hu = eg - crest;
            if (hu <= 0) continue;
            double hd = wsDown - crest, f = hd > 0 ? Math.Pow(Math.Max(0, 1 - Math.Pow(Math.Min(hd / hu, 1), 1.5)), 0.385) : 1;
            sum += Math.Pow(hu, 1.5) * f * dx;
        }
        return b.WeirC * sum;
    }
}
