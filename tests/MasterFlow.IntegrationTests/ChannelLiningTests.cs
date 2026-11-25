using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>HEC-15 flexible linings: factors, Blodgett n, grass n, normal depth and the lining checks by hand.</summary>
public class ChannelLiningTests
{
    private static readonly ChannelLiningTool Tool = new();

    private static ToolResult Run(string input, UnitSystem u = UnitSystem.USCustomary) => Tool.Run(input, new DesignToolContext(u));

    private static IReadOnlyList<object?> Row(ToolResult r, string name) => r.Tables[0].Rows.Single(x => (string)x[0]! == name);

    [Fact]
    public void Sample_RunsClean()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.False(Run("x = 1", UnitSystem.SI).Succeeded);
    }

    [Theory]
    [InlineData(1.0, 0.77)]
    [InlineData(3.0, 0.868)]
    [InlineData(6.0, 1.0)]
    public void K1_Factor(double z, double k1) => Assert.Equal(k1, ChannelLiningTool.K1(z), 3);

    [Theory]
    [InlineData(1.5, 2.0)]
    [InlineData(5.0, 1.5325)]
    [InlineData(12, 1.05)]
    public void Kb_Factor(double r, double kb) => Assert.Equal(kb, ChannelLiningTool.Kb(r), 4);

    [Fact]
    public void ShieldsTable_Interpolates()
    {
        Assert.Equal((0.047, 1.0), ChannelLiningTool.ShieldsAndSafety(1e4));
        Assert.Equal((0.15, 1.5), ChannelLiningTool.ShieldsAndSafety(3e5));
        var (f, sf) = ChannelLiningTool.ShieldsAndSafety(1.2e5);
        Assert.Equal(0.047 + 0.5 * 0.103, f, 6);
        Assert.Equal(1.25, sf, 6);
    }

    [Fact]
    public void Blodgett_ByHand() =>
        Assert.Equal(0.262 * Math.Pow(2, 1.0 / 6) / (2.25 + 5.23 * Math.Log10(2 / 0.5)), ChannelLiningTool.RiprapN(2, 0.5), 9);

    [Fact]
    public void Recp_NormalDepthAndShear()
    {
        var r = Run("[Reaches]\nName\tQ\tS\tB\tZ\tRc\tLining\tD50\tCn\tCf\tD75\ttau soil\ttau p\tn\nT\t40\t0.05\t2\t3\t\trecp\t\t\t\t\t\t2.0\t0.030");
        var row = Row(r, "T");
        double y = (double)row[3]!;
        double a = (2 + 3 * y) * y, p = 2 + 2 * y * Math.Sqrt(10);
        Assert.Equal(40, 1.486 / 0.03 * a * Math.Pow(a / p, 2.0 / 3) * Math.Sqrt(0.05), 0);
        Assert.InRange((double)row[7]! / (62.4 * y * 0.05), 0.99, 1.01);
        Assert.Equal("FAILS", row[14]);
    }

    [Fact]
    public void Riprap_SizedD50SatisfiesShields()
    {
        var r = Run("[Reaches]\nName\tQ\tS\tB\tZ\tRc\tLining\nR\t60\t0.02\t4\t3\t\triprap");
        var row = Row(r, "R");
        Assert.Equal("SIZED", row[14]);
        double y = (double)row[3]!, tau = 62.4 * y * 0.02;
        string req = (string)row[13]!;
        double bottom = double.Parse(req.Split("bottom ")[1].Split(',')[0], System.Globalization.CultureInfo.InvariantCulture);
        var (fs, sf) = ChannelLiningTool.ShieldsAndSafety(Math.Sqrt(32.2 * y * 0.02) * bottom / 12 / 1.217e-5);
        double need = sf * tau / (fs * (165 - 62.4)) * 12;
        Assert.InRange(bottom / need, 0.98, 1.02);
        // Given a smaller stone it fails, a larger one passes.
        Assert.Equal("FAILS", Row(Run("[Reaches]\nName\tQ\tS\tB\tZ\tRc\tLining\tD50\nR\t60\t0.02\t4\t3\t\triprap\t2"), "R")[14]);
        Assert.Equal("OK", Row(Run("[Reaches]\nName\tQ\tS\tB\tZ\tRc\tLining\tD50\nR\t60\t0.02\t4\t3\t\triprap\t12"), "R")[14]);
    }

    [Fact]
    public void Grass_EffectiveShearByHand()
    {
        var r = Run(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        var row = Row(r, "Swale A grass");
        double y = (double)row[3]!, n = (double)row[6]!;
        double a = (4 + 4 * y) * y, p = 4 + 2 * y * Math.Sqrt(17), rr = a / p;
        Assert.InRange(0.213 * 0.142 * Math.Pow(62.4 * rr * 0.02, -0.4) / n, 0.99, 1.01);
        double tauE = 62.4 * y * 0.02 * 0.1 * Math.Pow(0.016 / n, 2);
        double shown = double.Parse(((string)row[13]!).Split("τe ")[1].TrimEnd(')'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(shown / tauE, 0.95, 1.05);
    }
}
