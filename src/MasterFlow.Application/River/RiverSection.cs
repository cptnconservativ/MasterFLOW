namespace MasterFlow.Application.River;

/// <summary>Hydraulic properties of a natural cross-section at one water surface elevation (effective flow area only).</summary>
public sealed record SectionHydraulics(double Ws, double Area, double TopWidth, double Conveyance, double Alpha,
    double AreaLob, double AreaCh, double AreaRob, double KLob, double KCh, double KRob, double TopCh, double Moment, bool WallLeft, bool WallRight)
{
    public double Velocity(double q) => q / Area;
    public double VelocityHead(double q, double g) => Alpha * q * q / (2 * g * Area * Area);
}

/// <summary>
/// A surveyed river cross-section (HEC-RAS style): station–elevation points, bank stations splitting it into left
/// overbank, main channel and right overbank, Manning's n by subsection or by point, ineffective flow areas and reach
/// lengths to the next section downstream. Conveyance is computed for each n segment and summed per subsection (HEC-RAS
/// Hydraulic Reference Manual Ch. 2, "conveyance subdivision"); α = Σ(Ki³/Ai²)/(Kt³/At²). Water above an end point is held
/// by a vertical wall at the end station, whose height is added to the wetted perimeter, as HEC-RAS does.
/// </summary>
public sealed class RiverSection
{
    public string Name { get; }
    public double RiverStation { get; }
    public double LeftBank { get; }
    public double RightBank { get; }
    public double LengthLob { get; init; }
    public double LengthCh { get; init; }
    public double LengthRob { get; init; }
    public double Contraction { get; init; } = 0.1;
    public double Expansion { get; init; } = 0.3;
    /// <summary>Left ineffective area: station and the elevation it becomes effective at (null = none).</summary>
    public (double Station, double Elevation)? IneffectiveLeft { get; init; }
    public (double Station, double Elevation)? IneffectiveRight { get; init; }
    /// <summary>Bridge deck low chord over the whole section (bridge opening sections): water above it is under pressure.</summary>
    public double? Lid { get; init; }

    private readonly double[] _x, _z, _n;   // segment i runs from point i to i+1 with roughness _n[i]
    private readonly int[] _sub;            // 0 LOB, 1 channel, 2 ROB per segment
    private readonly double _k;

    public IReadOnlyList<(double X, double Z)> Points { get; }
    public double MinElevation { get; }
    public double LeftBankElevation { get; }
    public double RightBankElevation { get; }
    public double MinChannelElevation { get; }

    public RiverSection(string name, double rs, IReadOnlyList<(double X, double Z, double? N)> pts, double leftBank, double rightBank,
        double nLob, double nCh, double nRob, double k, (double, double)? ineffLeft = null, (double, double)? ineffRight = null)
    {
        Name = name; RiverStation = rs; LeftBank = leftBank; RightBank = rightBank; _k = k;
        IneffectiveLeft = ineffLeft; IneffectiveRight = ineffRight;
        // Split segments at the bank stations and ineffective stations so no segment straddles a boundary.
        var cuts = new List<double> { leftBank, rightBank };
        if (ineffLeft is { } il) cuts.Add(il.Item1);
        if (ineffRight is { } ir) cuts.Add(ir.Item1);
        var list = pts.Select(p => (p.X, p.Z, p.N)).ToList();
        foreach (double c in cuts)
        {
            for (int i = 0; i < list.Count - 1; i++)
                if (list[i].X < c - 1e-9 && list[i + 1].X > c + 1e-9)
                {
                    double f = (c - list[i].X) / (list[i + 1].X - list[i].X);
                    list.Insert(i + 1, (c, list[i].Z + f * (list[i + 1].Z - list[i].Z), list[i].N));
                    break;
                }
        }
        _x = list.Select(p => p.X).ToArray();
        _z = list.Select(p => p.Z).ToArray();
        int m = _x.Length - 1;
        _n = new double[m]; _sub = new int[m];
        double? carried = null;
        for (int i = 0; i < m; i++)
        {
            if (list[i].N is { } pn) carried = pn;
            double mid = 0.5 * (_x[i] + _x[i + 1]);
            _sub[i] = mid < leftBank ? 0 : mid > rightBank ? 2 : 1;
            _n[i] = carried ?? (_sub[i] == 0 ? nLob : _sub[i] == 1 ? nCh : nRob);
        }
        Points = list.Select(p => (p.X, p.Z)).ToList();
        MinElevation = _z.Min();
        LeftBankElevation = Interp(leftBank);
        RightBankElevation = Interp(rightBank);
        MinChannelElevation = Enumerable.Range(0, _x.Length).Where(i => _x[i] >= leftBank - 1e-9 && _x[i] <= rightBank + 1e-9).Select(i => _z[i]).DefaultIfEmpty(MinElevation).Min();
    }

    /// <summary>
    /// The section as seen through a bridge opening (HEC-RAS bridge sections BU/BD): ground outside the abutment stations is
    /// blocked by vertical walls up to <paramref name="top"/>, and each pier is a vertical block from the ground to
    /// <paramref name="top"/>, adding its faces to the wetted perimeter. Roughness is carried from this section. Banks are
    /// clipped to the opening; reach lengths and loss coefficients are as given.
    /// </summary>
    public RiverSection Opening(string name, double leftAbutment, double rightAbutment, double top, IEnumerable<(double Station, double Width)> piers,
        double lengthLob, double lengthCh, double lengthRob, double contraction, double expansion, double? lowChord = null)
    {
        double NAt(double x)
        {
            for (int i = 0; i < _n.Length; i++) if (x <= _x[i + 1] + 1e-9) return _n[i];
            return _n[^1];
        }
        var pts = new List<(double X, double Z, double? N)>();
        double la = Math.Max(leftAbutment, _x[0]), ra = Math.Min(rightAbutment, _x[^1]);
        // At a vertical face the ground just inside the opening is the right-hand point (left abutment) or left-hand point (right abutment).
        double zl = Enumerable.Range(0, _x.Length).Where(i => Math.Abs(_x[i] - la) < 1e-9).Select(i => _z[i]).DefaultIfEmpty(Interp(la)).Last();
        pts.Add((la, Math.Max(top, zl), NAt(la)));
        pts.Add((la, zl, NAt(la + 1e-6)));
        var blocks = piers.Select(pp => (L: pp.Station - pp.Width / 2, R: pp.Station + pp.Width / 2)).Where(b => b.R > la && b.L < ra).OrderBy(b => b.L).ToList();
        var xs = _x.Where(x => x > la + 1e-9 && x < ra - 1e-9).ToList();
        foreach (var b in blocks) { xs.Add(Math.Max(b.L, la + 1e-6)); xs.Add(Math.Min(b.R, ra - 1e-6)); }
        xs = xs.Distinct().OrderBy(x => x).ToList();
        foreach (double x in xs)
        {
            var blk = blocks.FirstOrDefault(b => Math.Abs(x - Math.Max(b.L, la + 1e-6)) < 1e-9);
            var end = blocks.FirstOrDefault(b => Math.Abs(x - Math.Min(b.R, ra - 1e-6)) < 1e-9);
            if (blk != default) { pts.Add((x, Interp(x), NAt(x))); pts.Add((x, top, NAt(x))); continue; }
            if (end != default) { pts.Add((x, top, NAt(x))); pts.Add((x, Interp(x), NAt(x))); continue; }
            if (blocks.Any(b => x > b.L && x < b.R)) continue;
            pts.Add((x, Interp(x), NAt(x)));
        }
        double zr = Interp(ra);
        pts.Add((ra, zr, NAt(ra)));
        pts.Add((ra, Math.Max(top, zr), NAt(ra)));
        double lb = Math.Clamp(LeftBank, la, ra), rb = Math.Clamp(RightBank, la, ra);
        if (rb <= lb) { lb = la; rb = ra; }
        return new RiverSection(name, RiverStation, pts, lb, rb, _n[0], _n[0], _n[0], _k)
        {
            LengthLob = lengthLob, LengthCh = lengthCh, LengthRob = lengthRob, Contraction = contraction, Expansion = expansion, Lid = lowChord,
        };
    }

    /// <summary>A copy with other reach lengths (for splitting a reach at a bridge).</summary>
    public RiverSection WithLengths(double lob, double ch, double rob)
    {
        var pts = Enumerable.Range(0, _x.Length).Select(i => (_x[i], _z[i], i < _n.Length ? (double?)_n[i] : null)).ToList();
        return new RiverSection(Name, RiverStation, pts, LeftBank, RightBank, _n[0], _n[0], _n[0], _k, IneffectiveLeft, IneffectiveRight)
        {
            LengthLob = lob, LengthCh = ch, LengthRob = rob, Contraction = Contraction, Expansion = Expansion,
        };
    }

    /// <summary>
    /// The section with floodway encroachments (HEC-RAS encroachment stations): ground left of <paramref name="left"/> and
    /// right of <paramref name="right"/> is removed, so water there is held by vertical walls at the encroachment stations.
    /// Banks are kept inside the encroachments; roughness, reach lengths, coefficients and ineffective areas are carried over.
    /// </summary>
    public RiverSection Encroach(double left, double right)
    {
        left = Math.Max(left, _x[0]); right = Math.Min(right, _x[^1]);
        double NAt(double x)
        {
            for (int i = 0; i < _n.Length; i++) if (x <= _x[i + 1] + 1e-9) return _n[i];
            return _n[^1];
        }
        var pts = new List<(double X, double Z, double? N)> { (left, Interp(left), NAt(left + 1e-9)) };
        for (int i = 0; i < _x.Length; i++)
            if (_x[i] > left + 1e-9 && _x[i] < right - 1e-9) pts.Add((_x[i], _z[i], i < _n.Length ? _n[i] : null));
        pts.Add((right, Interp(right), null));
        double lb = Math.Clamp(LeftBank, left, right), rb = Math.Clamp(RightBank, left, right);
        return new RiverSection(Name, RiverStation, pts, lb, rb, _n[0], _n[0], _n[0], _k, IneffectiveLeft, IneffectiveRight)
        {
            LengthLob = LengthLob, LengthCh = LengthCh, LengthRob = LengthRob, Contraction = Contraction, Expansion = Expansion, Lid = Lid,
        };
    }

    /// <summary>First and last station of the section.</summary>
    public (double Left, double Right) Extent => (_x[0], _x[^1]);

    public double Interp(double x)
    {
        if (x <= _x[0]) return _z[0];
        for (int i = 1; i < _x.Length; i++) if (x <= _x[i]) return _z[i - 1] + (_z[i] - _z[i - 1]) * (x - _x[i - 1]) / (_x[i] - _x[i - 1]);
        return _z[^1];
    }

    public double LowerEndElevation => Math.Min(_z[0], _z[^1]);

    private bool Ineffective(int i, double ws)
    {
        double mid = 0.5 * (_x[i] + _x[i + 1]);
        if (IneffectiveLeft is { } l && mid < l.Station && ws < l.Elevation) return true;
        if (IneffectiveRight is { } r && mid > r.Station && ws < r.Elevation) return true;
        return false;
    }

    /// <summary>Hydraulic properties at water surface elevation <paramref name="ws"/>.</summary>
    public SectionHydraulics At(double ws)
    {
        // Wetted strips, grouped by (subsection, n) into conveyance segments; contiguous segments with the same n share one K.
        var sA = new double[3]; var sK = new double[3]; var sKa = new double[3];
        double area = 0, top = 0, moment = 0, topCh = 0;
        double gA = 0, gP = 0; int gSub = -1; double gN = double.NaN;
        void Flush()
        {
            if (gA > 0 && gSub >= 0)
            {
                double kk = _k / gN * gA * Math.Pow(gA / Math.Max(gP, 1e-12), 2.0 / 3);
                sA[gSub] += gA; sK[gSub] += kk; sKa[gSub] += kk * kk * kk / (gA * gA);
            }
            gA = 0; gP = 0; gSub = -1; gN = double.NaN;
        }
        bool wallL = ws > _z[0], wallR = ws > _z[^1];
        bool lidded = Lid is { } lidEl && ws > lidEl;
        double wsl = lidded ? Lid!.Value : ws;
        for (int i = 0; i < _n.Length; i++)
        {
            double d1 = wsl - _z[i], d2 = wsl - _z[i + 1], dx = _x[i + 1] - _x[i];
            if ((d1 <= 0 && d2 <= 0) || Ineffective(i, ws)) continue;   // dry ground or an island does not break a conveyance segment (HEC-RAS: breaks at n changes only)
            if (dx <= 0)
            {
                // Vertical face: wetted perimeter only.
                double wet = Math.Min(wsl, Math.Max(_z[i], _z[i + 1])) - Math.Min(_z[i], _z[i + 1]);
                if (gSub == _sub[i] && gN == _n[i]) gP += wet;
                else { Flush(); gSub = _sub[i]; gN = _n[i]; gP = wet; }
                continue;
            }
            double a, pw, t, mo;
            double len = Math.Sqrt(dx * dx + (_z[i + 1] - _z[i]) * (_z[i + 1] - _z[i]));
            if (d1 > 0 && d2 > 0) { a = 0.5 * (d1 + d2) * dx; pw = len; t = dx; mo = dx * (d1 * d1 + d1 * d2 + d2 * d2) / 6; }
            else
            {
                double dp = Math.Max(d1, d2), dn = Math.Min(d1, d2), f = dp / (dp - dn);
                a = 0.5 * dp * f * dx; pw = f * len; t = f * dx; mo = f * dx * dp * dp / 6;
            }
            if (i == 0 && wallL) pw += d1;
            if (i == _n.Length - 1 && wallR) pw += d2;
            if (lidded) { pw += t; t = 0; mo += a * (ws - wsl); }   // the deck soffit is wetted; no free surface
            if (_sub[i] != gSub || _n[i] != gN) Flush();
            gSub = _sub[i]; gN = _n[i]; gA += a; gP += pw;
            area += a; top += t; moment += mo;
            if (_sub[i] == 1) topCh += t;
        }
        Flush();
        double kt = sK.Sum();
        double alpha = kt > 0 ? sKa.Sum() / (kt * kt * kt / (area * area)) : 1;
        return new(ws, area, top, kt, alpha, sA[0], sA[1], sA[2], sK[0], sK[1], sK[2], topCh, moment, wallL, wallR);
    }

    /// <summary>Specific energy WS + αV²/2g.</summary>
    public double Energy(double ws, double q, double g) { var h = At(ws); return h.Area > 0 ? ws + h.VelocityHead(q, g) : double.PositiveInfinity; }

    /// <summary>Specific force Q²/(gA) + A·ȳ (per unit weight), for choosing between sub- and supercritical solutions.</summary>
    public double SpecificForce(double ws, double q, double g) { var h = At(ws); return h.Area > 0 ? q * q / (g * h.Area) + h.Moment : double.PositiveInfinity; }

    /// <summary>
    /// Critical water surface: the elevation of minimum specific energy, found by a scan of 200 levels from the invert to
    /// twice the section relief and refined by golden section. The lowest local minimum is taken (HEC-RAS reports when
    /// there are several).
    /// </summary>
    public (double Ws, int Minima) Critical(double q, double g)
    {
        double lo = MinElevation + 1e-4, top = Math.Max(_z.Max(), MinElevation + 1) ;
        double hi = top + (top - MinElevation);
        int n = 400;
        var e = new double[n + 1];
        for (int i = 0; i <= n; i++) e[i] = Energy(lo + (hi - lo) * i / n, q, g);
        int minima = 0, first = -1;
        for (int i = 1; i < n; i++) if (e[i] <= e[i - 1] && e[i] < e[i + 1]) { minima++; if (first < 0) first = i; }
        if (first < 0) first = Array.IndexOf(e, e.Min());
        double a = lo + (hi - lo) * Math.Max(0, first - 1) / n, b = lo + (hi - lo) * Math.Min(n, first + 1) / n;
        const double gr = 0.6180339887498949;
        double c = b - gr * (b - a), d = a + gr * (b - a);
        for (int it = 0; it < 100 && b - a > 1e-7; it++)
        {
            if (Energy(c, q, g) < Energy(d, q, g)) b = d; else a = c;
            c = b - gr * (b - a); d = a + gr * (b - a);
        }
        return (0.5 * (a + b), Math.Max(1, minima));
    }

    /// <summary>Water surface for which the conveyance carries Q at friction slope S (normal depth), by bisection.</summary>
    public double Normal(double q, double slope)
    {
        double lo = MinElevation + 1e-6, hi = MinElevation + 1;
        for (int i = 0; i < 60 && At(hi).Conveyance * Math.Sqrt(slope) < q; i++) hi = MinElevation + 2 * (hi - MinElevation);
        for (int i = 0; i < 200 && hi - lo > 1e-8; i++)
        {
            double m = 0.5 * (lo + hi);
            if (At(m).Conveyance * Math.Sqrt(slope) < q) lo = m; else hi = m;
        }
        return 0.5 * (lo + hi);
    }
}
