using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterFlow.Application.Feasibility;

/// <summary>A flood hazard polygon (FEMA NFHL S_Fld_Haz_Ar): zone, subtype (e.g. FLOODWAY), whether it is a SFHA, the static BFE, and its rings.</summary>
public sealed record FloodArea(string Zone, string Subtype, bool Sfha, double? Bfe, IReadOnlyList<IReadOnlyList<GeoPoint>> Rings)
{
    public bool IsFloodway => Subtype.Contains("FLOODWAY", StringComparison.OrdinalIgnoreCase);
    /// <summary>Label used in the tables: "AE floodway", "AE", "X (0.2 %)".</summary>
    public string Label => IsFloodway ? Zone + " floodway" : Subtype.Contains("0.2", StringComparison.Ordinal) ? Zone + " (0.2 % annual chance)" : Zone;
}

/// <summary>A soil map unit on the site: key, symbol and name, hydrologic soil group of its dominant component, and its share of the site (0–1).</summary>
public sealed record SoilUnit(string MapUnit, string Name, string Hsg, double Fraction);

/// <summary>
/// Free public data for a site, each fetched once and cached on this computer (D-201):
/// <list type="bullet">
/// <item>Parcels: Pennington County tax parcels on the Rapid City MSP ArcGIS service (PIN → boundary, acres).</item>
/// <item>Flood: FEMA National Flood Hazard Layer, layer 28 (flood hazard zones), falling back to the copy on the Rapid City MSP service.</item>
/// <item>Soils: NRCS Soil Data Access (SSURGO): map units clipped to the boundary, dominant-component hydrologic soil group.</item>
/// <item>Elevation: USGS 3DEP elevation image service (getSamples, one call for the whole grid).</item>
/// <item>Rainfall: NOAA Atlas 14 PFDS (the CSV the PFDS page downloads).</item>
/// </list>
/// Only coordinates (or the PIN) are sent. Every request goes through <see cref="Fetcher"/>, so tests replace it.
/// </summary>
public static class PublicSiteData
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string ParcelLayer = "https://services.arcgis.com/04HiymDgLlsbhaV4/ArcGIS/rest/services/Rapid_City_MSP_Base_Data/FeatureServer/5";
    public const string FemaFloodLayer = "https://hazards.fema.gov/arcgis/rest/services/public/NFHL/MapServer/28";
    public const string RapidCityFloodLayer = "https://services.arcgis.com/04HiymDgLlsbhaV4/ArcGIS/rest/services/Rapid_City_MSP_Base_Data/FeatureServer/4";
    public const string SoilDataAccess = "https://SDMDataAccess.sc.egov.usda.gov/Tabular/post.rest";
    public const string ElevationService = "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/getSamples";
    public const string PfdsCsv = "https://hdsc.nws.noaa.gov/cgi-bin/hdsc/new/fe_text_mean.csv?lat={0}&lon={1}&data=depth&units=english&series=pds";

    /// <summary>Performs a request: (url, POST body or null) → response text; throws on failure. Tests replace it.</summary>
    public static Func<string, string?, string> Fetcher { get; set; } = DefaultFetch;

    public static string CacheFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MasterFlow", "site-data");

    private static string DefaultFetch(string url, string? body)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MasterFlow");
        var resp = body is null
            ? http.GetAsync(url).GetAwaiter().GetResult()
            : http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }

    public static string CacheFile(string url, string? body)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(url + "\n" + body));
        return Path.Combine(CacheFolder, Convert.ToHexString(h)[..24] + ".txt");
    }

    /// <summary>Response text from the cache, else from <see cref="Fetcher"/> (then cached). Error is set when neither works.</summary>
    public static (string? Text, string? Error, bool FromCache) Get(string url, string? body, bool refresh)
    {
        string file = CacheFile(url, body);
        if (!refresh)
            try { if (File.Exists(file)) return (File.ReadAllText(file), null, true); } catch { /* fetch instead */ }
        try
        {
            var text = Fetcher(url, body);
            try { Directory.CreateDirectory(CacheFolder); File.WriteAllText(file, text); } catch { /* the cache is optional */ }
            return (text, null, false);
        }
        catch (Exception ex)
        {
            // Offline: an older cached copy is still better than nothing.
            try { if (File.Exists(file)) return (File.ReadAllText(file), null, true); } catch { }
            return (null, ex.Message, false);
        }
    }

    // ---------------- Parcels ----------------

    public static string ParcelUrl(string pin) =>
        $"{ParcelLayer}/query?where={Uri.EscapeDataString($"PIN='{pin.Replace("'", "''")}'")}&outFields=PIN,Acres&returnGeometry=true&outSR=4326&f=json";

    /// <summary>The outer ring (largest) of the first parcel, and its recorded acres.</summary>
    public static (List<GeoPoint>? Ring, double? Acres, string? Error) ParseParcel(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err)) return (null, null, err.ToString());
            var feats = doc.RootElement.GetProperty("features");
            if (feats.GetArrayLength() == 0) return (null, null, "no parcel with that PIN");
            var f = feats[0];
            double? acres = f.GetProperty("attributes").TryGetProperty("Acres", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : null;
            var rings = ReadRings(f.GetProperty("geometry"));
            var ring = rings.OrderByDescending(r => Math.Abs(GeoArea(r))).FirstOrDefault();
            return (ring?.ToList(), acres, ring is null ? "the parcel has no geometry" : null);
        }
        catch (Exception ex) { return (null, null, "unreadable parcel response: " + ex.Message); }
    }

    // ---------------- Flood ----------------

    public static string FloodUrl(string layer, (double West, double South, double East, double North) b) =>
        $"{layer}/query?geometry={F(b.West)},{F(b.South)},{F(b.East)},{F(b.North)}&geometryType=esriGeometryEnvelope&inSR=4326" +
        "&spatialRel=esriSpatialRelIntersects&outFields=FLD_ZONE,ZONE_SUBTY,SFHA_TF,STATIC_BFE&returnGeometry=true&outSR=4326&f=json";

    public static (List<FloodArea>? Areas, string? Error) ParseFlood(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err)) return (null, err.ToString());
            var list = new List<FloodArea>();
            foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
            {
                var at = f.GetProperty("attributes");
                string S(string k) => at.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
                double? bfe = at.TryGetProperty("STATIC_BFE", out var b) && b.ValueKind == JsonValueKind.Number && b.GetDouble() > -9000 ? b.GetDouble() : null;
                string zone = S("FLD_ZONE");
                bool sfha = S("SFHA_TF") == "T" || zone.StartsWith('A') || zone.StartsWith('V');
                list.Add(new FloodArea(zone, S("ZONE_SUBTY"), sfha, bfe, ReadRings(f.GetProperty("geometry"))));
            }
            return (list, null);
        }
        catch (Exception ex) { return (null, "unreadable flood response: " + ex.Message); }
    }

    // ---------------- Soils ----------------

    /// <summary>Soil Data Access query: map units intersecting the polygon with the clipped area (degrees², used only as weights) and the dominant component's hydrologic group.</summary>
    public static string SoilsBody(string wkt)
    {
        string g = $"geometry::STGeomFromText('{wkt}', 4326).MakeValid()";
        string sql =
            "SELECT M.mukey, M.musym, M.muname, " +
            "(SELECT TOP 1 C.hydgrp FROM component C WHERE C.mukey = M.mukey ORDER BY C.comppct_r DESC) AS hydgrp, " +
            $"SUM(P.mupolygongeo.STIntersection({g}).STArea()) AS area " +
            "FROM mupolygon P INNER JOIN mapunit M ON M.mukey = P.mukey " +
            $"WHERE P.mupolygongeo.STIntersects({g}) = 1 " +
            "GROUP BY M.mukey, M.musym, M.muname";
        return JsonSerializer.Serialize(new { query = sql, format = "JSON+COLUMNNAME" });
    }

    public static (List<SoilUnit>? Units, string? Error) ParseSoils(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Table", out var t) || t.GetArrayLength() < 2) return (null, "no soil map units returned for the boundary");
            var head = t[0].EnumerateArray().Select(x => x.GetString()!.ToLowerInvariant()).ToList();
            int iSym = head.IndexOf("musym"), iName = head.IndexOf("muname"), iHsg = head.IndexOf("hydgrp"), iArea = head.IndexOf("area");
            var rows = new List<(string Sym, string Name, string Hsg, double A)>();
            foreach (var r in t.EnumerateArray().Skip(1))
            {
                var c = r.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? "" : x.ToString()).ToList();
                double.TryParse(c[iArea], NumberStyles.Float, Inv, out double a);
                rows.Add((c[iSym], c[iName], c[iHsg], a));
            }
            double tot = rows.Sum(r => r.A);
            if (tot <= 0) return (null, "soil map units returned no area");
            return (rows.OrderByDescending(r => r.A).Select(r => new SoilUnit(r.Sym, r.Name, r.Hsg, r.A / tot)).ToList(), null);
        }
        catch (Exception ex) { return (null, "unreadable Soil Data Access response: " + ex.Message); }
    }

    // ---------------- Elevation ----------------

    public static string ElevationUrl(IEnumerable<GeoPoint> pts)
    {
        string geom = "{\"points\":[" + string.Join(",", pts.Select(p => $"[{F(p.Lon)},{F(p.Lat)}]")) + "],\"spatialReference\":{\"wkid\":4326}}";
        return $"{ElevationService}?geometry={Uri.EscapeDataString(geom)}&geometryType=esriGeometryMultipoint&returnFirstValueOnly=true" +
               "&interpolation=RSP_BilinearInterpolation&f=json";
    }

    /// <summary>Elevations in feet by point index (the service returns metres); NaN where there is no value.</summary>
    public static (double[]? Elevations, string? Error) ParseElevations(string json, int count)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err)) return (null, err.ToString());
            var z = Enumerable.Repeat(double.NaN, count).ToArray();
            foreach (var s in doc.RootElement.GetProperty("samples").EnumerateArray())
            {
                int id = s.GetProperty("locationId").GetInt32();
                var v = s.GetProperty("value");
                string txt = v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString();
                if (id >= 0 && id < count && double.TryParse(txt, NumberStyles.Float, Inv, out double m) && m > -1000) z[id] = m / 0.3048;
            }
            return z.Any(double.IsFinite) ? (z, null) : (null, "no elevations returned");
        }
        catch (Exception ex) { return (null, "unreadable elevation response: " + ex.Message); }
    }

    // ---------------- Rainfall ----------------

    public static string PfdsUrl(double lat, double lon) => string.Format(Inv, PfdsCsv, lat.ToString("F4", Inv), lon.ToString("F4", Inv));

    /// <summary>
    /// Depths (in) by recurrence interval (yr) and duration label ("5-min", "1-hr", "24-hr", …) from the PFDS CSV:
    /// the line "by duration for ARI (years):, 1,2,5,…" gives the columns, then one "label:, v, v, …" line per duration.
    /// </summary>
    public static (Dictionary<int, Dictionary<string, double>>? Depths, string? Error) ParsePfds(string csv)
    {
        var lines = csv.Replace("\r", "").Split('\n');
        int h = Array.FindIndex(lines, l => l.Contains("ARI", StringComparison.OrdinalIgnoreCase) && l.Contains(':'));
        if (h < 0) return (null, "no \"by duration for ARI\" line in the PFDS text");
        var aris = lines[h][(lines[h].IndexOf(':') + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, Inv, out int y) ? y : -1).ToList();
        var d = new Dictionary<int, Dictionary<string, double>>();
        foreach (var y in aris.Where(y => y > 0)) d[y] = new(StringComparer.OrdinalIgnoreCase);
        for (int i = h + 1; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            int c = l.IndexOf(':');
            if (c <= 0) { if (d.Values.Any(x => x.Count > 0)) break; continue; }
            string label = l[..c].Trim();
            if (!label.Contains("min") && !label.Contains("hr") && !label.Contains("day")) { if (d.Values.Any(x => x.Count > 0)) break; continue; }
            var vals = l[(c + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int k = 0; k < Math.Min(vals.Length, aris.Count); k++)
                if (aris[k] > 0 && double.TryParse(vals[k], NumberStyles.Float, Inv, out double v)) d[aris[k]][label] = v;
        }
        return d.Values.Any(x => x.ContainsKey("24-hr")) ? (d, null) : (null, "no 24-hr depths in the PFDS text");
    }

    // ---------------- helpers ----------------

    private static string F(double v) => v.ToString("F7", Inv);

    private static List<IReadOnlyList<GeoPoint>> ReadRings(JsonElement geometry)
    {
        var list = new List<IReadOnlyList<GeoPoint>>();
        if (!geometry.TryGetProperty("rings", out var rings)) return list;
        foreach (var r in rings.EnumerateArray())
            list.Add(r.EnumerateArray().Select(p => new GeoPoint(p[1].GetDouble(), p[0].GetDouble())).ToList());
        return list;
    }

    private static double GeoArea(IReadOnlyList<GeoPoint> r) => SiteGeometry.Shoelace(r.Select(p => (p.Lon, p.Lat)).ToList());
}
