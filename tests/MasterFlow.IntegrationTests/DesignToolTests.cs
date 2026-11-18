using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using MasterFlow.Reporting;

namespace MasterFlow.IntegrationTests;

/// <summary>Design tools framework (every tool's sample must run cleanly) and the Rapid City rational method.</summary>
public class DesignToolTests
{
    internal static DesignToolContext Ctx(UnitSystem u = UnitSystem.USCustomary)
    {
        var n = NetworkOutputTests.NetA() with { Units = u };
        return new DesignToolContext(u, n, StormNetworkSolver.Solve(n));
    }

    public static TheoryData<string> ToolIds() { var d = new TheoryData<string>(); foreach (var t in DesignTools.All) d.Add(t.Id); return d; }

    [Theory]
    [MemberData(nameof(ToolIds))]
    public void EveryToolSample_RunsWithoutErrors_AndRenders(string id)
    {
        var tool = DesignTools.Find(id)!;
        var ctx = Ctx();
        var r = tool.Run(tool.SampleInput(ctx), ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.NotEmpty(r.Tables);
        Assert.All(r.Tables, t => Assert.All(t.Rows, row => Assert.True(row.Count <= t.Headers.Count, $"{t.Title}: row wider than header")));
        Assert.Contains(r.Title, DesignTools.ToText(r));
        Assert.Contains("<table>", ToolReport.ToHtml(new ProjectInfo { Name = "P" }, r, "x", "test"));
        Assert.True(XlsxWriter.ToBytes(ToolReport.ToSheets(r)).Length > 500);
        Assert.Equal(tool.Id, DesignTools.All.Single(t => t.Name == tool.Name).Id);
    }

    [Fact]
    public void ToolInput_ParsesParametersAndTables()
    {
        var p = ToolInput.Parse("a = 1\n# c\n[T]\nx\ty\n1\t2\n[U]\nk = 3\nh1,h2\nv1,v2\n");
        Assert.Equal(1, p.Number("a", 0));
        Assert.Equal(3, p.Number("U.k", 0));
        Assert.Equal(["x", "y"], p.Header("T"));
        Assert.Equal(["v1", "v2"], p.Rows("U").Single());
        Assert.Empty(p.Errors);
        p = ToolInput.Parse("loose line");
        Assert.Single(p.Errors);
    }

    // ---- Rapid City rational method: hand values from IDCM Tables 4-1/4-2 and Eqs. 4-2-1 to 4-2-5.

    [Fact]
    public void Intensity_FromTable42_TabulatedAndInterpolated()
    {
        Assert.Equal(0.53 / (5.0 / 60), RapidCityRational.Intensity(10, 5), 9);          // 6.36 in/hr
        Assert.Equal(2.95, RapidCityRational.Intensity(100, 60), 9);
        // 10 min, 10-yr: log-log between 5 min (0.53 in.) and 15 min (1.04 in.)
        double p = 0.53 * Math.Pow(1.04 / 0.53, Math.Log(2) / Math.Log(3));
        Assert.Equal(p / (10.0 / 60), RapidCityRational.Intensity(10, 10), 9);
        Assert.InRange(RapidCityRational.Intensity(10, 10), 4.86, 4.87);
        Assert.True(double.IsNaN(RapidCityRational.Intensity(10, 2000)));
    }

    [Fact]
    public void Basin_HandCalculation_MinimumTc_And_CompositeC()
    {
        // A3: 0.4 ac paved streets, L 40 ft, S 2 %: ti = 1.87(1.1−0.88)√40·2^−0.33 = 2.07 min → urban minimum 5 min.
        // A1: 2.4 ac SFR + 0.6 ac paved: C10 = (0.50·2.4 + 0.90·0.6)/3 = 0.58; C5 = (0.45·2.4+0.88·0.6)/3 = 0.536.
        var tool = new RationalMethodTool();
        string input = """
            urban = yes
            [Basins]
            Basin	Structure	Land use	L	S	tt	Flow L
            A1	CB-3	Single Family Residential: 2.4; Paved Streets: 0.6	120	2.0	4.0	900
            A3	CB-4	Paved Streets: 0.4	40	2.0	0	300
            """;
        var r = tool.Run(input, Ctx());
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var rows = r.Tables[0].Rows;
        var a3 = rows.Single(x => (string)x[0]! == "A3");
        Assert.Equal(2.0700, RapidCityRational.OverlandTime(0.88, 40, 2), 3);
        Assert.Equal(5.0, (double)a3[7]!, 9);
        Assert.Equal("minimum 5 min", a3[8]);
        // columns: ... C2,i2,Q2 (9..11), C10,i10,Q10 (12..14), C100,i100,Q100 (15..17)
        Assert.Equal(0.90 * (0.53 / (5.0 / 60)) * 0.4, (double)a3[14]!, 9);
        var a1 = rows.Single(x => (string)x[0]! == "A1");
        Assert.Equal(0.58, (double)a1[12]!, 9);
        double c5 = (0.45 * 2.4 + 0.88 * 0.6) / 3, ti = 1.87 * (1.1 - c5) * Math.Sqrt(120) * Math.Pow(2, -0.33);
        Assert.Equal(ti + 4, (double)a1[7]!, 9);                                              // below the cap 900/180 + 10 = 15
        Assert.Equal(0.58 * RapidCityRational.Intensity(10, ti + 4) * 3.0, (double)a1[14]!, 9);

        // Applied to the network: 10-yr Q as inflow, 2-yr Q as minor inflow.
        var cb4 = r.UpdatedNetwork!.Structures.Single(s => s.Name == "CB-4");
        Assert.Equal(Math.Round((double)a3[14]!, 3), cb4.Inflow!.Value, 9);
        Assert.Equal(Math.Round((double)a3[11]!, 3), cb4.MinorInflow!.Value, 9);
        Assert.Equal("2-yr", r.UpdatedNetwork.MinorStormName);
    }

    [Fact]
    public void UrbanCap_Governs_ForLongOverland()
    {
        var r = new RationalMethodTool().Run("[Basins]\nB\tS\tU\tL\tS\ttt\tFL\nX\t\tLawns, Clay Soil: 5\t600\t0.5\t10\t900", Ctx());
        var row = r.Tables[0].Rows.Single();
        Assert.Equal(900.0 / 180 + 10, (double)row[7]!, 9);
        Assert.Equal("L/180 + 10", row[8]);
        Assert.Null(r.UpdatedNetwork);   // no structures named
    }

    [Fact]
    public void Errors_AreReported_NotGuessed()
    {
        var tool = new RationalMethodTool();
        Assert.False(tool.Run(tool.SampleInput(Ctx(UnitSystem.SI)), Ctx(UnitSystem.SI)).Succeeded);
        var r = tool.Run("[Basins]\nB\tS\tU\tL\tS\nX\tCB-3\tSwamp: 2\t100\t1", Ctx());
        Assert.False(r.Succeeded);
        Assert.Contains(r.Messages, m => m.Code == "E-RM-006");
        Assert.Empty(r.Tables[0].Rows);
        r = tool.Run("design storm = 25\n[Basins]\nB\tS\tU\tL\tS\nX\tCB-3\tRoofs: 2\t100\t1", Ctx());
        Assert.Contains(r.Messages, m => m.Code == "E-RM-002");
        Assert.Null(r.UpdatedNetwork);
    }
}
