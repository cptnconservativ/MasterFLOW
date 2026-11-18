namespace MasterFlow.Application.Tools;

/// <summary>
/// One outlet of a detention pond's outlet works. Types:
/// <list type="bullet">
/// <item><c>orifice</c>: circular (Size = D) or rectangular (Size = width, Height = height). Full: Q = C A √(2g h), h to the centroid;
/// below the top: Q = (2/3) C √(2g) B h^1.5, B = A / height.</item>
/// <item><c>weir</c>: Q = C L h^1.5 (Size = crest length).</item>
/// <item><c>v-notch</c>: Q = C tan(θ/2) h^2.5 (Size = notch angle θ in degrees; C ≈ 2.5 U.S.).</item>
/// <item><c>riser</c>: drop-inlet riser with its crest at Invert; circular (Size = D) or box (Size × Height). Weir control
/// C P h^1.5 (P = perimeter) or orifice control OrificeC·A·√(2g h) over the riser opening, whichever is less (IDCM §4.10.5).</item>
/// <item><c>pipe</c>: the outlet pipe (barrel); not an inlet, it limits the principal outlets (see <see cref="BarrelCapacity"/>).</item>
/// <item><c>spillway</c>: emergency spillway, broad-crested trapezoidal weir, Q = C (L h^1.5 + 0.8 z h^2.5), Size = bottom width L,
/// Height = side slope z (H:V). It discharges beside the principal outlet, so the barrel does not limit it.</item>
/// </list>
/// </summary>
public sealed record PondOutlet(string Name, string Type, double Invert, double Size, double Height, double Coefficient, double Length = 0, double N = 0.013, double OutletInvert = double.NaN, double OrificeC = 0.6)
{
    public static readonly string[] Types = ["orifice", "weir", "v-notch", "riser", "pipe", "spillway"];

    /// <summary>True for the inlets whose total the outlet pipe limits (everything but the barrel and the emergency spillway).</summary>
    public bool IsPrincipalInlet => Type is "orifice" or "weir" or "v-notch" or "riser";

    /// <summary>Discharge at water surface elevation <paramref name="wse"/> (0 for the barrel; see <see cref="BarrelCapacity"/>).</summary>
    public double Flow(double wse, double g)
    {
        double h = wse - Invert;
        if (!(h > 0)) return 0;
        switch (Type)
        {
            case "weir": return Coefficient * Size * Math.Pow(h, 1.5);
            case "v-notch": return Coefficient * Math.Tan(Size * Math.PI / 360) * Math.Pow(h, 2.5);
            case "spillway": return Coefficient * (Size * Math.Pow(h, 1.5) + 0.8 * Height * Math.Pow(h, 2.5));
            case "riser": return Math.Min(RiserWeir(h), RiserOrifice(h, g));
            case "orifice":
            {
                bool round = Height <= 0;
                double ht = round ? Size : Height, area = round ? Math.PI * Size * Size / 4 : Size * Height, b = area / ht;
                return h < ht ? 2.0 / 3.0 * Coefficient * Math.Sqrt(2 * g) * b * Math.Pow(h, 1.5)
                              : Coefficient * area * Math.Sqrt(2 * g * (h - ht / 2));
            }
            default: return 0;
        }
    }

    public double RiserPerimeter => Height > 0 ? 2 * (Size + Height) : Math.PI * Size;
    public double RiserArea => Height > 0 ? Size * Height : Math.PI * Size * Size / 4;
    public double RiserWeir(double h) => h > 0 ? Coefficient * RiserPerimeter * Math.Pow(h, 1.5) : 0;
    public double RiserOrifice(double h, double g) => h > 0 ? OrificeC * RiserArea * Math.Sqrt(2 * g * h) : 0;

    /// <summary>
    /// Outlet pipe (barrel) capacity at pond level <paramref name="wse"/>: the culvert flow whose FHWA HDS-5 controlling
    /// headwater (the larger of inlet control and outlet control, see <see cref="Culverts.CulvertAnalysis"/>) equals
    /// WSE − inlet invert, with a free outfall (TW = 0, so ho = (dc + D)/2). Inlet: concrete pipe, square edge with
    /// headwall (HDS-5 chart 1); Ke = Coefficient. The rating is continuous and rises with stage, which
    /// storage-indication routing needs. This replaces the earlier below-crown approximation Qcrown·(h/D)^1.5.
    /// </summary>
    public double BarrelCapacity(double wse, double g, bool us)
    {
        double h = wse - Invert;
        if (!(h > 0)) return 0;
        var barrel = new Culverts.CulvertAnalysis(new Culverts.CulvertDefinition
        {
            Shape = Culverts.BarrelShape.Circular, Rise = Size, N = N, Length = Length > 0 ? Length : 1e-6,
            UpstreamInvert = Invert, DownstreamInvert = double.IsNaN(OutletInvert) || !(Length > 0) ? Invert : OutletInvert,
            Inlet = Culverts.CulvertInlets.Find("circ-conc-square-headwall")!, Ke = Coefficient,
        }, us ? MasterFlow.Domain.UnitSystem.USCustomary : MasterFlow.Domain.UnitSystem.SI);
        return barrel.Capacity(h, 0);
    }

    /// <summary>Critical depth of <paramref name="q"/> in a circular pipe of diameter <paramref name="d"/> (Q²/g = A³/T), capped at D.</summary>
    public static double CircularCriticalDepth(double q, double d, double g)
    {
        if (!(q > 0)) return 0;
        double F(double y)
        {
            double th = 2 * Math.Acos(1 - 2 * y / d), area = d * d / 8 * (th - Math.Sin(th)), top = d * Math.Sin(th / 2);
            return area * area * area / top - q * q / g;
        }
        double lo = 1e-9 * d, hi = d * (1 - 1e-9);
        if (F(hi) < 0) return d;
        for (int i = 0; i < 45; i++) { double m = 0.5 * (lo + hi); if (F(m) < 0) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }
}

/// <summary>How storage is computed between stage–area rows (contours).</summary>
public enum StorageMethod
{
    /// <summary>Frustum of a cone: V = h/3 (A₁ + A₂ + √(A₁A₂)); area varies with the square of the linear dimension.</summary>
    Conic,
    /// <summary>Average end area: V = h (A₁ + A₂)/2; area varies linearly with stage.</summary>
    AverageEndArea,
}

/// <summary>
/// A graded basin with a rectangular bottom (L × W) and uniform side slopes z (H:V). Area and volume are exact:
/// A(h) = (L + 2zh)(W + 2zh), V(h) = LWh + (L + W) z h² + (4/3) z² h³ (prismoidal formula).
/// </summary>
public sealed record BasinGeometry(double BottomElevation, double BottomLength, double BottomWidth, double SideSlope, double TopElevation)
{
    public double Area(double h) => (BottomLength + 2 * SideSlope * h) * (BottomWidth + 2 * SideSlope * h);
    public double Volume(double h) => BottomLength * BottomWidth * h + (BottomLength + BottomWidth) * SideSlope * h * h + 4.0 / 3.0 * SideSlope * SideSlope * h * h * h;
}

/// <summary>One time step of a routed hydrograph.</summary>
public sealed record RouteStep(double TimeMin, double Inflow, double Outflow, double Wse, double Storage, double Spillway);

/// <summary>Stage–storage–discharge relation of a pond and level-pool (storage-indication) routing.</summary>
public sealed class Pond
{
    private enum Basis { Conic, AverageEndArea, VolumeTable, Basin }

    /// <summary>Stage rows for display: elevation and surface area (NaN when the storage was given as volumes).</summary>
    public IReadOnlyList<(double Elev, double Area)> Stages { get; }
    public IReadOnlyList<PondOutlet> Outlets { get; }
    public BasinGeometry? Basin { get; }
    private readonly double[] _elev, _area, _vol;
    private readonly Basis _basis;
    private readonly double _g;
    private readonly bool _us;
    public PondOutlet? Barrel => Outlets.FirstOrDefault(o => o.Type == "pipe");

    /// <summary>Pond from stage–area rows (contours).</summary>
    public Pond(IReadOnlyList<(double Elev, double Area)> stages, IReadOnlyList<PondOutlet> outlets, double g, bool us, StorageMethod method = StorageMethod.Conic)
        : this(stages.Select(s => s.Elev).ToArray(), stages.Select(s => s.Area).ToArray(), null, method == StorageMethod.Conic ? Basis.Conic : Basis.AverageEndArea, null, outlets, g, us) { }

    /// <summary>Pond from an elevation–volume table (e.g. a Civil 3D stage–storage report); storage is linear between rows.</summary>
    public static Pond FromVolumes(IReadOnlyList<(double Elev, double Volume)> rows, IReadOnlyList<PondOutlet> outlets, double g, bool us) =>
        new(rows.Select(s => s.Elev).ToArray(), rows.Select(_ => double.NaN).ToArray(), rows.Select(s => s.Volume).ToArray(), Basis.VolumeTable, null, outlets, g, us);

    /// <summary>Pond from basin geometry; exact prismoidal storage. Display rows every 0.5 ft (or 0.1 of the depth for shallow basins).</summary>
    public static Pond FromBasin(BasinGeometry b, IReadOnlyList<PondOutlet> outlets, double g, bool us)
    {
        double depth = b.TopElevation - b.BottomElevation, step = depth >= 2 ? 0.5 : depth / 10;
        var e = Enumerable.Range(0, (int)Math.Floor(depth / step + 1e-9) + 1).Select(i => b.BottomElevation + i * step).ToList();
        if (b.TopElevation - e[^1] > 1e-9) e.Add(b.TopElevation);
        return new(e.ToArray(), e.Select(x => b.Area(x - b.BottomElevation)).ToArray(), e.Select(x => b.Volume(x - b.BottomElevation)).ToArray(), Basis.Basin, b, outlets, g, us);
    }

    private Pond(double[] elev, double[] area, double[]? vol, Basis basis, BasinGeometry? basin, IReadOnlyList<PondOutlet> outlets, double g, bool us)
    {
        _elev = elev; _area = area; _basis = basis; Basin = basin; Outlets = outlets; _g = g; _us = us;
        Stages = elev.Zip(area).ToList();
        _vol = vol ?? new double[elev.Length];
        if (vol is null)
            for (int i = 1; i < elev.Length; i++)
            {
                double h = elev[i] - elev[i - 1], a1 = area[i - 1], a2 = area[i];
                _vol[i] = _vol[i - 1] + (basis == Basis.Conic ? h / 3 * (a1 + a2 + Math.Sqrt(a1 * a2)) : h * (a1 + a2) / 2);
            }
        // The barrel rating needs nested solves; tabulate it once over the pond's range. Linear interpolation between
        // samples of a continuous, rising rating stays continuous and rising.
        if (Barrel is { } b && elev.Length > 0)
        {
            _bLo = Math.Min(b.Invert, elev[0]);
            double hiE = Math.Max(elev[^1], b.Invert + b.Size);
            _bStep = Math.Max(1e-4, (hiE - _bLo) / 2000);
            _bTable = Enumerable.Range(0, (int)Math.Ceiling((hiE - _bLo) / _bStep) + 3).Select(i => b.BarrelCapacity(_bLo + i * _bStep, g, us)).ToArray();
        }
    }

    private readonly double[]? _bTable;
    private readonly double _bLo, _bStep;

    public double BarrelAt(double e)
    {
        if (_bTable is null || Barrel is not { } b) return double.PositiveInfinity;
        double x = (e - _bLo) / _bStep;
        if (x <= 0) return _bTable[0];
        int i = (int)x;
        if (i >= _bTable.Length - 1) return b.BarrelCapacity(e, _g, _us);
        return _bTable[i] + (x - i) * (_bTable[i + 1] - _bTable[i]);
    }

    public double Bottom => _elev[0];
    public double Top => _elev[^1];
    public double MaxStorage => _vol[^1];

    /// <summary>Text for the report: how storage was computed.</summary>
    public string StorageBasis => _basis switch
    {
        Basis.Conic => "Stage–area rows; frustum (conic) method between rows, V = h/3 (A₁ + A₂ + √(A₁A₂)).",
        Basis.AverageEndArea => "Stage–area rows; average end area between rows, V = h (A₁ + A₂)/2.",
        Basis.VolumeTable => "Elevation–volume table as entered; storage linear between rows.",
        _ => $"Basin geometry: bottom {Basin!.BottomLength:G4} × {Basin.BottomWidth:G4} ft at {Basin.BottomElevation:F2}, side slopes {Basin.SideSlope:G3}H:1V; exact prismoidal volume V = LWh + (L+W)zh² + 4/3·z²h³.",
    };

    /// <summary>Storage at an elevation; 0 at and below the bottom, NaN above the top stage (never extrapolated).</summary>
    public double Storage(double e)
    {
        if (e <= Bottom) return 0;
        if (e > Top + 1e-12) return double.NaN;
        if (_basis == Basis.Basin) return Basin!.Volume(Math.Min(e, Top) - Basin.BottomElevation);
        for (int i = 1; i < _elev.Length; i++)
            if (e <= _elev[i] + 1e-12)
            {
                double h = e - _elev[i - 1], H = _elev[i] - _elev[i - 1], a1 = _area[i - 1], a2 = _area[i];
                switch (_basis)
                {
                    case Basis.VolumeTable: return _vol[i - 1] + (_vol[i] - _vol[i - 1]) * h / H;
                    case Basis.AverageEndArea: { double ae = a1 + (a2 - a1) * h / H; return _vol[i - 1] + h * (a1 + ae) / 2; }
                    default:
                    {
                        double s1 = Math.Sqrt(a1), s2 = Math.Sqrt(a2), ae = Math.Pow(s1 + (s2 - s1) * h / H, 2);
                        return _vol[i - 1] + h / 3 * (a1 + ae + Math.Sqrt(a1 * ae));
                    }
                }
            }
        return double.NaN;
    }

    /// <summary>Principal outlet discharge: orifices, weirs, v-notches and risers added, limited by the outlet pipe when there is one.</summary>
    public double PrincipalOutflow(double e)
    {
        double q = Outlets.Where(o => o.IsPrincipalInlet).Sum(o => o.Flow(e, _g));
        return Barrel is not null ? Math.Min(q, BarrelAt(e)) : q;
    }

    /// <summary>Emergency spillway discharge.</summary>
    public double SpillwayOutflow(double e) => Outlets.Where(o => o.Type == "spillway").Sum(o => o.Flow(e, _g));

    /// <summary>Total outflow: principal outlets plus the emergency spillway.</summary>
    public double Outflow(double e) => PrincipalOutflow(e) + SpillwayOutflow(e);

    /// <summary>Elevation where 2S/dt + O equals <paramref name="target"/> (bisection; NaN above the top stage).</summary>
    public double SolveIndication(double target, double dt)
    {
        double lo = Bottom, hi = Top;
        if (2 * Storage(hi) / dt + Outflow(hi) < target) return double.NaN;
        for (int i = 0; i < 80; i++) { double m = 0.5 * (lo + hi); if (2 * Storage(m) / dt + Outflow(m) < target) lo = m; else hi = m; }
        return 0.5 * (lo + hi);
    }

    public sealed record RouteResult(double PeakInflow, double PeakOutflow, double PeakWse, double PeakStorage, double TimeOfPeakOutflow, double DrainHours, bool Overtopped, double PeakSpillway = 0);

    /// <summary>
    /// Routes a hydrograph given per time step (flow units of the outlets, dt in seconds) by the storage-indication method
    /// (S₂/Δt + O₂/2 = (I₁ + I₂)/2 + S₁/Δt − O₁/2), starting empty, then continues with no inflow to find the drain time
    /// (end of inflow to 0.1 % of the peak storage). <paramref name="steps"/>, when given, receives every step including t = 0.
    /// </summary>
    public RouteResult Route(IReadOnlyList<double> inflow, double dt, double drainLimitHours = 168, List<double>? outflows = null, List<RouteStep>? steps = null)
    {
        double e = Bottom, s = 0, o = 0, peakO = 0, peakE = Bottom, peakS = 0, tPeak = 0, peakSp = 0;
        int maxSteps = inflow.Count + (int)(drainLimitHours * 3600 / dt);
        double endInflow = (inflow.Count - 1) * dt, drain = double.NaN;
        double i1 = inflow.Count > 0 ? inflow[0] : 0;
        steps?.Add(new RouteStep(0, i1, 0, e, 0, 0));
        for (int k = 1; k < maxSteps; k++)
        {
            double i2 = k < inflow.Count ? inflow[k] : 0;
            double target = (i1 + i2) + (2 * s / dt - o);
            double e2 = SolveIndication(target, dt);
            if (double.IsNaN(e2)) return new RouteResult(inflow.Max(), peakO, double.NaN, double.NaN, tPeak, double.NaN, true, peakSp);
            e = e2; s = Storage(e); o = Outflow(e); i1 = i2;
            double sp = SpillwayOutflow(e);
            outflows?.Add(o);
            steps?.Add(new RouteStep(k * dt / 60, i2, o, e, s, sp));
            if (o > peakO) { peakO = o; tPeak = k * dt; }
            if (sp > peakSp) peakSp = sp;
            if (e > peakE) { peakE = e; peakS = s; }
            if (k * dt > endInflow && s <= 0.001 * peakS + 1e-9) { drain = (k * dt - endInflow) / 3600; break; }
        }
        return new RouteResult(inflow.Count > 0 ? inflow.Max() : 0, peakO, peakE, peakS, tPeak, drain, false, peakSp);
    }
}

/// <summary>Inflow hydrograph helpers: resampling a (time, flow) table to the routing step and its volume.</summary>
public static class Hydrograph
{
    /// <summary>
    /// Flows at t = 0, Δt, 2Δt … to the last time, by linear interpolation. A table that does not start at t = 0 is taken
    /// to rise linearly from zero at t = 0 to its first point.
    /// </summary>
    public static List<double> Resample(IReadOnlyList<(double TimeMin, double Q)> points, double dtMin)
    {
        var pts = points.ToList();
        if (pts.Count == 0) return [0];
        if (pts[0].TimeMin > 0) pts.Insert(0, (0, 0));
        int n = (int)Math.Ceiling(pts[^1].TimeMin / dtMin - 1e-9);
        var r = new List<double>(n + 1);
        int j = 0;
        for (int k = 0; k <= n; k++)
        {
            double t = Math.Min(k * dtMin, pts[^1].TimeMin);
            while (j < pts.Count - 2 && pts[j + 1].TimeMin < t) j++;
            var (t1, q1) = pts[j]; var (t2, q2) = pts[Math.Min(j + 1, pts.Count - 1)];
            r.Add(t2 > t1 ? q1 + (q2 - q1) * (t - t1) / (t2 - t1) : q2);
        }
        return r;
    }

    /// <summary>Volume of a (time in min, flow) table by the trapezoid rule, in flow × seconds; a table not starting at 0 rises from zero at t = 0.</summary>
    public static double Volume(IReadOnlyList<(double TimeMin, double Q)> points)
    {
        var pts = points.ToList();
        if (pts.Count > 0 && pts[0].TimeMin > 0) pts.Insert(0, (0, 0));
        return pts.Zip(pts.Skip(1)).Sum(x => (x.First.Q + x.Second.Q) / 2 * (x.Second.TimeMin - x.First.TimeMin) * 60);
    }

    /// <summary>Volume of flows at a uniform step (trapezoid rule), flow × seconds.</summary>
    public static double Volume(IReadOnlyList<double> q, double dtSeconds) => q.Zip(q.Skip(1)).Sum(x => (x.First + x.Second) / 2 * dtSeconds);
}
