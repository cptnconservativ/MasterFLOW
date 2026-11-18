using MasterFlow.Application;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using Xunit;

namespace MasterFlow.Application.Tests;

public class TemplateTests
{
    public static TheoryData<string> Names() { var d = new TheoryData<string>(); foreach (var t in BuiltInTemplates.All) d.Add(t.Name); return d; }

    [Theory]
    [MemberData(nameof(Names))]
    public void EveryBuiltInTemplate_SolvesWithoutErrors(string name)
    {
        var t = BuiltInTemplates.All.Single(x => x.Name == name);
        var r = UniformFlowCalculator.Solve(t.Calculation.Input);
        Assert.True(r.Succeeded, string.Join("; ", r.Messages.Select(m => m.Code + " " + m.Text)));
        Assert.True(double.IsFinite(r.Normal.Depth) && r.Normal.Depth > 0);
        Assert.True(t.BuiltIn);
    }

    [Fact]
    public void TemplateNames_AreUnique()
    {
        Assert.Equal(BuiltInTemplates.All.Count, BuiltInTemplates.All.Select(t => t.Name).Distinct().Count());
    }

    [Fact]
    public void Instantiate_GivesFreshIdAndSameInput()
    {
        var t = BuiltInTemplates.All[0];
        var a = BuiltInTemplates.Instantiate(t);
        var b = BuiltInTemplates.Instantiate(t);
        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(t.Calculation.Id, a.Id);
        Assert.Equal(t.Calculation.Input, a.Input);
    }
}
