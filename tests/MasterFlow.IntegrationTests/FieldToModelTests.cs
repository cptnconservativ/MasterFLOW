using MasterFlow.Application.Field;
using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Field-to-model loop (premium, D-250): as-built measurements and photo GPS update the network; HGL re-run and certified.</summary>
public sealed class FieldToModelTests : IDisposable
{
    private static readonly FieldToModelTool Tool = new();
    private static readonly StatePlaneZone South = StatePlaneZone.Find("sd-south")!;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mf-photos-" + Guid.NewGuid().ToString("N"));

    public FieldToModelTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly (double N, double E) O = South.Project(44.0805, -103.231);

    private static StormNetwork Net()
    {
        NetworkPipe Pipe(string n, string f, string t, double d, double l, double ui, double di, double q) => new() { Name = n, From = f, To = t, Rise = d, Span = d, Length = l, UpstreamInvert = ui, DownstreamInvert = di, ManningN = 0.013, Discharge = q };
        NetworkStructure S(string n, double rim, double dn, double de) => new() { Name = n, Kind = "Manhole", RimElevation = rim, Northing = O.N + dn, Easting = O.E + de };
        return new StormNetwork
        {
            Name = "Main St", Structures = [S("OUT", 105, 0, 0), S("MH-2", 107.5, 0, 200), S("CB-3", 108.0, 150, 200), S("CB-4", 107.8, 0, 260)],
            Tailwater = TailwaterRule.Elevation, TailwaterElevation = 101.5, Criteria = NetworkCriteria.RapidCity2012,
            Pipes = [Pipe("P1", "MH-2", "OUT", 2.5, 200, 100.80, 100.00, 20), Pipe("P2", "CB-3", "MH-2", 2.0, 150, 102.00, 101.10, 12), Pipe("P3", "CB-4", "MH-2", 1.5, 60, 101.80, 101.30, 8)],
        };
    }

    private static DesignToolContext Ctx(StormNetwork n) => new(UnitSystem.USCustomary, n, StormNetworkSolver.Solve(n)) { Networks = [(n, StormNetworkSolver.Solve(n))] };

    private const string Shots = """
        [As-built]
        Structure	Rim	Pipe	Measure-down
        OUT	105.02	P1	5.04
        MH-2	107.46	P1	6.95
        MH-2		P2	6.32
        MH-2		P3	6.18
        CB-3	108.05	P2	5.83
        CB-4	107.81	P3	6.24
        """;

    [Fact]
    public void MeasureDowns_UpdateInvertsAndCertify()
    {
        var r = Tool.Run("coordinate system = sd-south\n" + Shots, Ctx(Net()));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var inv = r.Tables.Single(t => t.Title.StartsWith("Pipe inverts", StringComparison.Ordinal));
        var p1up = inv.Rows.Single(x => (string)x[0]! == "P1" && (string)x[1]! == "upstream");
        Assert.Equal(100.51, (double)p1up[4]!, 6);           // 107.46 − 6.95
        Assert.Equal("LOW", p1up[7]);
        Assert.Equal("HIGH", inv.Rows.Single(x => (string)x[0]! == "P2" && (string)x[1]! == "upstream")[7]);
        Assert.Contains(r.Summary, s => s.StartsWith("6 of 6 pipe ends and 4 of 4 rims measured; 3 invert(s) differ", StringComparison.Ordinal));
        Assert.Contains(r.Summary, s => s.StartsWith("Certification: as constructed", StringComparison.Ordinal));
        // P3 flattened from 0.83 % to 0.48 %: now below the design flow at full.
        var slopes = r.Tables.Single(t => t.Title.StartsWith("Pipe slopes", StringComparison.Ordinal));
        Assert.Equal("CHANGED, OVER CAPACITY", slopes.Rows.Single(x => (string)x[0]! == "P3")[7]);
        // The updated network carries the as-built values; HGL re-run from them.
        var built = r.UpdatedNetwork!;
        Assert.Equal(100.51, built.Pipes.Single(x => x.Name == "P1").UpstreamInvert, 6);
        Assert.Equal(108.05, built.Structures.Single(x => x.Name == "CB-3").RimElevation!.Value, 6);
        var hgl = StormNetworkSolver.Solve(built).Structures.Single(x => x.Structure.Name == "CB-3").Hgl;
        Assert.Equal(hgl, (double)r.Tables[0].Rows.Single(x => (string)x[0]! == "CB-3")[5]!, 6);
        Assert.Single(r.Charts);
    }

    [Fact]
    public void HighHgl_FailsCertificationAndNamesTheStructure()
    {
        var n = Net() with { TailwaterElevation = 104.4 };
        var r = Tool.Run(Shots, Ctx(n));
        Assert.Contains(r.Summary, s => s.StartsWith("As-built HGL is less than 1.00 ft below the rim at", StringComparison.Ordinal) && s.Contains("OUT"));
        Assert.Contains(r.Tables[0].Rows, x => x[^1] is "FAILS");
    }

    [Fact]
    public void Partial_SaysNotComplete_AndMeasureDownWithoutRimUsesDesignRim()
    {
        var r = Tool.Run("[As-built]\nStructure, Pipe, Measure-down\nMH-2, P1, 6.95\n", Ctx(Net()));
        Assert.Contains(r.Summary, s => s.StartsWith("Not a complete as-built: 5 pipe end(s) and 4 rim(s)", StringComparison.Ordinal));
        Assert.Contains(r.Messages, m => m.Code == "W-FM-010");
        Assert.Equal(100.55, r.UpdatedNetwork!.Pipes.Single(x => x.Name == "P1").UpstreamInvert, 6);   // 107.5 − 6.95
        Assert.Equal("NOT MEASURED", r.Tables.Single(t => t.Title.StartsWith("Pipe inverts", StringComparison.Ordinal)).Rows.Single(x => (string)x[0]! == "P2" && (string)x[1]! == "upstream")[7]);
    }

    [Fact]
    public void PositionOnly_MatchesNearestStructure()
    {
        // A surveyed invert shot given by northing/easting 3 ft from CB-4.
        string row = FormattableString.Invariant($"[As-built]\nStructure, Northing, Easting, Pipe, Invert\n, {O.N + 2:0.00}, {O.E + 262:0.00}, P3, 101.62\n, {O.N + 400:0.00}, {O.E:0.00}, P1, 99\n");
        var r = Tool.Run(row, Ctx(Net()));
        Assert.Equal(101.62, r.UpdatedNetwork!.Pipes.Single(x => x.Name == "P3").UpstreamInvert, 6);
        Assert.Contains(r.Messages, m => m.Code == "W-FM-004");    // the second shot is 400 ft from anything
    }

    [Fact]
    public void Photos_MatchByFileNameThenGps_AndFlagMisplacedStructure()
    {
        double DLat(double ft) => ft / 364.3 * 0.001;
        double DLon(double ft) => ft / (364.3 * Math.Cos(44.0805 * Math.PI / 180)) * 0.001;
        File.WriteAllBytes(Path.Combine(_dir, "MH-2 looking north.jpg"), ExifGps.Fake(44.0805 + DLat(3), -103.231 + DLon(201), new DateTime(2026, 11, 20, 9, 41, 0)));
        File.WriteAllBytes(Path.Combine(_dir, "IMG_2231.jpg"), ExifGps.Fake(44.0805 + DLat(152), -103.231 + DLon(199), new DateTime(2026, 11, 20, 9, 52, 0)));
        File.WriteAllBytes(Path.Combine(_dir, "CB-4.jpg"), ExifGps.Fake(44.0805 + DLat(60), -103.231 + DLon(260), new DateTime(2026, 11, 20, 10, 2, 0)));   // 60 ft off
        File.WriteAllBytes(Path.Combine(_dir, "IMG_2232.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "x");
        var r = Tool.Run($"coordinate system = sd-south\nphotos = {_dir}\n" + Shots, Ctx(Net()));
        var log = r.Tables.Single(t => t.Title == "Photo log");
        Assert.Equal(4, log.Rows.Count);
        var gps = log.Rows.Single(x => (string)x[0]! == "IMG_2231.jpg");
        Assert.Equal("CB-3", gps[6]); Assert.Equal("GPS", gps[8]);
        Assert.InRange((double)gps[7]!, 0, 10);   // grid convergence (~2°) and the rough ft→degree conversion
        Assert.Equal("file name", log.Rows.Single(x => (string)x[0]! == "MH-2 looking north.jpg")[8]);
        Assert.Equal("no GPS in the photo", log.Rows.Single(x => (string)x[0]! == "IMG_2232.jpg")[8]);
        Assert.Contains(r.Messages, m => m.Code == "W-FM-011" && m.Text.Contains("CB-4"));
        Assert.Contains(r.Messages, m => m.Code == "I-FM-001");
    }

    [Fact]
    public void ExifReader_ReadsFakeAndRejectsNonJpeg()
    {
        var tag = ExifGps.Read(ExifGps.Fake(-33.5, 151.25, new DateTime(2026, 1, 2, 3, 4, 5), 12.5), "a.jpg")!;
        Assert.Equal(-33.5, tag.Latitude!.Value, 6);
        Assert.Equal(151.25, tag.Longitude!.Value, 6);
        Assert.Equal(12.5, tag.Altitude!.Value, 6);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5), tag.Taken);
        Assert.Null(ExifGps.Read("PNG"u8.ToArray(), "x.png"));
    }

    [Fact]
    public void StatePlane_OriginAndScale()
    {
        var o = South.Project(42 + 20 / 60.0, -(100 + 20 / 60.0), feet: false);
        Assert.Equal(0, o.Northing, 3);
        Assert.Equal(600000, o.Easting, 3);
        // 0.001° of latitude near Rapid City is about 111.1 m on the ground; LCC scale there is within 1:10,000 of 1.
        var a = South.Project(44.0805, -103.231, false); var b = South.Project(44.0815, -103.231, false);
        double d = Math.Sqrt(Math.Pow(b.Northing - a.Northing, 2) + Math.Pow(b.Easting - a.Easting, 2));
        Assert.InRange(d, 111.07 * 0.9999, 111.12 * 1.0001);
        // Rapid City lies at about N 650,000, E 1,207,000 US ft in SD South.
        var rc = South.Project(44.0805, -103.231);
        Assert.InRange(rc.Northing, 640000, 660000);
        Assert.InRange(rc.Easting, 1195000, 1220000);
    }

    [Fact]
    public void Errors()
    {
        Assert.Contains(Tool.Run("network = Elm\n" + Shots, Ctx(Net())).Messages, m => m.Code == "E-FM-001");
        Assert.Contains(Tool.Run("x = 1\n", Ctx(Net())).Messages, m => m.Code == "E-FM-002");
        Assert.Contains(Tool.Run("[As-built]\nStructure, Rim\nMH-2, abc\n", Ctx(Net())).Messages, m => m.Code == "E-FM-003");
        var r = Tool.Run("[As-built]\nStructure, Rim, Pipe, Measure-down\nMH-9, 1, P1, 1\nCB-3, 108, P3, 5\nCB-3, 108, P8, 5\n", Ctx(Net()));
        Assert.Contains(r.Messages, m => m.Code == "W-FM-002");
        Assert.Equal(2, r.Messages.Count(m => m.Code == "W-FM-008"));
    }

    [Fact]
    public void SampleRuns_AndIsPremium()
    {
        var ctx = Ctx(Net());
        Assert.True(Tool.Run(Tool.SampleInput(ctx), ctx).Succeeded);
        Assert.Equal("field-to-model", PremiumFeatures.ForTool("field-to-model")?.Id);
    }
}
