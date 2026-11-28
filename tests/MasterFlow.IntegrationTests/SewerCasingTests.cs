using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sewer main in a casing (IDCM §3.12.11): casing size and wall, chocks, grade tolerance, jacking, pipe cap.</summary>
public class SewerCasingTests
{
    private static readonly SewerCasingTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static string Sample => Tool.SampleInput(Us);
    private static ToolResult Run(string input) => Tool.Run(input, Us);
    private static object? Item(ToolResult r, string startsWith) => r.Tables[0].Rows.First(x => ((string)x[0]!).StartsWith(startsWith))[1];

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Run(Sample);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.DoesNotContain(r.Messages, m => m.Severity == MessageSeverity.Warning);
        Assert.False(Tool.Run(Sample, new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Theory]
    [InlineData(8.625, 0.188)]
    [InlineData(12.75, 0.188)]
    [InlineData(14, 0.282)]
    [InlineData(16, 0.282)]
    [InlineData(18, 0.312)]
    [InlineData(24, 0.406)]
    [InlineData(28, 0.500)]
    [InlineData(30, 0.500)]
    [InlineData(36, 0.531)]
    [InlineData(42, 0.563)]
    public void WallTable_AsPrintedInIdcm(double d, double wall) => Assert.Equal(wall, SewerCasingTool.MinWall(d));

    [Fact]
    public void WallTable_StopsAt42()
    {
        Assert.Null(SewerCasingTool.MinWall(44));
        var r = Run(Sample.Replace("bell OD = 13.60", "bell OD = 40"));
        Assert.Contains(r.Messages, m => m.Code == "W-CS-004");
    }

    [Fact]
    public void CasingSize_IsSmallestStandardOdWhoseIdClearsTheBellBySixInches()
    {
        // Bell 13.60 + 6 = 19.60 in. needed. 20 in. × 0.344 → 19.31 in. ID (too small); 22 in. × 0.375 → 21.25 in. ID.
        var r = Run(Sample);
        Assert.Equal(22.0, Item(r, "Casing OD"));
        Assert.Equal(0.375, Item(r, "Casing wall"));
        Assert.Equal(21.25, Item(r, "Casing ID"));
        // RCP: the next jacking size at or above 19.6 in. is 21 in.
        var c = Run(Sample.Replace("casing material = steel", "casing material = RCP"));
        Assert.Equal(21.0, Item(c, "Casing ID"));
        Assert.Equal(21 / 12.0 + 1, (double)Item(c, "Casing wall")!, 2);
    }

    [Fact]
    public void Chocks_EachSideOfJoint_MidLength_TenFeetMax()
    {
        Assert.Equal((3, 9.0), SewerCasingTool.ChocksPerLength(20, 1, 10));
        Assert.Equal((5, 9.5), SewerCasingTool.ChocksPerLength(40, 1, 10));     // 38 ft between joint chocks → 4 gaps
        Assert.Equal((3, 6.0), SewerCasingTool.ChocksPerLength(14, 1, 10));
        Assert.Contains("18 chocks", Run(Sample).Summary[1]);                     // 120 ft / 20 ft = 6 lengths × 3
    }

    [Fact]
    public void GradeTolerance_AndLongestBore_ByHand()
    {
        var r = Run(Sample);
        // Room = (21.25 − 13.60)/2 = 3.825 in.; less 0.75 in. runner = 3.075 in.; at 0.2 %: 3.075/12/0.002 = 128 ft.
        Assert.Equal(3.08, Item(r, "Grade tolerance"));
        Assert.Equal(128.0, Item(r, "Longest bore"));
        // Unguided auger boring at 1 %: 14.4 in. expected → needs 13.6 + 2(14.4 + 0.75) = 43.9 in. ID → 46 in. casing.
        var u = Run(Sample.Replace("bore accuracy = 0.2", "bore accuracy = 1.0"));
        var w = Assert.Single(u.Messages, m => m.Code == "W-CS-005");
        Assert.Contains("46 in. steel casing", w.Text);
        Assert.Contains("43.9 in.", w.Text);
    }

    [Fact]
    public void JackingForce_AndCapacity_ByHand()
    {
        double f = 200 * Math.PI * 22 / 12 * 120;
        Assert.Equal(f, SewerCasingTool.JackingForce(22, 120, 200, 0), 6);
        var r = Run(Sample);
        Assert.Equal(Math.Round(f / 2000, 1), Item(r, "Jacking force"));
        double cap = 0.5 * 35000 * Math.PI / 4 * (22 * 22 - 21.25 * 21.25);
        Assert.Equal(Math.Round(cap / 2000, 1), Item(r, "Casing capacity"));
        Assert.Contains(Run(Sample.Replace("skin friction = 200", "skin friction = 900")).Messages, m => m.Code == "W-CS-006");
    }

    [Fact]
    public void CasingInverts_FromCarrierInvert_CarrierCentered()
    {
        var r = Run(Sample);
        double drop = (21.25 - 11.10) / 2 / 12 + (11.10 - 9.87) / 2 / 12;
        Assert.Equal(Math.Round(3412.40 - drop, 2), Item(r, "Casing invert, upstream"));
        Assert.Equal(Math.Round(3412.40 - drop - 0.004 * 120, 2), Item(r, "Casing invert, downstream"));
    }

    [Fact]
    public void Shortfalls_AreFlagged()
    {
        Assert.Contains(Run(Sample + "\ncasing OD = 22\ncasing wall = 0.25").Messages, m => m.Code == "W-CS-002");
        Assert.Contains(Run(Sample.Replace("casing yield = 35000", "casing yield = 30000")).Messages, m => m.Code == "W-CS-003");
        Assert.Contains(Run(Sample + "\ncasing OD = 18").Messages, m => m.Code == "W-CS-001");
        Assert.Contains(Run(Sample + "\nrestrained joints = no").Messages, m => m.Code == "W-CS-008");
        Assert.Contains(Run(Sample + "\nmax chock spacing = 12").Messages, m => m.Code == "W-CS-007");
    }

    [Fact]
    public void StreamCrossingCap_ThreeDiametersWide_SpringLineToSixInchesOverCrown()
    {
        var r = Run("protection = cap\ncarrier OD = 12\ncrossing length = 80");
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var rows = r.Tables[0].Rows;
        Assert.Equal(3.0, rows[1][1]);
        Assert.Equal(1.0, rows[2][1]);
        double perFt = (3.0 * 1.0 - Math.PI / 8) / 27;
        Assert.Equal(Math.Round(perFt, 3), rows[4][1]);
    }

    [Fact]
    public void BadInput_IsRefused()
    {
        Assert.Contains(Run(Sample.Replace("bell OD = 13.60", "")).Messages, m => m.Code == "E-CS-002");
        Assert.Contains(Run(Sample.Replace("bell OD = 13.60", "bell OD = 10")).Messages, m => m.Code == "E-CS-003");
        Assert.Contains(Run(Sample.Replace("casing length = 120", "casing length = -5")).Messages, m => m.Code == "E-CS-003");
        Assert.Contains(Run(Sample.Replace("carrier ID = 9.87", "carrier ID = 12")).Messages, m => m.Code == "E-CS-003");
    }
}
