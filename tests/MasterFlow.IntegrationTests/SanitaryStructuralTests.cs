using MasterFlow.Application.Tools;
using MasterFlow.Domain;

namespace MasterFlow.IntegrationTests;

/// <summary>Sanitary sewer structural check at the designed depths: PVC deflection by hand, the SDR 26 step-up, RCP equal to the pipe structural tool.</summary>
public class SanitaryStructuralTests
{
    private static readonly SanitaryStructuralTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static IReadOnlyList<object?> Row(ToolResult r, string table, string pipe) => r.Tables.First(t => t.Title.StartsWith(table)).Rows.First(x => (string?)x[0] == pipe);

    /// <summary>One sewer A→B with the given cover over the outside top of pipe at both ends.</summary>
    private static string OnePipe(double dIn, double odIn, double cover, string head = "") =>
        head + $"""

        h2s = no
        [Manholes]
        Name	Rim
        A	{100 + (dIn + (odIn - dIn) / 2) / 12 + cover:F6}
        B	{98.8 + (dIn + (odIn - dIn) / 2) / 12 + cover:F6}
        [Loads]
        Manhole	Land use	Population
        A	Residential	300
        [Pipes]
        Name	From	To	Length (ft)	Diameter (in)	Up invert	Down invert
        P1	A	B	304	{dIn}	100.00	98.80
        """;

    [Fact]
    public void Sample_RunsClean_AndListsDeepSewers()
    {
        var r = Tool.Run(Tool.SampleInput(Us), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var t = r.Tables.First(x => x.Title == "Sewers at their designed depths");
        Assert.Contains(t.Rows, x => (string?)x[5] == "yes");
        Assert.Single(r.Files);
        // The written input runs in the pipe structural tool.
        var ps = new PipeStructuralTool().Run(System.Text.Encoding.UTF8.GetString(r.Files[0].Content), Us);
        Assert.True(ps.Succeeded, string.Join("\n", ps.Messages.Select(m => m.Text)));
    }

    [Fact]
    public void Pvc8In_20ftCover_ModifiedIowaByHand()
    {
        // Pe = 120 × 20 / 144 = 16.667 psi; E′ = 2000 (gravel, moderate); Δ = 100·0.1·16.667/(0.149·46 + 0.061·2000) = 1.293 %.
        var r = Tool.Run(OnePipe(8, 8.4, 20), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var f = Row(r, "Flexible pipe", "P1");
        Assert.Equal("PVC SDR 35 (D3034)", f[1]);
        Assert.Equal(20, (double)f[4]!, 2);
        Assert.Equal(100 * 0.1 * (120 * 20 / 144.0) / (0.149 * 46 + 0.061 * 2000), (double)f[9]!, 2);
        Assert.Equal("yes", Row(r, "Sewers at their designed depths", "P1")[5]);
    }

    [Fact]
    public void Pvc_StepsUpToSdr26_WhenSdr35DeflectsTooMuch()
    {
        // Silt, moderate: E′ = 400. SDR 35: 100·0.1·16.667/(6.854 + 24.4) = 5.33 % > 5; SDR 26: /(17.135 + 24.4) = 4.01 %.
        var r = Tool.Run(OnePipe(8, 8.4, 20, "embedment = silt\ncompaction = moderate"), Us);
        var f = Row(r, "Flexible pipe", "P1");
        Assert.Equal("PVC SDR 26 (D3034)", f[1]);
        Assert.Equal(100 * 0.1 * (120 * 20 / 144.0) / (0.149 * 115 + 0.061 * 400), (double)f[9]!, 2);
        Assert.Contains(r.Messages, m => m.Code == "I-SD-001");
        // At 25 ft even SDR 26 exceeds 5 %: 20.83/(41.54) = 5.01 %.
        var r2 = Tool.Run(OnePipe(8, 8.4, 25, "embedment = silt\ncompaction = moderate"), Us);
        Assert.Contains(r2.Messages, m => m.Code == "W-SD-001");
        Assert.Equal("over 5 %", Row(r2, "Sewers at their designed depths", "P1")[8]);
    }

    [Fact]
    public void Rcp_MatchesPipeStructuralTool()
    {
        // 48 in. RCP wall B (5 in.), 22 ft cover, Type 2: the same D-load as the pipe structural tool.
        var r = Tool.Run(OnePipe(48, 58, 22, "material = RCP"), Us);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        var row = Row(r, "Reinforced concrete pipe", "P1");
        var ps = new PipeStructuralTool().Run("[Rigid]\nName\tID\tCover\tInstallation\nX\t48\t22\t2", Us);
        var pr = ps.Tables[0].Rows[0];
        Assert.Equal((double)pr[11]!, (double)row[10]!, 0);
        Assert.Equal($"Class {pr[12]!.ToString()!.Replace("Class ", "")}", row[11]);
    }

    [Fact]
    public void Helpers_MatchPipeStructuralHandCheck()
    {
        // docs/tools/pipe-structural.md: 24 in. RCP, 3 ft, Type 2 → 993 lb/ft/ft, Class II.
        var d = PipeStructuralTool.RigidDLoad(24, 3, 3, 2, null, 120, true, 1.15);
        Assert.Equal(993, d.DLoad, 0);
        Assert.Equal("II", PipeStructuralTool.C76Class(d.DLoad));
    }

    [Fact]
    public void BadInput_IsReported()
    {
        Assert.Contains(Tool.Run("material = clay\n" + OnePipe(8, 8.4, 10), Us).Messages, m => m.Code == "E-SD-002");
        Assert.Contains(Tool.Run("embedment = peat\n" + OnePipe(8, 8.4, 10), Us).Messages, m => m.Code == "E-SD-005");
        Assert.Contains(Tool.Run(OnePipe(8, 8.4, 10) + "\n[Materials]\nPipe\tMaterial\nZZ\tPVC", Us).Messages, m => m.Code == "E-SD-006");
        Assert.False(Tool.Run(OnePipe(8, 8.4, 10), new DesignToolContext(UnitSystem.SI)).Succeeded);
    }
}
