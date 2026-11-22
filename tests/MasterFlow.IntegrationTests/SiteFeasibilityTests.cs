using MasterFlow.Application.Feasibility;
using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>Site feasibility: geometry, hand checks of the hydrology and yield, public-data parsers, and the fetch / cache / offline paths.</summary>
public class SiteFeasibilityTests : IDisposable
{
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "mf-site-" + Guid.NewGuid().ToString("N"));
    private readonly Func<string, string?, string> _savedFetcher = PublicSiteData.Fetcher;
    private readonly string _savedCache = PublicSiteData.CacheFolder;

    public SiteFeasibilityTests() => PublicSiteData.CacheFolder = _cache;

    public void Dispose()
    {
        PublicSiteData.Fetcher = _savedFetcher;
        PublicSiteData.CacheFolder = _savedCache;
        try { Directory.Delete(_cache, true); } catch { }
    }

    private static ToolResult Run(string input)
    {
        var r = new SiteFeasibilityTool().Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    private static ToolTable T(ToolResult r, string start) => r.Tables.Single(t => t.Title.StartsWith(start));
    private static object? Row(ToolTable t, string item, int col = 1) => t.Rows.First(x => ((string)x[0]!).StartsWith(item))[col];

    [Fact]
    public void Geometry_SquareOf1320Feet_Is40Acres()
    {
        var g0 = new SiteGeometry([new(44.05, -103.30), new(44.051, -103.30), new(44.051, -103.299)]);
        var sq = new[] { (0.0, 0.0), (1320.0, 0.0), (1320.0, 1320.0), (0.0, 1320.0) }.Select(p => g0.ToGeo(p.Item1, p.Item2)).ToList();
        var g = new SiteGeometry(sq);
        Assert.Equal(40.0, g.Acres, 1);
        Assert.Equal(4 * 1320, g.PerimeterFt, 0);
        Assert.True(g.Contains(g.ToGeo(660, 660)));
        Assert.False(g.Contains(g.ToGeo(1400, 660)));
        // Sample grid: every cell of a square lies inside; cell areas add up to the site.
        var (pts, cell) = g.SampleGrid(20);
        Assert.Equal(400, pts.Count);
        Assert.Equal(g.AreaSqFt, pts.Count * cell, 0);
        Assert.Contains("POLYGON((-103.", g.Wkt());
    }

    [Fact]
    public void Hydrology_HandChecks()
    {
        // TR-55 Figure 6-1, Type II: r = 0.25 → 0.682 − 0.3575 + 0.1025 − 0.012563 = 0.41444.
        Assert.Equal(0.41444, SiteFeasibilityTool.StorageRatio(0.25), 4);
        // Lag: ℓ = 1750 ft, CN 80 (S = 2.5), Y = 7.2 % → L = 1750^0.8 · 3.5^0.7 / (1900 · 7.2^0.5) = 393.6 · 2.4031 / 5098.2 = 0.18553 h; Tc = 0.3092 h.
        Assert.Equal(0.3092, SiteFeasibilityTool.LagTc(1750, 80, 7.2), 3);
        // Peak: P = 4.55 in, CN 80 → Q = (4.55 − 0.5)² / (4.55 + 2.0) = 2.504 in.
        var (q, tc, qp) = SiteFeasibilityTool.Peak(4.55, 80, 1750, 7.2, 40);
        Assert.Equal(2.504, q, 3);
        Assert.Equal(0.3092, tc, 3);
        double qu = MasterFlow.Application.Hydrology.Tr55Tables.UnitPeak("II", tc, 0.5 / 4.55);
        Assert.Equal(qu * 40 / 640 * q, qp, 6);
    }

    [Fact]
    public void Sample_YieldPondAndCost_FollowTheirFormulas()
    {
        var tool = new SiteFeasibilityTool();
        var r = Run(tool.SampleInput(Us));
        var site = T(r, "Site");
        double A = (double)Row(site, "Area")!;
        Assert.InRange(A, 39.5, 40.5);
        var y = T(r, "Yield");
        double ex = (double)Row(y, "Excluded")!, pond = (double)Row(y, "Detention")!, dev = (double)Row(y, "Developable")!;
        // Entered shares: floodway 2 % + rest of SFHA (AE 6 %) + slopes > 25 % (4 %) = 12 % (overlap unknown, so summed).
        Assert.Equal(Math.Round(0.12 * A, 2), ex, 2);
        Assert.Equal(A - ex - pond - 0.05 * A, dev, 1);
        // Each lot takes 7500 / 0.9 + 50/2 · 52 = 9633 ft².
        int lots = (int)Math.Floor(dev * 43560 / (7500 / 0.9 + 25 * 52));
        Assert.StartsWith($"Yield: {lots} lots", (string)y.Rows.First(x => ((string)x[0]!).StartsWith("Yield"))[0]!);
        Assert.InRange(lots, 100, 170);
        // Pond: footprint = storage / depth × 1.3.
        var pc = T(r, "Pond concept");
        double v = (double)Row(pc, "Total storage")!;
        Assert.Equal(Math.Round(v * 43560 / 4 * 1.3 / 43560, 2), (double)Row(pc, "Footprint")!, 2);
        Assert.True((double)Row(pc, "Detention")! > 0);
        // Post-development peaks exceed pre for every storm.
        foreach (var row in T(r, "Runoff").Rows) Assert.True((double)row[9]! > (double)row[5]!);
        // Cost: the expected total is subtotal × 1.2 × 1.12, and the range is −30 % / +50 %.
        var c = T(r, "Cost range");
        double sub = (double)Row(c, "Subtotal", 4)!, tot = (double)Row(c, "Expected total", 4)!;
        Assert.InRange(tot, sub * 1.344 - 2, sub * 1.344 + 2);
        Assert.InRange((double)Row(c, "Range low", 4)!, tot * 0.7 - 1, tot * 0.7 + 1);
        Assert.InRange((double)Row(c, "Range high", 4)!, tot * 1.5 - 1, tot * 1.5 + 1);
        // Floodway on site → GO WITH CONDITIONS.
        Assert.StartsWith("Verdict: GO WITH CONDITIONS", r.Summary[0]);
        Assert.Contains(T(r, "Go / no-go").Rows, x => (string)x[1]! == "Regulatory floodway on site");
        Assert.NotEmpty(r.Charts);
        Assert.Contains("<svg", ToolReport.ToHtml(new ProjectInfo { Name = "P" }, r, "x", "test"));
    }

    [Fact]
    public void NoSewer_UsesOneAcreLots_AndFlagsIt()
    {
        var tool = new SiteFeasibilityTool();
        var input = tool.SampleInput(Us).Replace("sanitary sewer, 900, 8", "");
        var r = Run(input);
        var lots = int.Parse(((string)T(r, "Yield").Rows.First(x => ((string)x[0]!).StartsWith("Yield"))[0]!).Split(' ')[1]);
        Assert.InRange(lots, 15, 35);
        Assert.Contains(r.Messages, m => m.Code == "W-SF-019");
        Assert.Contains(T(r, "Go / no-go").Rows, x => ((string)x[1]!).StartsWith("No public sanitary sewer"));
    }

    [Fact]
    public void Errors_AreReported()
    {
        var tool = new SiteFeasibilityTool();
        Assert.Contains(tool.Run(tool.SampleInput(Us), new DesignToolContext(UnitSystem.SI)).Messages, m => m.Code == "E-SF-001");
        Assert.Contains(tool.Run(tool.SampleInput(Us).Replace("zoning = LDR-1", "zoning = XYZ"), Us).Messages, m => m.Code == "E-SF-009");
        Assert.Contains(tool.Run("zoning = LDR-1", Us).Messages, m => m.Code == "E-SF-005");
        Assert.Contains(tool.Run("parcel = 123\nzoning = LDR-1", Us).Messages, m => m.Code == "E-SF-002");
    }

    [Fact]
    public void IsPremium()
    {
        var tool = new SiteFeasibilityTool();
        Assert.True(PremiumFeatures.IsPremium(tool));
        Assert.Contains(PremiumFeatures.Run(tool, tool.SampleInput(Us), Us, LicenseState.Free).Messages, m => m.Code == "E-LIC-001");
    }

    // ---------------- public data ----------------

    private const string ParcelJson = """
        {"features":[{"attributes":{"PIN":"3712345678","Acres":40.1},
          "geometry":{"rings":[[[-103.30,44.05],[-103.30,44.05363],[-103.29496,44.05363],[-103.29496,44.05],[-103.30,44.05]]]}}]}
        """;

    // A floodway strip along the west 10 % of the site inside a wider AE zone covering the west 30 %.
    private const string FloodJson = """
        {"features":[
          {"attributes":{"FLD_ZONE":"AE","ZONE_SUBTY":"FLOODWAY","SFHA_TF":"T","STATIC_BFE":-9999},
           "geometry":{"rings":[[[-103.31,44.04],[-103.31,44.06],[-103.299496,44.06],[-103.299496,44.04],[-103.31,44.04]]]}},
          {"attributes":{"FLD_ZONE":"AE","ZONE_SUBTY":" ","SFHA_TF":"T","STATIC_BFE":3301.5},
           "geometry":{"rings":[[[-103.299496,44.04],[-103.299496,44.06],[-103.298488,44.06],[-103.298488,44.04],[-103.299496,44.04]]]}},
          {"attributes":{"FLD_ZONE":"X","ZONE_SUBTY":"AREA OF MINIMAL FLOOD HAZARD","SFHA_TF":"F","STATIC_BFE":-9999},
           "geometry":{"rings":[[[-103.298488,44.04],[-103.298488,44.06],[-103.28,44.06],[-103.28,44.04],[-103.298488,44.04]]]}}]}
        """;

    private const string SoilsJson = """
        {"Table":[["mukey","musym","muname","hydgrp","area"],["101","NuB","Nunn clay loam","C","3.0E-6"],["102","PeC","Pierre clay","D","1.0E-6"]]}
        """;

    private const string PfdsCsv = """
        Point precipitation frequency estimates (inches)
        NOAA Atlas 14 Volume 8 Version 2
        Data type: Precipitation depth
        Time series type: Partial duration

        PRECIPITATION FREQUENCY ESTIMATES
        by duration for ARI (years):, 1,2,5,10,25,50,100,200,500,1000
        5-min:, 0.29,0.35,0.46,0.56,0.70,0.82,0.95,1.08,1.27,1.42
        60-min:, 0.80,0.97,1.28,1.56,1.99,2.36,2.75,3.18,3.79,4.30
        24-hr:, 1.65,1.95,2.50,3.00,3.75,4.40,5.10,5.85,6.95,7.85
        2-day:, 1.90,2.25,2.90,3.45,4.30,5.00,5.75,6.55,7.70,8.65

        Date/time (GMT):  Fri Nov 21 2026
        """;

    /// <summary>Elevations on a plane falling 5 % to the east: z(m) = 1000 − 0.05·x(ft)·0.3048, so the slope is exactly 5 %.</summary>
    private static string ElevationJson(string url)
    {
        var geom = Uri.UnescapeDataString(url[(url.IndexOf("geometry=") + 9)..url.IndexOf("&geometryType")]);
        var pts = System.Text.Json.JsonDocument.Parse(geom).RootElement.GetProperty("points").EnumerateArray().Select(p => (Lon: p[0].GetDouble(), Lat: p[1].GetDouble())).ToList();
        double lon0 = pts.Min(p => p.Lon), cos = Math.Cos(44.0518 * Math.PI / 180);
        var samples = pts.Select((p, i) =>
        {
            double x = (p.Lon - lon0) * Math.PI / 180 * SiteGeometry.EarthRadiusFt * cos;
            return $"{{\"locationId\":{i},\"value\":\"{(1000 - 0.05 * x * 0.3048).ToString(System.Globalization.CultureInfo.InvariantCulture)}\"}}";
        });
        return "{\"samples\":[" + string.Join(",", samples) + "]}";
    }

    private static string Fake(string url, string? body)
    {
        if (url.StartsWith(PublicSiteData.ParcelLayer)) return ParcelJson;
        if (url.StartsWith(PublicSiteData.FemaFloodLayer)) return FloodJson;
        if (url.StartsWith(PublicSiteData.SoilDataAccess)) { Assert.Contains("STIntersects", body); return SoilsJson; }
        if (url.StartsWith(PublicSiteData.ElevationService)) return ElevationJson(url);
        if (url.StartsWith("https://hdsc.nws.noaa.gov")) return PfdsCsv;
        throw new HttpRequestException("unexpected " + url);
    }

    private const string FetchInput = """
        site name = Fetched parcel
        fetch = yes
        parcel = 3712345678
        zoning = LDR-1
        [Utilities]
        Utility, Distance (ft)
        water, 200
        sanitary, 300
        """;

    [Fact]
    public void Parsers_ReadServiceResponses()
    {
        var (ring, ac, err) = PublicSiteData.ParseParcel(ParcelJson);
        Assert.Null(err); Assert.Equal(40.1, ac); Assert.Equal(5, ring!.Count);
        var (fl, _) = PublicSiteData.ParseFlood(FloodJson);
        Assert.Equal(3, fl!.Count);
        Assert.True(fl[0].IsFloodway && fl[0].Sfha && fl[0].Bfe is null);
        Assert.Equal(3301.5, fl[1].Bfe); Assert.False(fl[2].Sfha);
        var (so, _) = PublicSiteData.ParseSoils(SoilsJson);
        Assert.Equal(0.75, so![0].Fraction, 9); Assert.Equal("D", so[1].Hsg);
        var (pf, _) = PublicSiteData.ParsePfds(PfdsCsv);
        Assert.Equal(5.10, pf![100]["24-hr"]); Assert.Equal(0.97, pf[2]["60-min"]); Assert.Equal(5.75, pf[100]["2-day"]);
        var (z, _) = PublicSiteData.ParseElevations("""{"samples":[{"locationId":1,"value":"1000"},{"locationId":0,"value":"NoData"}]}""", 2);
        Assert.True(double.IsNaN(z![0])); Assert.Equal(1000 / 0.3048, z[1], 6);
        Assert.NotNull(PublicSiteData.ParseFlood("""{"error":{"code":400}}""").Error);
        Assert.Null(PublicSiteData.ParsePfds("nothing here").Depths);
    }

    [Fact]
    public void Fetch_UsesEveryService_ThenTheCache_AndWorksOfflineFromIt()
    {
        int calls = 0;
        PublicSiteData.Fetcher = (u, b) => { calls++; return Fake(u, b); };
        var r = Run(FetchInput);
        Assert.Equal(5, calls);   // parcel, flood, elevation, soils, rainfall
        var src = T(r, "Data sources");
        Assert.All(src.Rows, row => Assert.NotEqual("not available", row[2]));
        Assert.Contains(src.Rows, row => (string)row[0]! == "Floodplain" && (string)row[2]! == "fetched");
        // Floodway = west 10 %, SFHA = west 30 % (grid sampling, 60 × 60).
        var fz = T(r, "Flood zones");
        Assert.InRange((double)fz.Rows.Single(x => (string)x[0]! == "AE floodway")[1]!, 8.5, 11.5);
        Assert.InRange((double)fz.Rows.Single(x => (string)x[0]! == "AE")[1]!, 18.5, 21.5);
        // Elevation plane at exactly 5 %, falling east.
        Assert.Equal(5.0, (double)Row(T(r, "Site"), "Mean slope")!, 1);
        Assert.Equal("E", Row(T(r, "Site"), "General fall toward"));
        Assert.Equal(100.0, (double)T(r, "Slope classes").Rows.Single(x => (string)x[0]! == "5-15")[1]!, 1);
        // Soils 75 % C / 25 % D; Atlas 14 shown beside the IDCM, IDCM used.
        Assert.Equal(75.0, (double)T(r, "Soils").Rows[0][3]!, 1);
        var rain = T(r, "Rainfall").Rows.Single(x => (string)x[0]! == "100-yr");
        Assert.Equal(5.10, rain[2]); Assert.Equal(4.55, rain[4]);
        Assert.Contains(r.Figures[0].Series, s => s.Kind == ToolSeriesKind.Water);

        // Second run: everything from the cache, nothing sent.
        var r2 = Run(FetchInput);
        Assert.Equal(5, calls);
        Assert.All(T(r2, "Data sources").Rows.Where(x => (string)x[1]! != "entered" && !((string)x[0]!).StartsWith("Rainfall (design)") && (string)x[0]! != "Utilities"), row => Assert.Equal("cached", row[2]));

        // Offline with refresh = yes: the services fail, the cached copies still answer.
        PublicSiteData.Fetcher = (_, _) => throw new HttpRequestException("offline");
        var r3 = Run(FetchInput + "\nrefresh = yes");
        Assert.Equal(T(r, "Yield").Rows.Count, T(r3, "Yield").Rows.Count);
        Assert.Equal(r.Summary[1], r3.Summary[1]);
    }

    [Fact]
    public void Offline_WithNoCache_DegradesToEnteredData()
    {
        PublicSiteData.Fetcher = (_, _) => throw new HttpRequestException("offline");
        var r = Run("""
            fetch = yes
            zoning = LDR-1
            mean slope = 4
            assume hsg = C
            [Boundary]
            Latitude, Longitude
            44.05000, -103.30000
            44.05000, -103.29496
            44.05363, -103.29496
            44.05363, -103.30000
            [Utilities]
            Utility, Distance (ft)
            water, 100
            sanitary, 100
            """);
        Assert.Contains(r.Messages, m => m.Code == "W-SF-010");
        Assert.Contains(r.Messages, m => m.Code == "W-SF-012");
        Assert.Contains(T(r, "Go / no-go").Rows, x => (string)x[0]! == "UNKNOWN");
        Assert.StartsWith("Verdict: GO WITH CONDITIONS", r.Summary[0]);
        // Without anything at all to go on, the tool says what is missing instead of guessing.
        var none = new SiteFeasibilityTool().Run("fetch = yes\nzoning = LDR-1\n[Boundary]\nLatitude, Longitude\n44.05,-103.30\n44.05,-103.295\n44.0536,-103.295", Us);
        Assert.Contains(none.Messages, m => m.Code == "E-SF-008");
    }

    [Fact]
    public void ClipRect_ClipsToTheBox()
    {
        var c = SiteFeasibilityTool.ClipRect([(-10, -10), (10, -10), (10, 10), (-10, 10)], 0, 0, 5, 5);
        Assert.Equal(25, Math.Abs(SiteGeometry.Shoelace(c)), 6);
    }
}
