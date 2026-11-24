using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Regional regression: power equations, prediction intervals, gage weighting and the Sauer transfer by hand.</summary>
public class RegionalRegressionTests
{
    private static readonly RegionalRegressionTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    private static ToolResult Run(string input)
    {
        var r = Tool.Run(input, Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Sample_Runs() => Run(Tool.SampleInput(Us));

    [Fact]
    public void PowerEquationAndInterval()
    {
        // Q100 = 84·12.5^0.52·18^0.79 = 84 × 3.7177 × 9.8152 = 3065; SEP 70 % → S = √ln(1.49)/ln 10 = 0.63150/2.30259 = 0.27425.
        var r = Run(Tool.SampleInput(Us));
        var row = r.Tables[0].Rows.Single(x => (double)x[0]! == 100);
        double q = 84 * Math.Pow(12.5, 0.52) * Math.Pow(18, 0.79);
        Assert.Equal(Math.Round(q), (double)row[2]!);
        Assert.Equal(0.27425, RegionalRegressionTool.LogSe(70), 5);
        Assert.Equal(Math.Round(q * Math.Pow(10, 1.645 * RegionalRegressionTool.LogSe(70))), (double)row[4]!);
    }

    [Fact]
    public void WeightingAndTransfer()
    {
        // Weighted: N = 35, E = 10 → log Qw = (35 log 1950 + 10 log 3704)/45.
        double qw = RegionalRegressionTool.Weighted(1950, 35, 3704, 10);
        Assert.Equal(Math.Pow(10, (35 * Math.Log10(1950) + 10 * Math.Log10(3704)) / 45), qw, 6);
        // Sauer: at the gage the transfer returns the weighted ratio; at ΔA = Ag/2 it returns the regression.
        Assert.Equal(1000 * 0.6, RegionalRegressionTool.Transfer(1000, 0.6, 18, 18), 9);
        Assert.Equal(1000, RegionalRegressionTool.Transfer(1000, 0.6, 18, 9), 9);
        // Midway (ΔA/Ag = 0.25): R − 0.5(R − 1) = 0.8.
        Assert.Equal(800, RegionalRegressionTool.Transfer(1000, 0.6, 20, 15), 9);
    }

    [Fact]
    public void Warnings_OutOfRangeAndFarFromGage()
    {
        string input = Tool.SampleInput(Us).Replace("A, 12.5, 0.5, 2000", "A, 3000, 0.5, 2000");
        var r = Run(input);
        Assert.Contains(r.Messages, m => m.Code == "W-RR-001");
        Assert.Contains(r.Messages, m => m.Code == "W-RR-003");
        Assert.All(r.Tables[1].Rows, x => Assert.Null(x[6]));
    }

    [Fact]
    public void Errors()
    {
        var r = Tool.Run("[Variables]\nName, Value\nA, 10\n[Equations]\nT, C, A, Slope\n100, 50, 0.6, 0.3\n", Us);
        Assert.Contains(r.Messages, m => m.Code == "E-RR-003");
        Assert.Contains(Tool.Run("x = 1\n", Us).Messages, m => m.Code == "E-RR-001");
    }
}
