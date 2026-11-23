using System.Globalization;
using MasterFlow.Application.Grading;
using MasterFlow.Application.Licensing;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Grading solver (premium, D-260): finished grades from pads, tie-ins, ADA and drainage constraints, balanced earthwork, conflicts flagged.</summary>
public class GradingSolverTests
{
    private static readonly GradingSolverTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);

    private static double[,] Grid(ToolResult r, string title)
    {
        var t = r.Tables.Single(x => x.Title.StartsWith(title, StringComparison.Ordinal));
        var g = new double[t.Rows.Count, t.Headers.Count - 1];
        for (int i = 0; i < t.Rows.Count; i++)
            for (int j = 1; j < t.Headers.Count; j++)
                g[i, j - 1] = double.Parse(((string)t.Rows[i][j]!).Replace('−', '-'), CultureInfo.InvariantCulture);
        return g;
    }

    private static IReadOnlyList<object?> Req(ToolResult r, string start) => r.Tables.Single(t => t.Title == "Requirements").Rows.Single(x => ((string)x[0]!).StartsWith(start, StringComparison.Ordinal));

    [Fact]
    public void Sample_MeetsEveryRequirementAndBalances()
    {
        var r = Tool.Run(Tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        Assert.Contains("Every requirement is met.", r.Summary);
        Assert.Contains(r.Summary, s => s.Contains("the site balances"));
        var z = Grid(r, "Finished grade"); var e = Grid(r, "Existing ground");
        // Tie-ins hold at existing ground.
        for (int c = 0; c < 21; c++) { Assert.Equal(e[0, c], z[0, c], 2); Assert.Equal(e[15, c], z[15, c], 2); }
        // Pad G4:L8 is flat and within 3458–3460.
        double pad = z[3, 6];
        Assert.InRange(pad, 3457.995, 3460.005);
        for (int rr = 3; rr <= 7; rr++) for (int c = 6; c <= 11; c++) Assert.Equal(pad, z[rr, c], 2);
        // Ground 10 ft from the pad is at least 0.5 ft lower (5 %).
        for (int c = 6; c <= 11; c++) { Assert.True(z[2, c] <= pad - 0.5 + 0.01); Assert.True(z[8, c] <= pad - 0.5 + 0.01); }
        // ADA walk H9:I15: running ≤ 5 %, ≥ 1 % falling south; cross ≤ 2 %.
        for (int rr = 8; rr < 14; rr++)
            for (int c = 7; c <= 8; c++)
            {
                double fall = (z[rr, c] - z[rr + 1, c]) / 10 * 100;
                Assert.InRange(fall, 1 - 0.15, 5 + 0.15);
            }
        for (int rr = 8; rr <= 14; rr++) Assert.True(Math.Abs(z[rr, 7] - z[rr, 8]) / 10 * 100 <= 2 + 0.15);
        Assert.Single(r.Charts);
    }

    [Fact]
    public void ImpossibleSlopeBetweenTieIns_IsFlagged_TieInsHold()
    {
        var input = "spacing = 10\n[Constraints]\nType, Range, Value, Min, Max, Direction\nfix, A1:D1, existing\nfix, A6:D6, 90\nslope, B1:C6, , 1, 5, south\n[Existing]\n"
                    + string.Join("\n", Enumerable.Range(0, 6).Select(i => string.Join(", ", Enumerable.Repeat((100 - 2 * i).ToString(CultureInfo.InvariantCulture), 4))));
        var r = Tool.Run(input, Us);
        Assert.Equal("OK", Req(r, "1. fix")[4]);
        Assert.Equal("OK", Req(r, "2. fix")[4]);
        Assert.StartsWith("CANNOT BE MET", (string)Req(r, "3. slope")[4]!);
        Assert.Contains(r.Tables, t => t.Title == "Conflicts");
        Assert.Contains(r.Messages, m => m.Code == "W-GS-003");
        var z = Grid(r, "Finished grade");
        Assert.Equal(100, z[0, 1], 2); Assert.Equal(90, z[5, 1], 2);
    }

    [Fact]
    public void AdaSlopeOutranksMaxCut()
    {
        // Existing falls 20 % to the south; the walk may fall only 5 % and the cut is limited to 1 ft: the walk wins.
        var input = "spacing = 10\nmax cut = 1\nmax fill = 1\nbalance = no\n[Constraints]\nType, Range, Value, Min, Max, Direction\nslope, A1:B8, , 0, 5, south\n[Existing]\n"
                    + string.Join("\n", Enumerable.Range(0, 8).Select(i => string.Join(", ", Enumerable.Repeat((100 - 2 * i).ToString(CultureInfo.InvariantCulture), 2))));
        var r = Tool.Run(input, Us);
        Assert.Equal("OK", Req(r, "1. slope")[4]);
        Assert.StartsWith("CANNOT BE MET", (string)Req(r, "Max cut")[4]! + (string)Req(r, "Max fill")[4]!, StringComparison.Ordinal);
        Assert.Contains(r.Messages, m => m.Code == "I-GS-001");
    }

    [Fact]
    public void Balance_WithShrinkAndImport()
    {
        // A pad set 1 ft above a flat site needs fill; with no import the solver lowers the pad as far as its Min allows
        // and cuts elsewhere so fill = cut × (1 − shrink).
        string flat = string.Join("\n", Enumerable.Range(0, 10).Select(_ => string.Join(", ", Enumerable.Repeat("100", 10))));
        var r = Tool.Run("spacing = 10\nshrink = 20\n[Constraints]\nType, Range, Value, Min, Max\npad, D4:F6, , 101, 102\n[Existing]\n" + flat, Us);
        var ew = r.Tables.Single(t => t.Title == "Earthwork").Rows;
        double cut = Convert.ToDouble(ew[0][1], CultureInfo.InvariantCulture), fill = Convert.ToDouble(ew[1][1], CultureInfo.InvariantCulture);
        Assert.InRange(fill, cut * 0.8 - 2, cut * 0.8 + 2);
        Assert.Equal(101, Grid(r, "Finished grade")[4, 4], 2);
        // With an import of 100 cy the net fill rises by about 100 cy.
        var r2 = Tool.Run("spacing = 10\nshrink = 20\nimport = 100\n[Constraints]\nType, Range, Value, Min, Max\npad, D4:F6, , 101, 102\n[Existing]\n" + flat, Us);
        var ew2 = r2.Tables.Single(t => t.Title == "Earthwork").Rows;
        double cut2 = Convert.ToDouble(ew2[0][1], CultureInfo.InvariantCulture), fill2 = Convert.ToDouble(ew2[1][1], CultureInfo.InvariantCulture);
        Assert.InRange(fill2 - cut2 * 0.8, 95, 105);
    }

    [Fact]
    public void Solver_ProjectsOntoFeasibleSet()
    {
        // Two points, z0 − z1 ≤ 1, existing 0 and −3: the least-squares answer moves each 1 ft toward the other.
        var res = GradingSolver.Solve([0, -3], [1, 1], [new GradeConstraint("g", [1, 0], [-1, 1], 1, false, 1e6, "")]);
        Assert.True(res.Converged);
        Assert.Equal(-1, res.Z[0], 6); Assert.Equal(-2, res.Z[1], 6);
    }

    [Theory]
    [InlineData("x = 1\n", "E-GS-001")]
    [InlineData("[Constraints]\nType, Range\nfix, A1:Z9\n[Existing]\n1, 2\n3, 4\n", "E-GS-007")]
    [InlineData("[Constraints]\nType, Range\nbulge, A1\n[Existing]\n1, 2\n3, 4\n", "E-GS-008")]
    [InlineData("[Constraints]\nType, Range, Max\nslope, A1:B2\n[Existing]\n1, 2\n3, 4\n", "E-GS-010")]
    [InlineData("[Existing]\n1, 2\n3\n", "E-GS-004")]
    public void BadInput(string input, string code) => Assert.Contains(Tool.Run(input, Us).Messages, m => m.Code == code);

    [Fact]
    public void Ranges()
    {
        Assert.Equal([0, 1, 4, 5], GradingSolverTool.Range("A1:B2", 3, 4, out _));
        Assert.Equal([11, 0], GradingSolverTool.Range("D3; A1", 3, 4, out _));
        GradingSolverTool.Range("E1", 3, 4, out var err);
        Assert.NotNull(err);
        Assert.Equal("grading-solver", PremiumFeatures.ForTool("grading-solver")?.Id);
    }
}
