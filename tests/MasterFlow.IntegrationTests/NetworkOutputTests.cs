using System.IO.Compression;
using System.Text;
using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Persistence;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>
/// Storm network outputs: the text grids, the profile, the PDF/HTML report and the Excel sheets all carry
/// the solver's numbers. Reference HGLs (validation/storm_hgl_output.txt, network A): P1 upstream 104.063193,
/// MH-2 104.513873, CB-3 107.367201.
/// </summary>
public class NetworkOutputTests
{
    private static NetworkPipe Pipe(string name, string from, string to, double d, double l, double up, double dn, double q) =>
        new() { Name = name, From = from, To = to, Rise = d, Span = d, Length = l, UpstreamInvert = up, DownstreamInvert = dn, ManningN = 0.013, Discharge = q };

    internal static StormNetwork NetA() => new()
    {
        Name = "Main St",
        Structures =
        [
            new() { Name = "OUT", Kind = "Outlet", RimElevation = 105 },
            new() { Name = "MH-2", Kind = "Manhole", RimElevation = 106.0 },
            new() { Name = "CB-3", Kind = "Inlet", RimElevation = 106.5 },
            new() { Name = "CB-4", Kind = "Inlet", RimElevation = 104.0 },
        ],
        Tailwater = TailwaterRule.Elevation, TailwaterElevation = 102.50,
        Pipes = [Pipe("P1", "MH-2", "OUT", 2.0, 200, 100.80, 100.00, 20), Pipe("P2", "CB-3", "MH-2", 1.5, 150, 102.00, 101.10, 12), Pipe("P3", "CB-4", "MH-2", 1.25, 60, 101.80, 101.30, 8)],
        Criteria = NetworkCriteria.RapidCity2012,
    };

    private static readonly ProjectInfo Info = new() { Name = "Rapid City test", ProjectNumber = "26-001", PreparedBy = "KH" };

    [Fact]
    public void TextGrids_RoundTrip()
    {
        var n = NetA() with { Pipes = [.. NetA().Pipes, new NetworkPipe { Name = "E1", From = "CB-9", To = "MH-2", Shape = NetworkPipeShape.Elliptical, Rise = 19.0 / 12, Span = 30.0 / 12, Length = 40, UpstreamInvert = 101.9, DownstreamInvert = 101.5, Discharge = 6 }] };
        var (pipes, pe) = NetworkText.ParsePipes(NetworkText.FormatPipes(n), n.Units);
        Assert.Empty(pe);
        Assert.Equal(n.Pipes.Count, pipes.Count);
        for (int i = 0; i < pipes.Count; i++)
        {
            Assert.Equal(n.Pipes[i].Name, pipes[i].Name);
            Assert.Equal(n.Pipes[i].Shape, pipes[i].Shape);
            Assert.Equal(n.Pipes[i].Rise, pipes[i].Rise, 9);
            Assert.Equal(n.Pipes[i].Span, pipes[i].Span, 9);
            Assert.Equal(n.Pipes[i].UpstreamInvert, pipes[i].UpstreamInvert, 9);
            Assert.Equal(n.Pipes[i].Discharge, pipes[i].Discharge, 9);
        }
        var withLoss = n with { Structures = [n.Structures[0], n.Structures[1] with { LossMethod = StructureLossMethod.StandardK, JunctionK = 0.8, Inflow = 4.5 }] };
        var (structs, se) = NetworkText.ParseStructures(NetworkText.FormatStructures(withLoss));
        Assert.Empty(se);
        Assert.Equal(StructureLossMethod.StandardK, structs[1].LossMethod);
        Assert.Equal(0.8, structs[1].JunctionK);
        Assert.Equal(106.0, structs[1].RimElevation);
        Assert.Equal(4.5, structs[1].Inflow);
        var (q2, q2e) = NetworkText.ParsePipes(NetworkText.FormatPipes(n with { Pipes = [n.Pipes[0] with { MinorDischarge = 2.5 }] }), n.Units);
        Assert.Empty(q2e);
        Assert.Equal(2.5, q2[0].MinorDischarge);
        var (s2, _) = NetworkText.ParseStructures("MH\tManhole\t100\t\t\t3\t1.5");
        Assert.Equal(1.5, s2[0].MinorInflow);
        Assert.Null(structs[0].Inflow);
        // A blank Q means "accumulate".
        var (blank, be) = NetworkText.ParsePipes("P1\tA\tB\tCircular\t24\t\t200\t101\t100\t0.013\t", UnitSystem.USCustomary);
        Assert.Empty(be);
        Assert.True(double.IsNaN(blank[0].Discharge));
    }

    [Fact]
    public void TextGrids_ReportBadCells()
    {
        var (_, pe) = NetworkText.ParsePipes("P1\tA\tB\tCircular\t24\t\t200\tx\t100\t0.013\t5", UnitSystem.USCustomary);
        Assert.Contains(pe, e => e.Contains("upstream invert \"x\""));
        var (_, se) = NetworkText.ParseStructures("MH\tManhole\t100\tSideways\t");
        Assert.Contains(se, e => e.Contains("loss method \"Sideways\""));
    }

    [Fact]
    public void Profile_StationsFromOutfall_WithStructureSteps()
    {
        var r = StormNetworkSolver.Solve(NetA());
        Assert.Equal(["CB-3", "CB-4"], NetworkProfile.PathStarts(r)); // longest path first
        var p = NetworkProfile.Build(r, "CB-3")!;
        Assert.Equal("OUT", p.To);
        Assert.Equal(350, p.Length, 9);
        Assert.Equal([0.0, 200.0, 350.0], p.Structures.Select(s => s.Station));
        Assert.Equal(102.50, p.Hgl[0].Z, 3);
        // At MH-2 (station 200) the HGL steps from the pipe's upstream end to the structure level.
        var at200 = p.Hgl.Where(q => Math.Abs(q.S - 200) < 1e-9).Select(q => q.Z).ToList();
        Assert.Contains(at200, z => Math.Abs(z - 104.063193) < 0.002);
        Assert.Contains(at200, z => Math.Abs(z - 104.513873) < 0.002);
        Assert.Equal(107.367201, p.Hgl[^1].Z, 2);
        // Stations never decrease along the series.
        for (int i = 1; i < p.Hgl.Count; i++) Assert.True(p.Hgl[i].S >= p.Hgl[i - 1].S - 1e-9);
        // CB-3 rim 106.5 − 1 ft criterion = 105.5 < HGL 107.37: fails; MH-2 106 − 1 = 105 > 104.51: meets.
        Assert.False(p.Structures.Single(s => s.Name == "CB-3").MeetsCriteria);
        Assert.True(p.Structures.Single(s => s.Name == "MH-2").MeetsCriteria);
        Assert.Equal(105.5, p.Structures.Single(s => s.Name == "CB-3").CriteriaLine);
    }

    [Fact]
    public void Readings_Every50Ft_WithRapidCityLimit()
    {
        var r = StormNetworkSolver.Solve(NetA());
        var p = NetworkProfile.Build(r, "CB-3")!;
        Assert.Equal(50, p.ReadingInterval);
        Assert.Equal("Rapid City limit", p.LimitLabel);
        var rd = p.Readings();
        Assert.Equal([0.0, 50, 100, 150, 200, 250, 300, 350], rd.Select(x => x.Station));
        // Station 0: outlet HGL = tailwater 102.50 (pipe full); rim 105 − 1 ft.
        Assert.Equal(102.50, rd[0].Hgl, 3);
        Assert.Equal(104.0, rd[0].Limit!.Value, 9);
        // Station 100: P1 is under pressure, so the HGL is linear between 102.50 and 104.063193 (independent values);
        // ground straight between rims 105 and 106 → 105.5, limit 104.5.
        Assert.Equal((102.50 + 104.063193) / 2, rd[2].Hgl, 2);
        Assert.Equal(105.5, rd[2].Ground!.Value, 9);
        Assert.Equal(104.5, rd[2].Limit!.Value, 9);
        Assert.True(rd[2].MeetsCriteria);
        // Station 200 (MH-2): the structure level on the upstream side; 350 (CB-3) fails.
        Assert.Equal(104.513873, rd[4].Hgl, 2);
        Assert.Equal(105.0, rd[4].Limit!.Value, 9);
        Assert.Equal(107.367201, rd[7].Hgl, 2);
        Assert.False(rd[7].MeetsCriteria);
        Assert.All(rd, x => Assert.True(x.Egl >= x.Hgl - 1e-9));
        // SI networks read every 15 m; no criterion → no limit.
        var si = NetworkProfile.Build(StormNetworkSolver.Solve(NetA() with { Units = UnitSystem.SI, Criteria = new() }), "CB-3")!;
        Assert.Equal(15, si.ReadingInterval);
        Assert.All(si.Readings(), x => Assert.Null(x.Limit));
    }

    [Fact]
    public void Tables_ShowCriteriaCheck()
    {
        var r = StormNetworkSolver.Solve(NetA());
        var rows = NetworkTables.StructureRows(r);
        Assert.Equal("FAILS", rows.Single(x => x[0] == "CB-3")[NetworkTables.StructureCheckColumn]);
        Assert.Equal("OK", rows.Single(x => x[0] == "MH-2")[NetworkTables.StructureCheckColumn]);
        Assert.Equal("104.51", rows.Single(x => x[0] == "MH-2")[4]);
        Assert.Contains("Rapid City", NetworkTables.CriteriaText(r.Network, r.Constants));
    }

    [Fact]
    public void Pdf_IsValid_AndCarriesValues()
    {
        var r = StormNetworkSolver.Solve(NetA());
        var pdf = NetworkReport.ToPdf(Info, r, new ReportOptions { Timestamp = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero) });
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Contains("%%EOF", Encoding.Latin1.GetString(pdf[^32..]));
        Assert.True(pdf.Length > 5000);
    }

    [Fact]
    public void Html_HasProfileSvg_AndTables()
    {
        var r = StormNetworkSolver.Solve(NetA());
        var html = NetworkReport.ToHtml(Info, r);
        Assert.Contains("<svg", html);
        Assert.Contains("HGL 104.51", html);
        Assert.Contains("107.37", html);
        Assert.Contains("FAILS", html);
        Assert.Contains("Rapid City", html);
        Assert.Contains("profile from CB-4", html); // one profile per path
        Assert.Contains("Rapid City limit", html);  // reading band under each profile
        Assert.Contains(">105.50<", html);          // CB-3 limit: rim 106.5 − 1 ft
    }

    [Fact]
    public void Workbook_HasNumericNetworkSheets()
    {
        var r = StormNetworkSolver.Solve(NetA());
        var bytes = XlsxWriter.ToBytes(ProjectWorkbook.Build(Info, [], [], null, [r]));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        string wb = new StreamReader(zip.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd();
        Assert.Contains("Main St pipes", wb);
        Assert.Contains("Main St structures", wb);
        Assert.Contains("Main St profile", wb);
        var all = string.Concat(zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/")).Select(e => new StreamReader(e.Open()).ReadToEnd()));
        Assert.Contains("<v>104.51", all);   // MH-2 HGL as a number, not text
        Assert.Contains("Main St readings", wb);
        Assert.Contains("FAILS", all);
    }

    [Fact]
    public void Project_SavesNetworks()
    {
        var p = new Project { Networks = [NetA()] };
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(p));
        var n = Assert.Single(back.Networks);
        Assert.Equal(NetA().Pipes, n.Pipes);
        Assert.Equal(NetA().Structures, n.Structures);
        Assert.Equal(NetA().Criteria, n.Criteria);
        Assert.Equal(102.5, n.TailwaterElevation);
        Assert.All(n.Pipes, x => Assert.True(double.IsNaN(x.MinorDischarge)));
    }
}
