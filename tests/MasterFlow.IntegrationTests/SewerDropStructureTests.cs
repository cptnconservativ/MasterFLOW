using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Sewer drop structures: IDCM 3.12.6 velocity limit, Ten States anchors and drops, vortex and baffle drop shafts.</summary>
public class SewerDropStructureTests
{
    private static readonly SewerDropStructureTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static string Sample => Tool.SampleInput(Us);
    private static ToolResult Run(string input) => Tool.Run(input, Us);
    private const double M = 0.3048, Cfs = 35.3147;

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Run(Sample);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.Equal(["W-DS-003", "W-DS-006"], r.Messages.Where(m => m.Severity == MessageSeverity.Warning).Select(m => m.Code));
        var types = r.Tables.First(t => t.Title == "Drops").Rows.ToDictionary(x => (string)x[0]!, x => (string)x[9]!);
        Assert.StartsWith("Filleted", types["MH-12"]);       // 1.40 ft < 2 ft
        Assert.StartsWith("Vortex", types["MH-14"]);         // 24 ft ≥ 15 ft
        Assert.StartsWith("Outside", types["MH-20"]);        // 4.48 ft
        Assert.StartsWith("Vortex", types["T-2 drop 1/1"]);  // regrade of the 8 % reach
        Assert.False(Tool.Run(Sample, new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void VortexShaft_MatchesJainRelation_PublishedModel()
    {
        // Sci. Rep. 13 (2023) model: Qd = 19.4 L/s, k = 1.22 gives D = 0.16 m (Jain's D = k(Q²/g)^0.2).
        double d = SewerDropStructureTool.VortexDiameter(0.0194 * Cfs, 1.22) * M;
        Assert.Equal(0.16, d, 0.005);
        double q = 3.0;
        Assert.Equal(q, SewerDropStructureTool.VortexCapacity(SewerDropStructureTool.VortexDiameter(q, 1.2), 1.2), 9);
        Assert.Equal(1.2 * Math.Pow(9 / 32.174, 0.2), SewerDropStructureTool.VortexDiameter(3, 1.2), 12);
    }

    [Fact]
    public void BaffleDrop_MatchesOdgaardYorkPrototype()
    {
        // Odgaard et al. (2013): York, Q = 16.2 m³/s, D = 12 m, B = 6 m, F = 0.15, (h − t)/B = 0.39.
        double q = 16.2 * Cfs;
        Assert.Equal(12 / M, SewerDropStructureTool.BaffleDiameter(q), 12 / M * 0.05);
        Assert.Equal(0.15, SewerDropStructureTool.BaffleF(q, 6 / M), 0.002);
        // With B = D/2 from Eq. 11, F = (1/243)^(1/3) = 0.160 whatever the flow; (h − t)/B = F/0.55 + 0.1 = 0.391.
        double dB = SewerDropStructureTool.BaffleDiameter(50);
        Assert.Equal(Math.Cbrt(1 / 243.0), SewerDropStructureTool.BaffleF(50, dB / 2), 9);
        Assert.Equal(0.391, (SewerDropStructureTool.BaffleSpacing(Math.Cbrt(1 / 243.0), 1, 0)), 0.001);
        // Eq. 7 with B = D/2: 2(h/D)(0.5)F + (0.25/0.55)F².
        Assert.Equal(2 * 0.4 * 0.5 * 0.16 + 0.25 / 0.55 * 0.0256, SewerDropStructureTool.PlungeCriterion(4, 10, 5, 0.16), 12);
    }

    [Fact]
    public void CriticalDepth_HalfFull_AndAnchors()
    {
        double d = 2, a = Math.PI * d * d / 8;
        double q = Math.Sqrt(32.174 * a * a * a / d);
        Assert.Equal(1.0, SewerDropStructureTool.CriticalDepth(q, d), 6);
        Assert.True(double.IsNaN(SewerDropStructureTool.AnchorSpacing(0.19)));
        Assert.Equal(36, SewerDropStructureTool.AnchorSpacing(0.20));
        Assert.Equal(24, SewerDropStructureTool.AnchorSpacing(0.35));
        Assert.Equal(16, SewerDropStructureTool.AnchorSpacing(0.55));
    }

    [Fact]
    public void SteepReach_RegradeByHand()
    {
        var r = Run(Sample);
        var row = r.Tables.First(t => t.Title == "Regrading steep reaches").Rows.Single();
        double sAllow = SanitarySewerTool.SlopeForFullVelocity(15 / 12.0, 0.013, 10);
        Assert.Equal(350 * (0.08 - sAllow), Convert.ToDouble(row[3]), 2);
        // Velocity at half depth = full: 1.486/n·(D/4)^(2/3)·√S at 8 % in 15 in.
        double v = 1.486 / 0.013 * Math.Pow(15 / 48.0, 2.0 / 3) * Math.Sqrt(0.08);
        Assert.Equal(v, Convert.ToDouble(r.Tables[0].Rows[1][4]), 2);
        // A tighter limit per structure splits the drop: 15.37 ft at 6 ft each → 3 structures.
        var r2 = Run(Sample.Replace("max drop per structure = 30", "max drop per structure = 6"));
        Assert.Equal(3, Convert.ToInt32(r2.Tables.First(t => t.Title == "Regrading steep reaches").Rows.Single()[4]));
    }

    [Fact]
    public void Warnings_AndBadInput()
    {
        // 30 % reach: anchors at 36 ft.
        var steep = Run("[Reaches]\nPipe\tLength\tD\tUp\tDn\tQ\nS\t100\t8\t130\t100\t200");
        Assert.Contains(steep.Messages, m => m.Code == "W-DS-004" && m.Text.Contains("36 ft"));
        // Overload: k = 1.0 with the hydraulic size governing (no minimum) leaves too little margin.
        var big = Run("vortex k = 1.0\nmin shaft diameter = 0\n[Drops]\nMH\tIn\tOut\tQ\tDin\tSin\tDout\tSout\nX\t140\t100\t89766\t36\t5\t60\t0.5");
        Assert.Contains(big.Messages, m => m.Code == "W-DS-007");
        // 200 cfs: the Eq. 11 baffle shaft (25 ft) is beyond the size list and rounds to the next foot.
        Assert.Equal(300.0, Convert.ToDouble(big.Tables.First(t => t.Title.StartsWith("Baffle")).Rows.Single()[2]));
        Assert.DoesNotContain(big.Messages, m => m.Code == "W-DS-008");
        Assert.Contains(Run(Sample.Replace("n = 0.013", "n = 0.011")).Messages, m => m.Code == "W-DS-001");
        Assert.Contains(Run(Sample.Replace("vortex k = 1.2", "vortex k = 1.6")).Messages, m => m.Code == "W-DS-002");
        Assert.Contains(Run(Sample.Replace("MH-20	3459.48	3455.00", "MH-20	3455.00	3459.48")).Messages, m => m.Code == "E-DS-004");
        Assert.Contains(Run(Sample.Replace("T-1	400	15", "T-1	-400	15")).Messages, m => m.Code == "E-DS-003");
        Assert.Contains(Run("n = 0.013").Messages, m => m.Code == "E-DS-005");
        Assert.Contains(Run(Sample.Replace("overload = 1.5", "overload = 0.5")).Messages, m => m.Code == "E-DS-002");
    }
}
