using System.Globalization;
using MasterFlow.Application.Feasibility;
using MasterFlow.Application.Hydrology;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Site feasibility in ten minutes: from a parcel boundary (entered, or a Pennington County PIN), pull floodplain,
/// soils, elevation and rainfall from free public services (or take them as entered), apply the Rapid City zoning
/// district, and return a lot yield, a drainage concept (pre/post peaks, detention and WQCV, pond footprint), a rough
/// grading picture (slope classes, fall direction, balanced-pad earthwork), an order-of-magnitude cost range and a
/// go / no-go list of constraints. See docs/tools/site-feasibility.md; decisions D-200 to D-209.
/// </summary>
public sealed class SiteFeasibilityTool : IDesignTool
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double Acre = SiteGeometry.SqFtPerAcre;

    public string Id => "site-feasibility";
    public string Name => "Site feasibility (lot yield, drainage concept, cost range, go/no-go)";
    public string Description => "Quick go/no-go for a parcel: area, FEMA floodplain, NRCS soils, USGS slopes, Rapid City zoning yield, TR-55 pre/post peaks with detention and WQCV, rough earthwork and a cost range. Data are fetched from public services when fetch = yes (only coordinates or the PIN are sent; results are cached) or entered by hand, so it also works offline.";
    public bool UsesNetwork => false;

    public string SampleInput(DesignToolContext context) => """
        # Site feasibility. fetch = yes pulls the parcel (by PIN), FEMA flood zones, NRCS soils, USGS 3DEP elevations and
        # NOAA Atlas 14 rainfall for the boundary; only coordinates or the PIN are sent, and every answer is cached on this
        # computer. Any section entered below is used instead of fetching (and is how the tool runs offline).
        site name = Sample 40-acre parcel, west Rapid City
        fetch = no
        # parcel = 3712345678          # Pennington County PIN (with fetch = yes, replaces [Boundary])
        zoning = LDR-1                 # Rapid City district: GAD, PF, LDR-1, LDR-2, MDR, HDR, GC, LI
        housing = single-family        # single-family, two-family or multi-family
        lot area = 7500                # ft²; target lot (never below the district minimum)
        row width = 52                 # ft, local street right-of-way
        open space = 5                 # % of the site kept as open space or parkland
        existing cover = pasture fair  # TR-55 cover before development
        mean slope = 7.2               # % (used when no elevations are fetched)
        relief = 92                    # ft, high point minus low point
        flow length = 1750             # ft, hydraulic length for the lag method (default: the site diagonal)
        pond depth = 4                 # ft of storage depth for the pond footprint
        [Boundary]
        Latitude, Longitude
        44.05000, -103.30000
        44.05000, -103.29496
        44.05363, -103.29496
        44.05363, -103.30000
        [Flood zones]
        Zone, Percent
        AE, 6
        AE floodway, 2
        X (0.2 % annual chance), 4
        [Soils]
        Map unit, Name, HSG, Percent
        NuB, Nunn clay loam 0-6 % slopes, C, 55
        PeC, Pierre clay 6-9 % slopes, D, 30
        HbA, Haverson loam 0-2 % slopes, B, 15
        [Slope classes]
        Range (%), Percent
        0-5, 44
        5-15, 41
        15-25, 11
        >25, 4
        [Utilities]
        Utility, Distance (ft), Size (in)
        water, 350, 8
        sanitary sewer, 900, 8
        [Unit costs]
        Item, Price
        # Order-of-magnitude placeholders: replace with your own prices.
        street (per LF), 450
        """;

    private sealed record Flag(string Level, string Constraint, string Value, string Effect);

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title0 = "Site feasibility";
        if (context.Units != UnitSystem.USCustomary)
            return ToolResult.Failed(title0, CalcMessage.Error("E-SF-001", "Site feasibility works in U.S. customary units (ft, ac, in); switch the project units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        string siteName = p.Text("site name") ?? "Site";
        string title = $"Site feasibility — {siteName}";
        bool fetch = p.Flag("fetch", false), refresh = p.Flag("refresh", false);
        var sources = new List<IReadOnlyList<object?>>();
        void Source(string what, string src, string status, string detail = "") => sources.Add([what, src, status, detail]);

        string? Fetch(string what, string url, string? body, out bool cached)
        {
            var (t, err, c) = PublicSiteData.Get(url, body, refresh);
            cached = c;
            if (t is null) msgs.Add(CalcMessage.Warning("W-SF-010", $"{what}: the public service could not be reached ({err}). Enter the data by hand or try again online."));
            return t;
        }

        // ---------- Boundary ----------
        SiteGeometry? site = null;
        var ring = new List<GeoPoint>();
        foreach (var row in p.Rows("Boundary"))
        {
            var lat = p.Cell(row, 0, "latitude", "Boundary"); var lon = p.Cell(row, 1, "longitude", "Boundary");
            if (lat is { } a && lon is { } b) ring.Add(new GeoPoint(a, b));
        }
        double? recordedAcres = null;
        if (ring.Count >= 3) Source("Boundary", "entered", "used", $"{ring.Count} vertices");
        else if (p.Text("parcel") is { } pin)
        {
            if (!fetch) return ToolResult.Failed(title, CalcMessage.Error("E-SF-002", "A parcel PIN needs fetch = yes (or enter the [Boundary] vertices)."));
            var t = Fetch("Parcel", PublicSiteData.ParcelUrl(pin), null, out bool c);
            if (t is null) return ToolResult.Failed(title, [.. msgs, CalcMessage.Error("E-SF-003", "No boundary: the parcel service could not be reached and no [Boundary] was entered.")]);
            var (r, ac, err) = PublicSiteData.ParseParcel(t);
            if (r is null) return ToolResult.Failed(title, CalcMessage.Error("E-SF-004", $"Parcel {pin}: {err}."));
            ring = r; recordedAcres = ac;
            Source("Boundary", "Pennington County tax parcels (Rapid City MSP service)", c ? "cached" : "fetched", $"PIN {pin}, {ring.Count} vertices");
        }
        else return ToolResult.Failed(title, [.. p.Errors, CalcMessage.Error("E-SF-005", "Enter the site boundary under [Boundary] (Latitude, Longitude; three or more vertices) or a parcel PIN with fetch = yes.")]);
        if (ring.Any(g => Math.Abs(g.Lat) > 90 || Math.Abs(g.Lon) > 180))
            return ToolResult.Failed(title, CalcMessage.Error("E-SF-006", "A boundary vertex is not a latitude/longitude in decimal degrees."));
        site = new SiteGeometry(ring);
        double A = site.Acres;
        if (A < 0.05) return ToolResult.Failed(title, CalcMessage.Error("E-SF-007", $"The boundary encloses only {A:F3} ac; check the vertex order and values."));
        if (recordedAcres is { } ra && Math.Abs(ra - A) > 0.05 * A)
            msgs.Add(CalcMessage.Warning("W-SF-011", $"The county records {ra:F2} ac for the parcel; the boundary measures {A:F2} ac."));

        // Sample grid shared by flood and slope measurement (D-203).
        var (grid, cellArea) = site.SampleGrid(60);

        // ---------- Flood ----------
        var floodShares = new Dictionary<string, double>();
        double floodwayPct = double.NaN, sfhaPct = double.NaN;
        bool[]? inSfha = null, inFloodway = null;
        List<FloodArea>? floodAreas = null;
        if (p.HasSection("Flood zones"))
        {
            foreach (var row in p.Rows("Flood zones"))
                if (p.Cell(row, 1, "percent", row[0]) is { } pc) floodShares[row[0]] = floodShares.GetValueOrDefault(row[0]) + pc;
            floodwayPct = floodShares.Where(k => k.Key.Contains("floodway", StringComparison.OrdinalIgnoreCase)).Sum(k => k.Value);
            sfhaPct = floodShares.Where(k => IsSfhaLabel(k.Key)).Sum(k => k.Value);
            Source("Floodplain", "entered", "used", floodShares.Count == 0 ? "none on site" : string.Join(", ", floodShares.Select(k => $"{k.Key} {k.Value:0.#} %")));
        }
        else if (fetch)
        {
            string? t = Fetch("FEMA flood zones", PublicSiteData.FloodUrl(PublicSiteData.FemaFloodLayer, site.GeoBounds), null, out bool c);
            string src = "FEMA National Flood Hazard Layer";
            var parsed = t is null ? (null, "not reached") : PublicSiteData.ParseFlood(t);
            if (parsed.Item1 is null)
            {
                t = Fetch("Rapid City flood layer", PublicSiteData.FloodUrl(PublicSiteData.RapidCityFloodLayer, site.GeoBounds), null, out c);
                src = "FEMA flood plain (Rapid City MSP copy)";
                parsed = t is null ? (null, "not reached") : PublicSiteData.ParseFlood(t);
            }
            if (parsed.Item1 is { } areas)
            {
                floodAreas = areas;
                var local = areas.Select(a => (Area: a, Rings: a.Rings.Select(r => r.Select(site.ToLocal).ToList()).ToList())).ToList();
                inSfha = new bool[grid.Count]; inFloodway = new bool[grid.Count];
                var counts = new Dictionary<string, int>();
                for (int k = 0; k < grid.Count; k++)
                {
                    var hit = local.Where(a => InRings(a.Rings, grid[k].X, grid[k].Y)).Select(a => a.Area).ToList();
                    // Most restrictive zone at the point: floodway, then SFHA, then other.
                    var z = hit.OrderByDescending(a => a.IsFloodway).ThenByDescending(a => a.Sfha).FirstOrDefault();
                    if (z is null) continue;
                    counts[z.Label] = counts.GetValueOrDefault(z.Label) + 1;
                    inSfha[k] = z.Sfha; inFloodway[k] = z.IsFloodway;
                }
                foreach (var kv in counts) floodShares[kv.Key] = 100.0 * kv.Value / grid.Count;
                floodwayPct = 100.0 * inFloodway.Count(x => x) / grid.Count;
                sfhaPct = 100.0 * inSfha.Count(x => x) / grid.Count;
                var bfes = areas.Where(a => a.Bfe is not null).Select(a => a.Bfe!.Value).ToList();
                Source("Floodplain", src, c ? "cached" : "fetched", (counts.Count == 0 ? "no mapped flood zone on the site" : string.Join(", ", floodShares.Select(k => $"{k.Key} {k.Value:0.#} %")))
                    + (bfes.Count > 0 ? $"; static BFE {bfes.Min():0.#}–{bfes.Max():0.#} ft" : ""));
            }
            else Source("Floodplain", src, "not available", parsed.Item2 ?? "");
        }
        else Source("Floodplain", "—", "not entered", "enter [Flood zones] or set fetch = yes");
        if (double.IsNaN(sfhaPct)) msgs.Add(CalcMessage.Warning("W-SF-012", "Floodplain unknown: no flood zones were entered or fetched, so no area was excluded for floodplain. Check the FEMA FIRM."));

        // ---------- Elevation and slopes ----------
        double steepLimit = p.Number("steep slope", 25);
        double[] classBreaks = [5, 15, 25];
        var slopeClasses = new List<(string Range, double Lo, double Pct)>();
        double meanSlope = p.Number("mean slope", double.NaN), relief = p.Number("relief", double.NaN), steepPct = double.NaN;
        string fall = "—";
        (double X, double Y)? lowPoint = null;
        double lowElev = double.NaN, highElev = double.NaN;
        bool[]? inSteep = null;
        ElevationGrid? eg = null;
        if (p.HasSection("Slope classes"))
        {
            foreach (var row in p.Rows("Slope classes"))
                if (p.Cell(row, 1, "percent", row[0]) is { } pc) slopeClasses.Add((row[0], LowerBound(row[0]), pc));
            steepPct = slopeClasses.Where(c => c.Lo >= steepLimit - 1e-9).Sum(c => c.Pct);
            if (double.IsNaN(meanSlope)) meanSlope = slopeClasses.Sum(c => c.Pct * Mid(c.Range)) / Math.Max(1e-9, slopeClasses.Sum(c => c.Pct));
            Source("Slopes", "entered", "used", $"mean {meanSlope:0.#} %, {steepPct:0.#} % of the site steeper than {steepLimit:0} %");
        }
        else if (fetch)
        {
            int n = (int)Math.Clamp(p.Number("elevation grid", 15), 5, 30);
            var nodes = ElevationGrid.Nodes(site, n, n);
            var t = Fetch("USGS 3DEP elevations", PublicSiteData.ElevationUrl(nodes.Select(q => site.ToGeo(q.X, q.Y))), null, out bool c);
            var (z, err) = t is null ? (null, "not reached") : PublicSiteData.ParseElevations(t, nodes.Count);
            if (z is not null)
            {
                var b = site.Bounds;
                var zz = new double[n, n];
                foreach (var (q, k) in nodes.Select((q, k) => (q, k))) zz[q.I, q.J] = z[k];
                eg = new ElevationGrid(n, n, b.MinX, b.MinY, (b.MaxX - b.MinX) / (n - 1), (b.MaxY - b.MinY) / (n - 1), zz);
                var inside = nodes.Where(q => site.Contains(q.X, q.Y) && double.IsFinite(zz[q.I, q.J])).ToList();
                if (inside.Count < 4) inside = nodes.Where(q => double.IsFinite(zz[q.I, q.J])).ToList();
                var s = inside.Select(q => 100 * eg.Slope(q.I, q.J)).Where(double.IsFinite).ToList();
                meanSlope = s.Average();
                highElev = inside.Max(q => zz[q.I, q.J]); lowElev = inside.Min(q => zz[q.I, q.J]); relief = highElev - lowElev;
                var lo = inside.OrderBy(q => zz[q.I, q.J]).First(); lowPoint = (lo.X, lo.Y);
                var (bx, cy) = eg.Plane(site); fall = ElevationGrid.FallDirection(bx, cy);
                double lb = 0;
                foreach (var hi in classBreaks.Append(double.PositiveInfinity))
                {
                    double pct = 100.0 * s.Count(v => v >= lb && v < hi) / s.Count;
                    slopeClasses.Add((double.IsInfinity(hi) ? $">{lb:0}" : $"{lb:0}-{hi:0}", lb, pct));
                    lb = hi;
                }
                steepPct = 100.0 * s.Count(v => v >= steepLimit) / s.Count;
                // Steep share at each flood/yield sample point (nearest elevation node) for the exclusion union.
                inSteep = grid.Select(g => { int i = (int)Math.Round((g.X - b.MinX) / eg.Dx), j = (int)Math.Round((g.Y - b.MinY) / eg.Dy); return 100 * eg.Slope(Math.Clamp(i, 0, n - 1), Math.Clamp(j, 0, n - 1)) >= steepLimit; }).ToArray();
                Source("Elevation", "USGS 3DEP (1/3 arc-second DEM)", c ? "cached" : "fetched", $"{n} × {n} grid, {(b.MaxX - b.MinX) / (n - 1):0} ft × {(b.MaxY - b.MinY) / (n - 1):0} ft spacing; elevations {lowElev:0.#}–{highElev:0.#} ft");
            }
            else Source("Elevation", "USGS 3DEP", "not available", err ?? "");
        }
        if (slopeClasses.Count == 0 && double.IsFinite(meanSlope)) Source("Slopes", "entered", "used", $"mean slope {meanSlope:0.#} % only (no slope classes)");
        if (!double.IsFinite(meanSlope))
            return ToolResult.Failed(title, [.. p.Errors, .. msgs, CalcMessage.Error("E-SF-008", "Slopes unknown: enter mean slope (and [Slope classes]) or set fetch = yes for USGS elevations.")]);
        if (double.IsNaN(steepPct)) { steepPct = 0; msgs.Add(CalcMessage.Warning("W-SF-013", "No slope classes: the share of steep ground is unknown and nothing was excluded for slope.")); }

        // ---------- Soils ----------
        var soils = new List<SoilUnit>();
        if (p.HasSection("Soils"))
        {
            foreach (var row in p.Rows("Soils"))
                if (p.Cell(row, 3, "percent", row[0]) is { } pc) soils.Add(new SoilUnit(row[0], row.Length > 1 ? row[1] : "", row.Length > 2 ? row[2] : "", pc / 100));
            double tot = soils.Sum(s => s.Fraction);
            if (tot > 0 && Math.Abs(tot - 1) > 0.02) { msgs.Add(CalcMessage.Warning("W-SF-014", $"Soil percentages add to {tot * 100:0.#} %; they were scaled to 100 %.")); soils = soils.Select(s => s with { Fraction = s.Fraction / tot }).ToList(); }
            Source("Soils", "entered", "used", $"{soils.Count} map units");
        }
        else if (fetch)
        {
            var t = Fetch("NRCS soils", PublicSiteData.SoilDataAccess, PublicSiteData.SoilsBody(site.Wkt()), out bool c);
            var (u, err) = t is null ? (null, "not reached") : PublicSiteData.ParseSoils(t);
            if (u is not null) { soils = u; Source("Soils", "NRCS SSURGO (Soil Data Access)", c ? "cached" : "fetched", $"{u.Count} map units"); }
            else Source("Soils", "NRCS SSURGO", "not available", err ?? "");
        }
        string? assumeHsg = p.Text("assume hsg")?.ToUpperInvariant();
        if (soils.Count == 0 && assumeHsg is not null) { soils.Add(new SoilUnit("—", "assumed", assumeHsg, 1)); Source("Soils", "assumed (assume hsg)", "used", $"HSG {assumeHsg}"); }
        // Dual groups (C/D): the first letter is the drained condition, which is what a developed site has (D-204).
        var hsgShare = new Dictionary<char, double>();
        foreach (var s in soils)
        {
            char g = s.Hsg.Trim().ToUpperInvariant().FirstOrDefault(ch => ch is 'A' or 'B' or 'C' or 'D');
            if (g == default) { msgs.Add(CalcMessage.Warning("W-SF-015", $"Soil {s.MapUnit} has no hydrologic soil group; HSG D was used for it (most runoff).")); g = 'D'; }
            hsgShare[g] = hsgShare.GetValueOrDefault(g) + s.Fraction;
        }

        // ---------- Zoning ----------
        string zcode = p.Text("zoning") ?? "";
        var zone = RapidCityZoning.Find(zcode);
        if (zone is null)
            return ToolResult.Failed(title, CalcMessage.Error("E-SF-009", $"Zoning \"{zcode}\" is not in the Rapid City table. Use one of: {string.Join(", ", RapidCityZoning.Districts.Select(d => d.Code))}."));
        if (zone.Verify is not null) msgs.Add(CalcMessage.Warning("W-SF-016", $"{zone.Code}: {zone.Verify} Check RCMC {zone.Section}."));
        msgs.Add(CalcMessage.Info("I-SF-001", "Zoning standards are from Rapid City Municipal Code Title 17 as published; the zoning map is not a public GIS layer, so the district is as entered. Confirm both with Community Development."));

        // ---------- Utilities ----------
        var utils = new List<(string Name, double Dist, double? Size)>();
        foreach (var row in p.Rows("Utilities"))
            if (p.Cell(row, 1, "distance", row[0]) is { } d) utils.Add((row[0], d, p.Cell(row, 2, "size", row[0])));
        double? waterDist = utils.Where(u => u.Name.Contains("water", StringComparison.OrdinalIgnoreCase)).Select(u => (double?)u.Dist).Min();
        double? sewerDist = utils.Where(u => u.Name.Contains("sewer", StringComparison.OrdinalIgnoreCase) && !u.Name.Contains("storm", StringComparison.OrdinalIgnoreCase) || u.Name.Contains("sanitary", StringComparison.OrdinalIgnoreCase)).Select(u => (double?)u.Dist).Min();
        double? stormDist = utils.Where(u => u.Name.Contains("storm", StringComparison.OrdinalIgnoreCase)).Select(u => (double?)u.Dist).Min();
        Source("Utilities", utils.Count > 0 ? "entered" : "—", utils.Count > 0 ? "used" : "not entered", "Rapid City does not publish utility mains as a public GIS layer; enter distances from the City's utility maps");
        bool sewer = sewerDist is not null;

        // ---------- Rainfall ----------
        int[] storms = [2, 10, 100];
        var idcm = storms.ToDictionary(y => y, y => DesignStorms.RapidCityDepths[y][^1]);
        Dictionary<int, Dictionary<string, double>>? atlas = null;
        if (fetch || p.Flag("atlas 14", false))
        {
            var c0 = site.Centroid;
            var t = Fetch("NOAA Atlas 14", PublicSiteData.PfdsUrl(c0.Lat, c0.Lon), null, out bool c);
            var (d, err) = t is null ? (null, "not reached") : PublicSiteData.ParsePfds(t);
            atlas = d;
            Source("Rainfall (comparison)", "NOAA Atlas 14 PFDS", d is null ? "not available" : c ? "cached" : "fetched", d is null ? err ?? "" : $"{c0.Lat:F4}, {c0.Lon:F4}");
        }
        bool useAtlas = string.Equals(p.Text("rainfall"), "atlas14", StringComparison.OrdinalIgnoreCase) || string.Equals(p.Text("rainfall"), "atlas 14", StringComparison.OrdinalIgnoreCase);
        var design = new Dictionary<int, double>(idcm);
        if (useAtlas)
        {
            if (atlas is null) msgs.Add(CalcMessage.Warning("W-SF-017", "rainfall = atlas14 but no Atlas 14 table is available; IDCM Table 4-2 was used."));
            else { foreach (var y in storms) if (atlas.TryGetValue(y, out var dd) && dd.TryGetValue("24-hr", out var v)) design[y] = v; msgs.Add(CalcMessage.Warning("W-SF-018", "Atlas 14 depths used for design: the Rapid City IDCM (Table 4-2) governs a submittal.")); }
        }
        Source("Rainfall (design)", useAtlas && atlas is not null ? "NOAA Atlas 14" : "Rapid City IDCM 2012 Table 4-2 (24-hr)", "used", string.Join(", ", storms.Select(y => $"{y}-yr {design[y]:0.00} in")));

        // ---------- Exclusions (D-205) ----------
        bool exFlood = p.Flag("exclude floodplain", true), exSteep = p.Flag("exclude steep", true);
        double otherEx = p.Number("other exclusions", 0);   // ac (easements, wetlands, existing ROW)
        double exPct;
        string exHow;
        if (inSfha is not null && inSteep is not null)
        {
            int cnt = 0;
            for (int k = 0; k < grid.Count; k++) if (inFloodway![k] || (exFlood && inSfha[k]) || (exSteep && inSteep[k])) cnt++;
            exPct = 100.0 * cnt / grid.Count; exHow = "union of the mapped areas";
        }
        else
        {
            exPct = Math.Min(100, (double.IsNaN(floodwayPct) ? 0 : floodwayPct) + (exFlood && !double.IsNaN(sfhaPct) ? Math.Max(0, sfhaPct - (double.IsNaN(floodwayPct) ? 0 : floodwayPct)) : 0) + (exSteep ? steepPct : 0));
            exHow = "sum of the entered shares (overlap not known, conservative)";
        }
        double excludedAc = exPct / 100 * A + otherEx;
        double openPct = p.Number("open space", 5);

        // ---------- Lot geometry ----------
        string housing = (p.Text("housing") ?? (zone.Use == ZoningUse.Residential ? "single-family" : "")).ToLowerInvariant();
        bool residential = zone.Use is ZoningUse.Residential or ZoningUse.Agricultural;
        double minLot = (sewer ? zone.MinLot : zone.MinLotNoSewer) ?? 0;
        if (!sewer && residential && zone.MinLotNoSewer > zone.MinLot)
            msgs.Add(CalcMessage.Warning("W-SF-019", $"No sanitary sewer entered under [Utilities]: lots sized for on-site wastewater ({zone.MinLotNoSewer / Acre:0.##} ac minimum, RCMC {zone.Section}(F))."));
        double lotArea = Math.Max(minLot, p.Number("lot area", minLot));
        if (lotArea <= 0) lotArea = p.Number("building lot", 2) * Acre;
        double lotWidth = Math.Max(zone.MinWidth ?? 0, p.Number("lot width", zone.MinWidth ?? Math.Sqrt(lotArea)));
        double rowWidth = p.Number("row width", 52);
        double efficiency = p.Number("lot efficiency", 90) / 100;   // irregular geometry, corner lots, remnants

        // ---------- Developed cover (TR-55 Table 2-2a) ----------
        var existing = Tr55Tables.Find(p.Text("existing cover") ?? "pasture fair");
        if (existing is null) return ToolResult.Failed(title, CalcMessage.Error("E-SF-010", $"Existing cover \"{p.Text("existing cover")}\" is not a TR-55 cover key (see the TR-55 tool, e.g. pasture fair, brush good, row crops sr good)."));
        string devKey; double imp;
        if (zone.Use == ZoningUse.Commercial) (devKey, imp) = ("commercial", 85);
        else if (zone.Use == ZoningUse.Industrial) (devKey, imp) = ("industrial", 72);
        else if (housing.StartsWith("multi") || housing.StartsWith("two")) (devKey, imp) = ("residential 1/8", 65);
        else (devKey, imp) = (lotArea / Acre) switch
        {
            <= 0.125 + 1e-9 => ("residential 1/8", 65.0), <= 0.25 + 1e-9 => ("residential 1/4", 38.0), <= 0.34 => ("residential 1/3", 30.0),
            <= 0.5 + 1e-9 => ("residential 1/2", 25.0), <= 1 + 1e-9 => ("residential 1", 20.0), _ => ("residential 2", 12.0),
        };
        if (p.OptionalNumber("impervious") is { } impIn) imp = impIn;
        var developed = Tr55Tables.Find(devKey)!;
        var openCover = Tr55Tables.Find("open space good")!;

        // ---------- Hydrology and pond (iterate: the pond takes land, which changes the developed share) ----------
        double flowLen = p.Number("flow length", site.DiagonalFt);
        double pondDepth = p.Number("pond depth", 4), pondFactor = p.Number("pond footprint factor", 1.3);
        bool wq = p.Flag("water quality", true);
        double drainHours = p.Number("wq drain time", 40);
        double aCoef = drainHours switch { <= 6 => 0.7, <= 12 => 0.8, <= 24 => 0.9, _ => 1.0 };   // SWQM §2.2.2
        bool hydro = hsgShare.Count > 0;
        if (!hydro) msgs.Add(CalcMessage.Warning("W-SF-020", "Soils unknown: runoff, detention and the pond footprint were not computed. Enter [Soils], assume hsg, or set fetch = yes."));
        var storm = new List<(int Y, double P, double Qpre, double Qpost, double Tpre, double Tpost, double qPre, double qPost, double Vs)>();
        double pondAc = hydro ? 0.04 * A : 0, cnPre = double.NaN, cnPost = double.NaN, devFrac = 0, vDet = 0, wqcv = 0, pondVol = 0;
        if (hydro)
        {
            cnPre = hsgShare.Sum(k => k.Value * existing.For(k.Key));
            if (double.IsNaN(cnPre)) return ToolResult.Failed(title, CalcMessage.Error("E-SF-011", $"TR-55 gives no curve number for \"{existing.Key}\" on one of the site's soil groups."));
            for (int it = 0; it < 4; it++)
            {
                double devAc = Math.Max(0, A - excludedAc - pondAc - openPct / 100 * A);
                devFrac = devAc / A;
                double openFrac = (pondAc + openPct / 100 * A) / A, natFrac = Math.Max(0, 1 - devFrac - openFrac);
                cnPost = hsgShare.Sum(k => k.Value * (devFrac * developed.For(k.Key) + openFrac * openCover.For(k.Key) + natFrac * existing.For(k.Key)));
                storm.Clear();
                foreach (var y in storms)
                {
                    double P = design[y];
                    var (qpre, tpre, upre) = Peak(P, cnPre, flowLen, meanSlope, A);
                    var (qpost, tpost, upost) = Peak(P, cnPost, flowLen, meanSlope, A);
                    double r = upost > 0 ? upre / upost : 1;
                    double vs = r >= 1 ? 0 : StorageRatio(Math.Max(0.1, r)) * qpost / 12 * A;   // ac-ft
                    storm.Add((y, P, qpre, qpost, tpre, tpost, upre, upost, vs));
                }
                vDet = storm.Max(s => s.Vs);
                double I = imp / 100 * devFrac;
                wqcv = wq ? 1.16 * aCoef * (0.91 * I * I * I - 1.191 * I * I + 0.78 * I) / 12 * A : 0;   // ac-ft
                pondVol = vDet + wqcv;
                pondAc = pondVol * Acre / Math.Max(0.5, pondDepth) * pondFactor / Acre;
            }
            if (storm.Any(s => s.qPost > 0 && s.qPre / s.qPost < 0.1))
                msgs.Add(CalcMessage.Warning("W-SF-021", "Release over inflow below 0.1: TR-55 Figure 6-1 is outside its range there (0.1 was used); route the pond with the detention tool."));
            if (storm.Any(s => s.Tpre <= 0.1 + 1e-9 || s.Tpost <= 0.1 + 1e-9))
                msgs.Add(CalcMessage.Info("I-SF-002", "Tc below 0.1 h was set to 0.1 h, as TR-55 directs."));
        }
        double devAcres = Math.Max(0, A - excludedAc - pondAc - openPct / 100 * A);

        // ---------- Yield (D-206) ----------
        double perLot = lotArea / efficiency + lotWidth / 2 * rowWidth;
        int lots = 0, units = 0;
        double streetLf, buildableAc = devAcres, coverageAc = 0;
        string yieldNote;
        if (residential && housing.StartsWith("multi"))
        {
            if (zone.MultiPerUnit is null) return ToolResult.Failed(title, CalcMessage.Error("E-SF-012", $"{zone.Code} has no multi-family density in the table; use MDR or HDR."));
            double rowShare = p.Number("multi row percent", 10) / 100, bldgLot = p.Number("building lot", 2) * Acre;
            double net = devAcres * Acre * (1 - rowShare);
            lots = (int)Math.Max(0, Math.Ceiling(net / bldgLot));
            units = (int)Math.Max(0, Math.Floor((net - lots * zone.MultiBase!.Value) / zone.MultiPerUnit.Value));
            streetLf = devAcres * Acre * rowShare / rowWidth;
            yieldNote = $"{zone.MultiBase:0} ft² + {zone.MultiPerUnit:0} ft² per unit on {lots} building lot(s) of about {bldgLot / Acre:0.#} ac; {rowShare * 100:0} % of the developable land to streets";
        }
        else if (residential)
        {
            lots = (int)Math.Floor(devAcres * Acre / perLot);
            units = housing.StartsWith("two") ? 2 * lots : lots;
            if (housing.StartsWith("two") && zone.Code is not ("LDR-2" or "MDR" or "HDR"))
                msgs.Add(CalcMessage.Warning("W-SF-022", $"Two-family dwellings may not be a permitted use in {zone.Code}; check RCMC."));
            streetLf = lots * lotWidth / 2;
            yieldNote = $"each lot takes {lotArea:0} ft² / {efficiency:0.##} efficiency + half its {lotWidth:0} ft frontage × {rowWidth:0} ft ROW = {perLot:0} ft²";
        }
        else
        {
            double rowShare = p.Number("multi row percent", 10) / 100;
            buildableAc = devAcres * (1 - rowShare);
            coverageAc = buildableAc * zone.MaxCoverage;
            lots = zone.MinLot is { } ml ? (int)Math.Floor(buildableAc * Acre / Math.Max(ml, lotArea)) : 1;
            streetLf = devAcres * Acre * rowShare / rowWidth;
            yieldNote = $"{buildableAc:0.##} ac buildable after {rowShare * 100:0} % to streets; {zone.MaxCoverage * 100:0} % maximum coverage gives {coverageAc:0.##} ac of building and paving";
        }
        if (p.OptionalNumber("street length") is { } sl) streetLf = sl;

        // ---------- Earthwork (balanced pads on a plane, D-207) ----------
        double lotDepth = residential && !housing.StartsWith("multi") ? lotArea / lotWidth : Math.Sqrt(p.Number("building lot", 2) * Acre);
        double disturbedAc = devAcres + pondAc;
        double cutCy = disturbedAc * Acre * (meanSlope / 100) * lotDepth / 8 / 27 + pondVol * Acre / 27 * (hydro ? 1 : 0);
        double retaining = p.Number("retaining", 0);

        // ---------- Costs (D-208) ----------
        var price = DefaultPrices();
        foreach (var row in p.Rows("Unit costs"))
            if (row.Length > 1 && p.Cell(row, 1, "price", row[0]) is { } v) price[Key(row[0])] = v;
        double Pr(string k) => price.GetValueOrDefault(Key(k));
        int count = residential ? Math.Max(units, lots) : lots;
        var cost = new List<(string Item, double Qty, string Unit, double Price)>
        {
            ("Local street: pavement, curb and gutter, base", streetLf, "LF", Pr("street (per LF)")),
            ("Water main in streets", streetLf, "LF", Pr("water main (per LF)")),
            ("Sanitary sewer in streets", sewer ? streetLf : 0, "LF", Pr("sanitary sewer (per LF)")),
            ("Storm sewer", streetLf * p.Number("storm sewer ratio", 0.5), "LF", Pr("storm sewer (per LF)")),
            ("Water and sewer services", residential ? count : lots, "EA", Pr("services (each)")),
            ("Off-site water main extension", waterDist ?? 0, "LF", Pr("water extension (per LF)")),
            ("Off-site sanitary sewer extension", sewerDist ?? 0, "LF", Pr("sewer extension (per LF)")),
            ("Detention / water quality pond excavation", hydro ? pondVol * Acre / 27 : 0, "CY", Pr("pond excavation (per CY)")),
            ("Pond outlet structure and spillway", hydro && pondVol > 0 ? 1 : 0, "LS", Pr("pond outlet (each)")),
            ("Mass grading (balanced pads)", cutCy, "CY", Pr("earthwork (per CY)")),
            ("Erosion and sediment control", disturbedAc, "AC", Pr("erosion control (per AC)")),
            ("Retaining walls", retaining, "SF face", Pr("retaining wall (per SF)")),
        };
        if (!sewer && residential) cost.Add(("On-site wastewater systems", count, "EA", Pr("septic system (each)")));
        double sub = cost.Sum(c => c.Qty * c.Price);
        double cont = p.Number("contingency", 20) / 100, eng = p.Number("engineering", 12) / 100;
        double total = sub * (1 + cont) * (1 + eng);
        double lowF = 1 - p.Number("range low", 30) / 100, highF = 1 + p.Number("range high", 50) / 100;

        // ---------- Constraints and verdict (D-209) ----------
        var flags = new List<Flag>();
        void F(string level, string what, string value, string effect) => flags.Add(new Flag(level, what, value, effect));
        if (!double.IsNaN(floodwayPct) && floodwayPct > 0) F(floodwayPct > 25 ? "FATAL" : "CONSTRAINT", "Regulatory floodway on site", $"{floodwayPct:0.#} %", "No fill or structures in the floodway; excluded from yield (RCMC 15.32).");
        if (!double.IsNaN(sfhaPct) && sfhaPct > 0) F(sfhaPct > 50 ? "FATAL" : "CONSTRAINT", "Special flood hazard area (100-yr)", $"{sfhaPct:0.#} %", exFlood ? "Excluded from yield; a floodplain development permit and possibly a CLOMR/LOMR-F are needed to use it." : "Counted as developable: needs a floodplain development permit, fill to 1 ft above BFE and a LOMR-F.");
        if (double.IsNaN(sfhaPct)) F("UNKNOWN", "Floodplain", "not checked", "Check the FEMA FIRM.");
        if (steepPct > 0) F(steepPct > 30 ? "CONSTRAINT" : "NOTE", $"Slopes steeper than {steepLimit:0} %", $"{steepPct:0.#} %", exSteep ? "Excluded from yield; expect retaining walls or custom grading." : "Counted as developable: expect retaining walls.");
        if (meanSlope > 12) F("CONSTRAINT", "Mean slope", $"{meanSlope:0.#} %", "Street grades and ADA routes will be hard to meet; heavy grading.");
        double dShare = hsgShare.GetValueOrDefault('D'), cShare = hsgShare.GetValueOrDefault('C');
        if (dShare > 0.5) F("CONSTRAINT", "Hydrologic soil group D", $"{dShare * 100:0} %", "High runoff and little infiltration: larger detention; infiltration BMPs unlikely.");
        if (!hydro) F("UNKNOWN", "Soils", "not checked", "Runoff and detention were not estimated.");
        if (residential && !sewer) F("CONSTRAINT", "No public sanitary sewer entered", "—", $"On-site wastewater: {zone.MinLotNoSewer / Acre:0.##} ac minimum lots.");
        if (sewerDist > 2640) F("CONSTRAINT", "Sanitary sewer far away", $"{sewerDist:0} ft", "Long off-site extension; check grade to the main (lift station?).");
        if (waterDist is null) F("CONSTRAINT", "No water main entered", "—", "Water service and fire flow unknown.");
        else if (waterDist > 2640) F("CONSTRAINT", "Water main far away", $"{waterDist:0} ft", "Long off-site extension; check fire flow.");
        if (hydro && pondAc > 0.15 * A) F("CONSTRAINT", "Pond footprint", $"{pondAc / A * 100:0} % of the site", "Detention takes a large share of the land; consider regional detention or underground storage.");
        if (residential && lots == 0 || !residential && buildableAc <= 0) F("FATAL", "No yield", "0", "Nothing buildable after exclusions.");
        if (p.OptionalNumber("target lots") is { } tl && count < tl) F(count < 0.75 * tl ? "FATAL" : "CONSTRAINT", "Yield below target", $"{count} vs {tl:0}", "Below the target count.");
        if (p.OptionalNumber("max cost per lot") is { } mc && count > 0 && total / count > mc) F(total / count * lowF > mc ? "FATAL" : "CONSTRAINT", "Cost per lot above target", $"${total / count:N0}", $"Target ${mc:N0}.");
        if (atlas is not null && atlas.TryGetValue(100, out var a100) && a100.TryGetValue("24-hr", out var p100) && p100 > idcm[100] * 1.1)
            F("NOTE", "Atlas 14 100-yr depth above IDCM", $"{p100:0.00} vs {idcm[100]:0.00} in", "The IDCM governs, but a reviewer may ask; size outlets with margin.");
        string verdict = flags.Any(f => f.Level == "FATAL") ? "NO-GO" : flags.Any(f => f.Level is "CONSTRAINT" or "UNKNOWN") ? "GO WITH CONDITIONS" : "GO";

        // ---------- Output ----------
        string unitWord = residential ? (housing.StartsWith("multi") || housing.StartsWith("two") ? "units" : "lots") : "lots";
        var summary = new List<string>
        {
            $"Verdict: {verdict}. {A:0.00} ac ({site.AreaSqFt:N0} ft²), {zone.Code} ({zone.Name}).",
            residential
                ? $"Yield: {count} {unitWord} on {devAcres:0.00} developable ac ({excludedAc:0.00} ac excluded, {pondAc:0.00} ac pond, {openPct:0} % open space); about {streetLf:N0} LF of street."
                : $"Yield: {buildableAc:0.00} ac buildable, {coverageAc:0.00} ac of building and paving at {zone.MaxCoverage * 100:0} % coverage{(zone.MinLot is null ? "" : $", {lots} lots")}.",
            hydro
                ? $"Drainage: 100-yr peak {storm.Last().qPre:0.#} cfs before, {storm.Last().qPost:0.#} cfs after; detention {vDet:0.00} ac-ft + WQCV {wqcv:0.00} ac-ft; pond about {pondAc:0.00} ac at {pondDepth:0.#} ft deep."
                : "Drainage: not computed (soils unknown).",
            $"Cost (order of magnitude): ${total * lowF:N0} to ${total * highF:N0}, expected ${total:N0}" + (count > 0 ? $" (${total / count:N0} per {unitWord.TrimEnd('s')})." : "."),
        };

        var tables = new List<ToolTable>
        {
            new("Go / no-go", ["Level", "Constraint", "Value", "Effect"], flags.Select(f => (IReadOnlyList<object?>)[f.Level, f.Constraint, f.Value, f.Effect]).ToList(),
                [$"Verdict {verdict}: any FATAL item = NO-GO; any CONSTRAINT or UNKNOWN = GO WITH CONDITIONS; otherwise GO."]),
            new("Site", ["Item", "Value", "Unit"],
            [
                ["Area", Math.Round(A, 3), "ac"], ["Perimeter", Math.Round(site.PerimeterFt, 0), "ft"],
                ["Centroid latitude", Math.Round(site.Centroid.Lat, 5), "°"], ["Centroid longitude", Math.Round(site.Centroid.Lon, 5), "°"],
                ["Mean slope", Math.Round(meanSlope, 2), "%"], ["Relief", double.IsFinite(relief) ? Math.Round(relief, 1) : null, "ft"],
                ["General fall toward", fall, ""], ["Low point elevation", double.IsFinite(lowElev) ? Math.Round(lowElev, 1) : null, "ft"],
                ["Hydraulic length (lag method)", Math.Round(flowLen, 0), "ft"],
            ]),
            new("Data sources", ["Dataset", "Source", "Status", "Detail"], sources),
        };
        if (floodShares.Count > 0 || !double.IsNaN(sfhaPct))
            tables.Add(new("Flood zones on the site", ["Zone", "Share (%)", "Area (ac)"], floodShares.OrderByDescending(k => k.Value).Select(k => (IReadOnlyList<object?>)[k.Key, Math.Round(k.Value, 1), Math.Round(k.Value / 100 * A, 2)]).ToList(),
                [$"Floodway {(double.IsNaN(floodwayPct) ? 0 : floodwayPct):0.#} %, SFHA {(double.IsNaN(sfhaPct) ? 0 : sfhaPct):0.#} % (the floodway is part of the SFHA)."]));
        if (slopeClasses.Count > 0)
            tables.Add(new("Slope classes", ["Slope (%)", "Share (%)", "Area (ac)"], slopeClasses.Select(c => (IReadOnlyList<object?>)[c.Range, Math.Round(c.Pct, 1), Math.Round(c.Pct / 100 * A, 2)]).ToList(),
                [$"Steeper than {steepLimit:0} %: {steepPct:0.#} % of the site."]));
        if (soils.Count > 0)
            tables.Add(new("Soils", ["Map unit", "Name", "HSG", "Share (%)", "CN existing", "CN developed"], soils.Select(s =>
            {
                char g = s.Hsg.Trim().ToUpperInvariant().FirstOrDefault(ch => ch is 'A' or 'B' or 'C' or 'D'); if (g == default) g = 'D';
                return (IReadOnlyList<object?>)[s.MapUnit, s.Name, s.Hsg, Math.Round(s.Fraction * 100, 1), existing.For(g), developed.For(g)];
            }).ToList(), ["Dual groups (e.g. C/D): the first letter (drained) is used. CNs are TR-55 Table 2-2."]));
        tables.Add(new("Zoning and lot layout", ["Item", "Value", "Unit"],
        [
            ["District", $"{zone.Code} {zone.Name}", $"RCMC {zone.Section}"],
            ["Minimum lot area", zone.MinLot, "ft²"], ["Minimum lot area without sewer", zone.MinLotNoSewer, "ft²"],
            ["Minimum lot width", zone.MinWidth, "ft"], ["Maximum coverage", zone.MaxCoverage * 100, "%"],
            ["Front / side / rear yard", $"{zone.Front:0} / {zone.Side:0} / {zone.Rear:0}", "ft"], ["Maximum height", zone.MaxHeight, "ft"],
            ["Design lot area", Math.Round(lotArea, 0), "ft²"], ["Design lot width", Math.Round(lotWidth, 0), "ft"], ["Street right-of-way", rowWidth, "ft"],
        ]));
        tables.Add(new("Yield", ["Item", "Area (ac)", "Share (%)"],
        [
            ["Gross site", Math.Round(A, 2), 100.0],
            ["Excluded (floodway" + (exFlood ? ", floodplain" : "") + (exSteep ? ", steep slopes" : "") + (otherEx > 0 ? ", other" : "") + ")", Math.Round(excludedAc, 2), Math.Round(excludedAc / A * 100, 1)],
            ["Detention / water quality pond", Math.Round(pondAc, 2), Math.Round(pondAc / A * 100, 1)],
            ["Open space", Math.Round(openPct / 100 * A, 2), openPct],
            ["Developable (lots and streets)", Math.Round(devAcres, 2), Math.Round(devAcres / A * 100, 1)],
            [residential ? $"Yield: {count} {unitWord}" : $"Building and paving at {zone.MaxCoverage * 100:0} % coverage", residential ? null : Math.Round(coverageAc, 2), null],
            ["Street length (LF)", Math.Round(streetLf, 0), null],
        ], [yieldNote, $"Exclusions: {exHow}."]));
        if (hydro)
        {
            tables.Add(new("Runoff before and after (TR-55 graphical peak, Type II)", ["Storm", "P 24-hr (in)", "CN pre", "Q pre (in)", "Tc pre (h)", "qp pre (cfs)", "CN post", "Q post (in)", "Tc post (h)", "qp post (cfs)", "Storage Vs (ac-ft)"],
                storm.Select(s => (IReadOnlyList<object?>)[$"{s.Y}-yr", s.P, Math.Round(cnPre, 1), Math.Round(s.Qpre, 2), Math.Round(s.Tpre, 2), Math.Round(s.qPre, 1), Math.Round(cnPost, 1), Math.Round(s.Qpost, 2), Math.Round(s.Tpost, 2), Math.Round(s.qPost, 1), Math.Round(s.Vs, 3)]).ToList(),
                ["Tc by the NRCS lag method (Tc = L/0.6) with the mean slope and hydraulic length; qp = qu·Am·Q (TR-55 Ch. 4).",
                 "Storage by TR-55 Ch. 6 (Figure 6-1, Type II): Vs/Vr = 0.682 − 1.43 r + 1.64 r² − 0.804 r³, r = post-development release (pre-development peak) / post-development peak.",
                 "Concept only: route the pond with the detention tool (IDCM §4.10) for design."]));
            tables.Add(new("Pond concept", ["Item", "Value", "Unit"],
            [
                ["Detention storage (largest of the storms)", Math.Round(vDet, 3), "ac-ft"],
                [$"WQCV (SWQM §2.2.2, {drainHours:0} h drain, a = {aCoef:0.0}, I = {imp * devFrac:0.#} %)", Math.Round(wqcv, 3), "ac-ft"],
                ["Total storage", Math.Round(pondVol, 3), "ac-ft"], ["Storage depth", pondDepth, "ft"],
                ["Footprint (storage / depth × " + pondFactor.ToString("0.##", Inv) + " for side slopes, freeboard and access)", Math.Round(pondAc, 2), "ac"],
                ["Suggested location", lowPoint is null ? "site low point" : "low point" + (fall == "—" ? "" : ", site falls " + fall), ""],
            ], ["The WQCV is stacked below the detention volume (an extended detention basin), per the Rapid City SWQM via IDCM §4.1.7."]));
        }
        tables.Add(new("Rainfall", ["Storm", "IDCM Table 4-2 24-hr (in)", "Atlas 14 24-hr (in)", "Atlas 14 1-hr (in)", "Used (in)"],
            storms.Select(y => (IReadOnlyList<object?>)[$"{y}-yr", idcm[y], A14(atlas, y, "24-hr"), A14(atlas, y, "60-min") ?? A14(atlas, y, "1-hr"), design[y]]).ToList(),
            ["Criteria precedence: IDCM 2012 > SWQM 2009 > Drainage Criteria Manual. Atlas 14 is shown for comparison."]));
        if (utils.Count > 0)
            tables.Add(new("Utilities", ["Utility", "Distance to site (ft)", "Size (in)"], utils.Select(u => (IReadOnlyList<object?>)[u.Name, u.Dist, u.Size]).ToList()));
        var costRows = cost.Where(c => c.Qty > 0).Select(c => (IReadOnlyList<object?>)[c.Item, Math.Round(c.Qty, c.Unit == "EA" || c.Unit == "LS" ? 0 : 1), c.Unit, c.Price, Math.Round(c.Qty * c.Price, 0)]).ToList();
        costRows.Add(["Subtotal", null, null, null, Math.Round(sub, 0)]);
        costRows.Add([$"Contingency {cont * 100:0} %", null, null, null, Math.Round(sub * cont, 0)]);
        costRows.Add([$"Engineering, survey, testing {eng * 100:0} %", null, null, null, Math.Round(sub * (1 + cont) * eng, 0)]);
        costRows.Add(["Expected total", null, null, null, Math.Round(total, 0)]);
        costRows.Add([$"Range low (−{(1 - lowF) * 100:0} %)", null, null, null, Math.Round(total * lowF, 0)]);
        costRows.Add([$"Range high (+{(highF - 1) * 100:0} %)", null, null, null, Math.Round(total * highF, 0)]);
        if (count > 0) costRows.Add([$"Per {unitWord.TrimEnd('s')} (expected)", count, "EA", null, Math.Round(total / count, 0)]);
        tables.Add(new("Cost range (site development, order of magnitude)", ["Item", "Quantity", "Unit", "Unit price ($)", "Amount ($)"], costRows,
            ["Unit prices are your [Unit costs] or placeholders; land, fees, dry utilities and off-site street work are not included.",
             $"Earthwork: balanced pads on a plane at the mean slope, cut ≈ area × slope × lot depth ({lotDepth:0} ft) / 8, plus pond excavation."]));

        var charts = new List<ToolChart>();
        if (hydro)
            charts.Add(new ToolChart("Peak discharge before and after development", "Return period (yr)", "Peak (cfs)",
                [new ChartSeries("Before", storm.Select(s => ((double)s.Y, s.qPre)).ToList()), new ChartSeries("After, no detention", storm.Select(s => ((double)s.Y, s.qPost)).ToList(), Dashed: true)]) { LogX = true });
        if (slopeClasses.Count > 0)
            charts.Add(new ToolChart("Slope distribution", "Slope class lower bound (%)", "Share of site (%)",
                [new ChartSeries("Share", slopeClasses.Select(c => (c.Lo, c.Pct)).ToList())]));
        if (atlas is not null)
            charts.Add(new ToolChart("24-hour rainfall: IDCM vs Atlas 14", "Return period (yr)", "Depth (in)",
                [new ChartSeries("IDCM Table 4-2", storms.Select(y => ((double)y, idcm[y])).ToList()),
                 new ChartSeries("Atlas 14", atlas.Where(k => k.Value.ContainsKey("24-hr")).OrderBy(k => k.Key).Select(k => ((double)k.Key, k.Value["24-hr"])).ToList(), Dashed: true)]) { LogX = true });

        var fig = new List<ToolFigureSeries> { new("Site boundary", ToolSeriesKind.Ground, site.Local.Append(site.Local[0]).ToList()) };
        if (floodAreas is not null)
        {
            var b = site.Bounds; double m = 0.1 * Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY);
            foreach (var a in floodAreas.Where(a => a.Sfha))
                foreach (var r in a.Rings)
                {
                    var clipped = ClipRect(r.Select(site.ToLocal).ToList(), b.MinX - m, b.MinY - m, b.MaxX + m, b.MaxY + m);
                    if (clipped.Count >= 3) fig.Add(new ToolFigureSeries(a.IsFloodway ? "Floodway" : "100-yr floodplain", ToolSeriesKind.Water, clipped));
                }
        }
        if (lowPoint is { } lp)
        {
            double s = 0.03 * site.DiagonalFt;
            fig.Add(new ToolFigureSeries("Low point (pond)", ToolSeriesKind.Limit, [(lp.X - s, lp.Y - s), (lp.X + s, lp.Y + s), (lp.X, lp.Y), (lp.X - s, lp.Y + s), (lp.X + s, lp.Y - s)]));
        }
        var figures = new List<ToolFigure> { new("Site plan", fig, "East (ft) from the site's west edge", "North (ft)", ["Plan view; the vertical scale may differ from the horizontal."]) };

        msgs.InsertRange(0, p.Errors);
        if (verdict != "GO") msgs.Add(CalcMessage.Warning("W-SF-001", $"Verdict {verdict}: see the go / no-go table."));
        return new ToolResult(title, summary, tables, msgs) { Charts = charts, Figures = figures };
    }

    // ---------------- calculations ----------------

    /// <summary>TR-55 runoff depth (in), Tc (h, lag method), and peak (cfs) for one storm.</summary>
    public static (double Q, double Tc, double Qp) Peak(double p, double cn, double lengthFt, double slopePct, double acres)
    {
        double q = Losses.CurveNumberRunoff(p, cn);
        double tc = Math.Clamp(LagTc(lengthFt, cn, slopePct), 0.1, 10);
        double s = 1000 / cn - 10, iaP = 0.2 * s / p;
        double qu = Tr55Tables.UnitPeak("II", tc, iaP);
        return (q, tc, qu * acres / 640 * q);
    }

    /// <summary>NRCS lag method (NEH 630 Ch. 15, Eq. 15-4): L = ℓ^0.8 (S + 1)^0.7 / (1900 Y^0.5) h, Tc = L / 0.6; Y in percent (at least 0.5).</summary>
    public static double LagTc(double lengthFt, double cn, double slopePct)
    {
        double s = 1000 / cn - 10;
        return Math.Pow(lengthFt, 0.8) * Math.Pow(s + 1, 0.7) / (1900 * Math.Sqrt(Math.Max(0.5, slopePct))) / 0.6;
    }

    /// <summary>TR-55 Figure 6-1 (Appendix F), Type II/III: Vs/Vr = 0.682 − 1.43 r + 1.64 r² − 0.804 r³, r = qo/qi.</summary>
    public static double StorageRatio(double r) => 0.682 - 1.43 * r + 1.64 * r * r - 0.804 * r * r * r;

    private static double? A14(Dictionary<int, Dictionary<string, double>>? a, int y, string d) =>
        a is not null && a.TryGetValue(y, out var x) && x.TryGetValue(d, out var v) ? v : null;

    private static bool IsSfhaLabel(string z)
    {
        var t = z.Trim().ToUpperInvariant();
        return (t.StartsWith('A') || t.StartsWith('V')) && !t.Contains("0.2");
    }

    private static double LowerBound(string range)
    {
        var t = range.Trim().TrimStart('>', '≥', '<', '≤');
        var first = t.Split('-', '–', ' ')[0];
        return range.Trim().StartsWith('<') ? 0 : double.TryParse(first, NumberStyles.Float, Inv, out double v) ? v : 0;
    }

    private static double Mid(string range)
    {
        var parts = range.Trim().TrimStart('>', '≥').Split('-', '–').Select(s => double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : double.NaN).ToArray();
        if (range.Trim().StartsWith('>') || range.Trim().StartsWith('≥')) return parts[0] + 5;
        return parts.Length >= 2 && double.IsFinite(parts[1]) ? (parts[0] + parts[1]) / 2 : parts[0];
    }

    private static bool InRings(List<List<(double X, double Y)>> rings, double x, double y)
    {
        bool inside = false;
        foreach (var r in rings) if (SiteGeometry.Inside(r, x, y)) inside = !inside;   // holes toggle back out
        return inside;
    }

    /// <summary>Sutherland–Hodgman clip of a polygon to a rectangle (for drawing flood polygons that extend far off the site).</summary>
    public static List<(double X, double Y)> ClipRect(List<(double X, double Y)> poly, double x0, double y0, double x1, double y1)
    {
        var o = poly;
        foreach (var (inside, cut) in new (Func<(double X, double Y), bool>, Func<(double X, double Y), (double X, double Y), (double X, double Y)>)[]
        {
            (q => q.X >= x0, (a, b) => (x0, a.Y + (b.Y - a.Y) * (x0 - a.X) / (b.X - a.X))),
            (q => q.X <= x1, (a, b) => (x1, a.Y + (b.Y - a.Y) * (x1 - a.X) / (b.X - a.X))),
            (q => q.Y >= y0, (a, b) => (a.X + (b.X - a.X) * (y0 - a.Y) / (b.Y - a.Y), y0)),
            (q => q.Y <= y1, (a, b) => (a.X + (b.X - a.X) * (y1 - a.Y) / (b.Y - a.Y), y1)),
        })
        {
            var n = new List<(double X, double Y)>();
            for (int i = 0; i < o.Count; i++)
            {
                var cur = o[i]; var prev = o[(i + o.Count - 1) % o.Count];
                bool ci = inside(cur), pi = inside(prev);
                if (ci) { if (!pi) n.Add(cut(prev, cur)); n.Add(cur); }
                else if (pi) n.Add(cut(prev, cur));
            }
            o = n;
            if (o.Count == 0) break;
        }
        return o;
    }

    private static string Key(string s) => s.Trim().ToLowerInvariant();

    /// <summary>Placeholder unit prices (2026 order of magnitude, Black Hills region); every one can be replaced under [Unit costs].</summary>
    public static Dictionary<string, double> DefaultPrices() => new()
    {
        ["street (per lf)"] = 450, ["water main (per lf)"] = 120, ["sanitary sewer (per lf)"] = 140, ["storm sewer (per lf)"] = 160,
        ["services (each)"] = 4500, ["water extension (per lf)"] = 150, ["sewer extension (per lf)"] = 180,
        ["pond excavation (per cy)"] = 12, ["pond outlet (each)"] = 25000, ["earthwork (per cy)"] = 4.5,
        ["erosion control (per ac)"] = 2500, ["retaining wall (per sf)"] = 65, ["septic system (each)"] = 18000,
    };
}
