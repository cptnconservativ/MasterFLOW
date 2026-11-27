using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Gravity vs lift station life-cycle cost: NIST Handbook 135 factors, replacements, salvage, energy, break-even.</summary>
public class SewerLifeCycleCostTests
{
    private static readonly SewerLifeCycleCostTool Tool = new();
    private static readonly DesignToolContext Us = new(UnitSystem.USCustomary);
    private static string Sample => Tool.SampleInput(Us);
    private static ToolResult Run(string input) => Tool.Run(input, Us);
    private static double Pw(ToolResult r, string component, int col) => Convert.ToDouble(r.Tables[0].Rows.First(x => (string)x[0]! == component)[col]);

    [Fact]
    public void Sample_RunsClean_AndSiIsRefused()
    {
        var r = Run(Sample);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        Assert.DoesNotContain(r.Messages, m => m.Severity == MessageSeverity.Warning);
        Assert.NotEmpty(r.Charts);
        Assert.False(Tool.Run(Sample, new DesignToolContext(UnitSystem.SI)).Succeeded);
    }

    [Fact]
    public void Factors_ByHand()
    {
        // UPV(3 %, 75) = (1.03^75 − 1)/(0.03·1.03^75)
        double f = Math.Pow(1.03, 75);
        Assert.Equal((f - 1) / (0.03 * f), SewerLifeCycleCostTool.Upv(0.03, 75), 10);
        Assert.Equal(75, SewerLifeCycleCostTool.Upv(0, 75), 10);
        Assert.Equal(1 / f, SewerLifeCycleCostTool.Spv(0.03, 75), 12);
        Assert.Equal(1, SewerLifeCycleCostTool.Crf(0.03, 75) * SewerLifeCycleCostTool.Upv(0.03, 75), 12);
        Assert.Equal([25, 50], SewerLifeCycleCostTool.ReplacementYears(25, 75));
        Assert.Equal([15, 30, 45, 60], SewerLifeCycleCostTool.ReplacementYears(15, 75));
        Assert.Empty(SewerLifeCycleCostTool.ReplacementYears(100, 75));
        // 25-yr item: last installed year 50, fully used at 75 → no salvage; 15-yr: installed 60, used 15 → 0; 20-yr: installed 60, 5 of 20 left.
        Assert.Equal(0, SewerLifeCycleCostTool.SalvageFraction(25, 75), 12);
        Assert.Equal(0.25, SewerLifeCycleCostTool.SalvageFraction(20, 75), 12);
        Assert.Equal(0.25, SewerLifeCycleCostTool.SalvageFraction(100, 75), 12);
        // Energy: 100 gpm × 50 ft / (3960 × 0.5) hp × 0.7457 kW/hp × 8760 h
        Assert.Equal(100 * 50 / 1980.0 * 0.7457 * 8760, SewerLifeCycleCostTool.AnnualKwh(100, 50, 0.5), 6);
        Assert.Equal(60, SewerLifeCycleCostTool.FlowInYear(1, 60, 120, 21), 9);
        Assert.Equal(90, SewerLifeCycleCostTool.FlowInYear(11, 60, 120, 21), 9);
        Assert.Equal(120, SewerLifeCycleCostTool.FlowInYear(40, 60, 120, 21), 9);
    }

    [Fact]
    public void SmallCase_ByHand()
    {
        // Gravity $1,000,000 pipe (100-yr life) + $2/yr·LF O&M on 1,000 LF; lift station $400,000 structure (75 yr) + $100,000 pumps (25 yr)
        // + $5,000/yr O&M; constant 100 gpm at TDH 50 ft, η 0.5, $0.10/kWh; 75 yr at 3 %.
        const string input = """
            analysis period = 75
            discount rate = 3
            power price = 0.10
            initial average flow = 100
            design average flow = 100
            TDH = 50
            efficiency = 0.5
            [Gravity items]
            Item	Unit	Quantity	Unit cost	Life	O&M
            Pipe	LF	1000	1000	100	2
            [Lift station items]
            Item	Unit	Quantity	Unit cost	Life	O&M
            Structure	LS	1	400000	75
            Pumps	LS	1	100000	25
            Operation	LS	1	0		5000
            """;
        var r = Run(input);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        double upv = SewerLifeCycleCostTool.Upv(0.03, 75), spv75 = Math.Pow(1.03, -75);
        double gravity = 1_000_000 + 2000 * upv - 0.25 * 1_000_000 * spv75;
        double kwh = 100 * 50 / 1980.0 * 0.7457 * 8760;
        double lift = 500_000 + 100_000 * (Math.Pow(1.03, -25) + Math.Pow(1.03, -50)) + 5_000 * upv + kwh * 0.10 * upv;
        Assert.Equal(gravity, Pw(r, "Total present worth", 1), 0);
        Assert.Equal(lift, Pw(r, "Total present worth", 2), 0);
        Assert.Equal(kwh * 0.10 * upv, Pw(r, "Pumping energy", 2), 0);
        // Break-even power price: lift(non-energy) + kWh·p·UPV = gravity.
        double be = (gravity - (lift - kwh * 0.10 * upv)) / (kwh * upv);
        Assert.Contains(r.Summary, s => s.Contains($"${be:F3}/kWh"));
        // With $50,000/yr station O&M the lift station loses even at free power.
        Assert.Contains(Run(input.Replace("0		5000", "0		50000")).Summary, s => s.StartsWith("Gravity is lower even with free power"));
    }

    [Fact]
    public void TdhFromForceMain_MatchesLiftStationTool()
    {
        var r = Run(Sample);
        double tdh = 42 + SanitaryLiftStationTool.SystemHead(330, 0, 2600, 6, 120, 6.5);
        Assert.Contains(r.Summary, s => s.Contains($"TDH {tdh:F1} ft at 330 gpm"));
    }

    [Fact]
    public void Escalation_AndSensitivity()
    {
        var a = Run(Sample);
        var b = Run(Sample.Replace("price index from =          # optional: escalate all unit costs by a cost index (e.g. ENR CCI then and now)", "price index from = 100").Replace("price index to =\n", "price index to = 110\n"));
        Assert.True(b.Succeeded, string.Join("\n", b.Messages.Select(m => m.Text)));
        Assert.Equal(1.1 * Pw(a, "Capital", 1), Pw(b, "Capital", 1), 0);
        Assert.Contains(b.Messages, m => m.Code == "I-LC-003");
        // Higher power price favours gravity: lift minus gravity grows across the sensitivity row.
        foreach (var row in a.Tables[2].Rows) Assert.True(Convert.ToDouble(row[3]) > Convert.ToDouble(row[1]));
    }

    [Fact]
    public void Warnings_AndBadInput()
    {
        Assert.Contains(Run(Sample.Replace("analysis period = 75", "analysis period = 50")).Messages, m => m.Code == "W-LC-001");
        Assert.Contains(Run(Sample.Replace("Pumps, motors and rails	each	2	32000	25", "Pumps, motors and rails	each	2	32000	40")).Messages, m => m.Code == "W-LC-002");
        Assert.Contains(Run(Sample.Replace("discount rate = 3.0", "discount rate = 80")).Messages, m => m.Code == "E-LC-002");
        Assert.Contains(Run(Sample.Replace("efficiency = 0.55", "efficiency = 1.4")).Messages, m => m.Code == "E-LC-005");
        Assert.Contains(Run(Sample.Replace("Manholes 48 in	each	4	7500", "Manholes 48 in	each	-4	7500")).Messages, m => m.Code == "E-LC-006");
        Assert.Contains(Run(Sample.Replace("price index to =\n", "price index to = 110\n")).Messages, m => m.Code == "E-LC-003");
        Assert.Contains(Run("analysis period = 75\ninitial average flow = 50\nTDH = 30").Messages, m => m.Code == "E-LC-007");
        // Lump sums from the cost estimate tool stand in for the tables.
        var lump = Run("initial average flow = 50\nTDH = 30\ngravity capital = 2000000\nlift station capital = 900000");
        Assert.True(lump.Succeeded);
        Assert.Equal(2_000_000, Pw(lump, "Capital", 1), 0);
    }
}
