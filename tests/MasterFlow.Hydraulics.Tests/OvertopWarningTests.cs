using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Sample 6 (creek with overbanks): the Horton-Einstein result put the water at
/// elevation 108.018 above both 108.0 end points and the caution was easy to miss. Irregular sections now
/// warn with the overtop amount and the extended walls, and warn when water leaves labelled banks.
/// </summary>
public class OvertopWarningTests
{
    private static IrregularSection Creek(CompositeRoughnessMethod m) => new(
    [
        new(0, 108, 0.06, "Left overbank"), new(40, 105, 0.06), new(50, 105, 0.035, "Left bank"), new(55, 100, 0.035),
        new(65, 100, 0.035), new(70, 105, 0.06, "Right bank"), new(80, 105, 0.06), new(120, 108, null, "Right overbank"),
    ], m);

    private static UniformFlowResult Solve(IrregularSection s) => UniformFlowCalculator.Solve(new UniformFlowInput
    { Section = s, SolveFor = SolveFor.NormalDepth, ManningN = 0.06, Slope = 0.001, Discharge = 500, Depth = 6 });

    [Fact]
    public void HortonEinstein_Sample6_IsReportedAsOvertopped_WithExtendedWalls()
    {
        var r = Solve(Creek(CompositeRoughnessMethod.EqualVelocity));
        Assert.True(r.Succeeded);
        Assert.Equal(108.018, r.Irregular!.WaterSurfaceElevation, 3);
        var w = Assert.Single(r.Messages, m => m.Code == MessageCodes.WallExtended);
        Assert.Equal(MessageSeverity.Warning, w.Severity);
        Assert.StartsWith("Overtopped", w.Text);
        Assert.Contains("0.018 ft above the left end (elevation 108.000)", w.Text);
        Assert.Contains("0.018 ft above the right end (elevation 108.000)", w.Text);
        Assert.Contains("extended the left and right end walls", w.Text);
    }

    [Fact]
    public void HecRas_Sample6_IsInsideTheSection_ButOutOfBank()
    {
        var r = Solve(Creek(CompositeRoughnessMethod.Conveyance));
        Assert.Equal(6.893, r.Normal.Depth, 3);
        Assert.DoesNotContain(r.Messages, m => m.Code == MessageCodes.WallExtended);
        var w = Assert.Single(r.Messages, m => m.Code == MessageCodes.OutOfBank);
        Assert.Contains("\"Left bank\"", w.Text);
        Assert.Contains("\"Right bank\"", w.Text);
        Assert.Contains("by 1.893 ft", w.Text);
        Assert.DoesNotContain("overbank\"", w.Text); // end points labelled "overbank" are not banks
    }

    [Fact]
    public void InBank_HasNoOvertopOrBankWarning()
    {
        var r = UniformFlowCalculator.Solve(new UniformFlowInput
        { Section = Creek(CompositeRoughnessMethod.Conveyance), SolveFor = SolveFor.NormalDepth, ManningN = 0.06, Slope = 0.001, Discharge = 50, Depth = 2 });
        Assert.True(r.Irregular!.WaterSurfaceElevation < 105);
        Assert.DoesNotContain(r.Messages, m => m.Code is MessageCodes.WallExtended or MessageCodes.OutOfBank);
    }

    [Theory]
    [InlineData("Left bank", true)]
    [InlineData("top of bank", true)]
    [InlineData("RB", true)]
    [InlineData("TOB", true)]
    [InlineData("Left overbank", false)]
    [InlineData("Thalweg", false)]
    [InlineData(null, false)]
    public void BankLabels(string? description, bool isBank) => Assert.Equal(isBank, UniformFlowCalculator.IsBankLabel(description));
}
