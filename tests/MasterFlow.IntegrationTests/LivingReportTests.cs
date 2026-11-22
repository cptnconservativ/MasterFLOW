using MasterFlow.Application.Reports;
using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.IntegrationTests;

/// <summary>Living drainage report (premium, D-230): live values in the narrative, the value register, issued-value change tracking and typed-number hints.</summary>
public class LivingReportTests
{
    private static readonly DrainageReportTool Tool = new();

    private static DesignToolContext Project(string detention)
    {
        var us = new DesignToolContext(UnitSystem.USCustomary);
        var net = NetworkOutputTests.NetA();
        var inputs = new Dictionary<string, string>
        {
            ["detention-routing"] = detention,
            ["hds5-culvert"] = DesignTools.Find("hds5-culvert")!.SampleInput(us),
        };
        return new DesignToolContext(UnitSystem.USCustomary, net, StormNetworkSolver.Solve(net))
        {
            Project = new ProjectInfo { Name = "Founders Park" }, ToolInputs = inputs, Networks = [(net, StormNetworkSolver.Solve(net))],
        };
    }

    private static string Sample => DesignTools.Find("detention-routing")!.SampleInput(new DesignToolContext(UnitSystem.USCustomary));

    private const string Body = """
        [Report]
        # Detention
        The pond releases {value: detention-routing | Routing results | 100-yr | Peak outflow #F1} cfs in the 100-year storm, stores {value:detention-routing|Routing|100-yr|Storage used #F2} ac-ft and drains in {value: detention-routing | Routing results | max | Drain time #F0} hours.
        - Culvert headwater: {value: hds5-culvert | Results | Design | HW/D #F2}
        - Least HGL clearance: {network: Main St | min | clearance #F2} ft; pipe P1 carries {network: Main St | P1 | q} cfs.
        @values
        """;

    [Fact]
    public void LiveValues_ResolveFromCurrentCalculations()
    {
        var ctx = Project(Sample);
        var r = Tool.Run("title = Test\n" + Body, ctx);
        Assert.True(r.Succeeded, string.Join("\n", r.Messages));
        var text = string.Join("\n", r.Report!.Body.OfType<ReportParagraph>().Select(p => p.Text));
        Assert.Contains("releases 13.7 cfs", text);
        Assert.Contains("stores 1.36 ac-ft", text);
        Assert.Contains("drains in 36 hours", text);       // max of 36.3, 25.4, 20.2
        Assert.Contains("headwater: 1.57", text);
        Assert.Contains("P1 carries 20 cfs", text);
        Assert.DoesNotContain("{", text);
        Assert.DoesNotContain("missing", text);
        // @values puts the register in the report; the result has the register and the lines to paste on issue.
        Assert.Contains(r.Report.Body.OfType<ReportTable>(), t => t.Table.Title == "Live values");
        var reg = r.Tables.Single(t => t.Title == "Live values");
        Assert.Equal(6, reg.Rows.Count);
        Assert.All(reg.Rows, row => Assert.Equal("LIVE", row[4]));
        Assert.Contains(reg.Rows, row => row[5] is string s && s.Contains("min clearance (", StringComparison.Ordinal));
        var paste = r.Tables.Single(t => t.Title == "Issued values");
        Assert.Contains(paste.Rows, row => (string)row[0]! == "{value: detention-routing | Routing results | 100-yr | Peak outflow #F1} = 13.7");
    }

    [Fact]
    public void IssuedValues_ListWhatChangedAfterRedesign()
    {
        // Issue the report, then shrink the riser: the 100-yr release changes and the register says so.
        var first = Tool.Run("title = Test\n" + Body, Project(Sample));
        var issued = "[Issued values]\n" + string.Join("\n", first.Tables.Single(t => t.Title == "Issued values").Rows.Select(r => (string)r[0]!)) + "\n";
        Assert.Contains("Riser", Sample);
        var revised = Sample.Replace("Riser\triser\t104.0\t3.0", "Riser\triser\t104.0\t2.0");
        Assert.NotEqual(Sample, revised);
        var r = Tool.Run("title = Test\nmark changed values = yes\n" + issued + Body, Project(revised));
        var reg = r.Tables.Single(t => t.Title == "Live values");
        var q = reg.Rows.Single(x => ((string)x[0]!).Contains("Peak outflow"));
        Assert.Equal("CHANGED", q[4]);
        Assert.Equal("13.7", q[2]);
        Assert.NotNull(q[3]);
        Assert.Equal("SAME", reg.Rows.Single(x => ((string)x[0]!).Contains("hds5")).ElementAt(4));
        Assert.Contains(r.Messages, m => m.Code == "W-RB-011" && m.Text.Contains("Peak outflow"));
        Assert.Contains(r.Report!.Body.OfType<ReportParagraph>(), p => p.Text.Contains("cfs in the 100-year storm") && p.Text.Contains("(was 13.7)"));
        Assert.Contains(r.Summary, s => s.Contains("changed since issue"));
    }

    [Fact]
    public void IssuedValueDroppedFromText_IsListed()
    {
        var r = Tool.Run("[Issued values]\n{network: Main St | P2 | q} = 12\n" + Body, Project(Sample));
        Assert.Contains(r.Tables.Single(t => t.Title == "Live values").Rows, x => x[4] is "NOT IN TEXT" && (string)x[2]! == "12");
        Assert.Contains(r.Messages, m => m.Code == "I-RB-011");
    }

    [Theory]
    [InlineData("{value: detention-routing | Nope | 100-yr | Peak outflow}", "no table")]
    [InlineData("{value: detention-routing | Routing results | 500-yr | Peak outflow}", "no row")]
    [InlineData("{value: detention-routing | Routing results | 100-yr | Velocity}", "no column")]
    [InlineData("{value: rational-rapid-city | Results | A1 | Q100}", "no saved input")]
    [InlineData("{network: Main St | P9 | q}", "no pipe")]
    [InlineData("{network: Main St | P1 | flux}", "unknown field")]
    [InlineData("{value: detention-routing | Routing results | 100-yr}", "give tool id")]
    public void BadReference_WarnsAndMarksTheText(string reference, string why)
    {
        var r = Tool.Run("[Report]\nValue " + reference + " here.", Project(Sample));
        Assert.Contains(r.Messages, m => m.Code == "W-RB-010" && m.Text.Contains(why));
        Assert.Contains(r.Report!.Body.OfType<ReportParagraph>(), p => p.Text.Contains("[missing value:"));
        Assert.Equal("MISSING", r.Tables.Single(t => t.Title == "Live values").Rows[0][4]);
    }

    [Fact]
    public void TypedNumbers_GetALiveReferenceSuggestion()
    {
        var r = Tool.Run("[Report]\n# Detention\nThe pond releases 13.7 cfs in the 100-year event (IDCM §4.10.3), with 2,450 cf of extra volume.\n@tool detention-routing summary", Project(Sample));
        var t = r.Tables.Single(x => x.Title == "Typed numbers");
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("13.7 cfs", t.Rows[0][1]);
        Assert.Equal("Detention", t.Rows[0][0]);
        Assert.StartsWith("{value: detention-routing |", (string)t.Rows[0][3]!);
        Assert.Contains("#F1}", (string)t.Rows[0][3]!);
        Assert.Equal("2450 cf", t.Rows[1][1]);
        Assert.Contains(r.Messages, m => m.Code == "I-RB-012");
    }

    [Fact]
    public void Normalize_IgnoresSpacingBracesAndKindCase()
    {
        Assert.Equal(LiveValues.Normalize("{value: a | b | c | d}"), LiveValues.Normalize("VALUE:a|b|c|d"));
        Assert.True(LiveValues.SameValue("12.40", "12.4"));
        Assert.False(LiveValues.SameValue("12.4", "12.5"));
    }

    [Fact]
    public void Aggregates_CountSumNth()
    {
        var ctx = Project(Sample);
        (IDesignTool, ToolResult)? Find(string id) { var t = DesignTools.Find(id)!; return (t, t.Run(ctx.ToolInputs[id], ctx)); }
        Assert.Equal(3, LiveValues.Resolve("value: detention-routing | Routing results | count | Storm", Find, ctx.Networks).Number);
        Assert.Equal("10-yr", LiveValues.Resolve("value: detention-routing | Routing results | #2 | Storm", Find, ctx.Networks).Text);
        Assert.Equal(40, LiveValues.Resolve("network: | sum | q", Find, ctx.Networks).Number);
        Assert.Equal(3, LiveValues.Resolve("network: Main St | count | q", Find, ctx.Networks).Number);
    }
}
