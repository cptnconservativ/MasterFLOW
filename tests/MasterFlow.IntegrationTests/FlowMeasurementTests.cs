using MasterFlow.Application.Tools;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;
using D = MasterFlow.Application.Tools.FlowMeasurementTool.Device;

namespace MasterFlow.IntegrationTests;

/// <summary>Flow measurement: USBR WMM weir and Parshall equations by hand, submergence, SI.</summary>
public class FlowMeasurementTests
{
    private static readonly FlowMeasurementTool Tool = new();

    private static ToolResult Ok(string input, UnitSystem u = UnitSystem.USCustomary)
    {
        var r = Tool.Run(input, new DesignToolContext(u));
        Assert.True(r.Succeeded, string.Join("\n", r.Messages.Select(m => m.Text)));
        return r;
    }

    [Fact]
    public void Samples_RunInBothUnits()
    {
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.USCustomary)));
        Ok(Tool.SampleInput(new DesignToolContext(UnitSystem.SI)), UnitSystem.SI);
    }

    [Fact]
    public void Weirs_ByHand()
    {
        Assert.Equal(4.28 * 0.578 * Math.Pow(0.483, 2.5), FlowMeasurementTool.Discharge(D.VNotch, 90, 0.48), 9);
        Assert.Equal((3.22 + 0.40 * 0.65 / 2) * 3.997 * Math.Pow(0.653, 1.5), FlowMeasurementTool.Discharge(D.Suppressed, 4, 0.65, 2), 9);
        Assert.Equal(3.33 * (3 - 0.1) * Math.Pow(0.5, 1.5), FlowMeasurementTool.Discharge(D.Contracted, 3, 0.5), 9);
        Assert.Equal(3.367 * 3 * Math.Pow(0.5, 1.5), FlowMeasurementTool.Discharge(D.Cipolletti, 3, 0.5), 9);
    }

    [Fact]
    public void Parshall_Coefficients()
    {
        Assert.Equal(3.07 * Math.Pow(0.8, 1.53), FlowMeasurementTool.Discharge(D.Parshall, 0.75, 0.8), 9);
        Assert.Equal(4 * 2 * Math.Pow(1.2, 1.522 * Math.Pow(2, 0.026)), FlowMeasurementTool.Discharge(D.Parshall, 2, 1.2), 9);
        Assert.Equal((3.6875 * 12 + 2.5) * Math.Pow(2, 1.6), FlowMeasurementTool.Discharge(D.Parshall, 12, 2), 9);
        Assert.Null(FlowMeasurementTool.Parshall(0.4));
        // The 1-ft equation at 1 ft of head gives C = 4·W = 4.00 cfs.
        Assert.Equal(4.0, FlowMeasurementTool.Discharge(D.Parshall, 1, 1), 6);
    }

    [Fact]
    public void Parshall_Submerged_GivesNoDischarge()
    {
        var r = Ok("[Devices]\nName,Type,Size,P,Ha,Hb\nF,parshall,9 in,,0.8,0.6\n");
        Assert.True(double.IsNaN((double)r.Tables[0].Rows[0][4]!));
        Assert.Contains(r.Messages, m => m.Code == "W-WM-002");
        var free = Ok("[Devices]\nName,Type,Size,P,Ha,Hb\nF,parshall,9 in,,0.8,0.4\n");
        Assert.Equal(Math.Round(3.07 * Math.Pow(0.8, 1.53), 4), (double)free.Tables[0].Rows[0][4]!, 4);
    }

    [Fact]
    public void SiMatchesUs()
    {
        var us = Ok("[Devices]\nName,Type,Size,P,Ha\nW,suppressed,4,2,0.65\n");
        var si = Ok($"[Devices]\nName,Type,Size,P,Ha\nW,suppressed,{4 * 0.3048},{2 * 0.3048},{0.65 * 0.3048}\n", UnitSystem.SI);
        Assert.Equal((double)us.Tables[0].Rows[0][4]! * 0.0283168, (double)si.Tables[0].Rows[0][4]!, 4);
    }

    [Fact]
    public void RangeWarnings_AndBadInput()
    {
        Assert.Contains(Ok("[Devices]\nName,Type,Size,P,Ha\nV,v-notch,90,,0.1\n").Messages, m => m.Code == "W-WM-003");
        Assert.Contains(Ok("[Devices]\nName,Type,Size,P,Ha\nV,v-notch,60,,0.5\n").Messages, m => m.Code == "W-WM-001");
        var u = new DesignToolContext(UnitSystem.USCustomary);
        Assert.Contains(Tool.Run("[Devices]\nName,Type,Size\nX,orifice,2\n", u).Messages, m => m.Code == "E-WM-002");
        Assert.Contains(Tool.Run("[Devices]\nName,Type,Size\nX,v-notch,200\n", u).Messages, m => m.Code == "E-WM-003");
        Assert.Contains(Tool.Run("[Devices]\nName,Type,Size\nX,suppressed,4\n", u).Messages, m => m.Code == "E-WM-004");
        Assert.Contains(Tool.Run("[Devices]\nName,Type,Size\nX,parshall,5 in\n", u).Messages, m => m.Code == "E-WM-005");
        Assert.Contains(Tool.Run("", u).Messages, m => m.Code == "E-WM-007");
    }
}
