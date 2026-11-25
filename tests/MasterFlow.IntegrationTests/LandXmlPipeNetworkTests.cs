using System.Text;
using MasterFlow.Domain;
using MasterFlow.Persistence;

namespace MasterFlow.IntegrationTests;

public class LandXmlPipeNetworkTests
{
    public static string Storm => File.ReadAllText(SamplePath());

    /// <summary>docs/samples/storm-network.xml, found by walking up to the repository root.</summary>
    public static string SamplePath()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "MasterFlow.slnx"))) d = d.Parent;
        return Path.Combine(d!.FullName, "docs", "samples", "storm-network.xml");
    }

    private static LandXmlNetworkReadResult Read(string xml) => LandXmlPipeNetworks.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Civil3DStyle_Network_ReadsPipesStructuresAndInlets_InFileUnits()
    {
        var r = Read(Storm);
        Assert.Equal(UnitSystem.USCustomary, r.Units);
        Assert.Equal(2, r.Pipes.Count);
        var p1 = r.Pipes[0];
        Assert.Equal((NetworkPipeShape.Circular, 1.5, "CB-1", "MH-1", "RCP"), (p1.Shape, p1.Rise, p1.StartStructure, p1.EndStructure, p1.Material));
        Assert.Equal((100.5, 99.5), (p1.StartInvert!.Value, p1.EndInvert!.Value));
        Assert.Equal(0.01, p1.Slope!.Value, 12); // (100.50 − 99.50) / 100, not the file's slope attribute
        var p2 = r.Pipes[1];
        Assert.Equal(NetworkPipeShape.Elliptical, p2.Shape);
        Assert.Equal(1.0 / 125, p2.Slope!.Value, 12);

        Assert.Equal(3, r.Structures.Count);
        var cb = r.Structures[0];
        Assert.Equal(("Inlet", "Storm A", 5000.0, 2000.0, 105.2, 100.0, "Curb inlet"), (cb.Kind, cb.Network, cb.Northing!.Value, cb.Easting!.Value, cb.RimElevation!.Value, cb.SumpElevation!.Value, cb.Description));
        Assert.Equal("Manhole (circular)", r.Structures[1].Kind);
        Assert.Equal(2, r.Structures[1].Inverts.Count);
        Assert.Equal("Outlet", r.Structures[2].Kind);

        Assert.Contains("RectPipe", Assert.Single(r.Problems));
    }

    [Fact]
    public void MissingInverts_AdverseSlope_AndInchSizes_AreReported_NotFixed()
    {
        var r = Read("""
            <LandXML><Units><Imperial linearUnit="foot"/></Units><PipeNetworks><PipeNetwork name="N">
              <Structs>
                <Struct name="A"><Invert elev="99" flowDir="out" refPipe="up"/></Struct>
                <Struct name="B"><Invert elev="100" flowDir="in" refPipe="up"/></Struct>
              </Structs>
              <Pipes>
                <Pipe name="up" refStart="A" refEnd="B" length="50"><CircPipe diameter="18"/></Pipe>
                <Pipe name="noinv" refStart="A" refEnd="B" length="50"><CircPipe diameter="1.5"/></Pipe>
              </Pipes>
            </PipeNetwork></PipeNetworks></LandXML>
            """);
        Assert.Equal(2, r.Pipes.Count);
        Assert.Equal(18, r.Pipes[0].Rise);                           // not converted
        // drawn A → B but A is the lower end: flow direction taken from the inverts (D-703)
        Assert.Equal(("B", "A", 0.02, true), (r.Pipes[0].StartStructure, r.Pipes[0].EndStructure, Math.Round(r.Pipes[0].Slope!.Value, 12), r.Pipes[0].Reversed));
        Assert.Contains("\"up\" is drawn A → B", Assert.Single(r.FlowNotes));
        Assert.Null(r.Pipes[1].Slope);
        Assert.False(r.Pipes[1].Reversed);
        Assert.Contains(r.Problems, p => p.Contains("looks like inches"));
        Assert.Contains(r.Problems, p => p.Contains("cannot be computed"));
    }

    /// <summary>
    /// A manhole with three pipes drawn out of it (refStart = MH-2), as Civil 3D allows: only P63 runs downhill
    /// from it. The other two are inflows drawn the wrong way, so the network solves with one outlet (D-703).
    /// </summary>
    private const string ReversedPipes = """
        <LandXML><Units><Imperial linearUnit="foot" diameterUnit="inch"/></Units><PipeNetworks><PipeNetwork name="Storm Sewer">
          <Structs>
            <Struct name="OUT" elevRim="104"><OutletStruct/><Invert elev="99.00" flowDir="in" refPipe="P63"/></Struct>
            <Struct name="MH-2" elevRim="106"><CircStruct diameter="48"/>
              <Invert elev="100.00" flowDir="out" refPipe="P63"/><Invert elev="100.30" flowDir="out" refPipe="P68"/>
              <Invert elev="100.30" flowDir="out" refPipe="P69"/><Invert elev="100.50" flowDir="out" refPipe="P70"/></Struct>
            <Struct name="CB-68" elevRim="106"><InletStruct/><Invert elev="101.30" flowDir="in" refPipe="P68"/></Struct>
            <Struct name="CB-69" elevRim="106.5"><InletStruct/><Invert elev="101.00" flowDir="in" refPipe="P69"/></Struct>
            <Struct name="CB-70" elevRim="106.5"><InletStruct/><Invert elev="100.50" flowDir="in" refPipe="P70"/></Struct>
          </Structs>
          <Pipes>
            <Pipe name="P63" refStart="MH-2" refEnd="OUT" length="100"><CircPipe diameter="24"/></Pipe>
            <Pipe name="P68" refStart="MH-2" refEnd="CB-68" length="100"><CircPipe diameter="18"/></Pipe>
            <Pipe name="P69" refStart="MH-2" refEnd="CB-69" length="70"><CircPipe diameter="15"/></Pipe>
            <Pipe name="P70" refStart="MH-2" refEnd="CB-70" length="40"><CircPipe diameter="12"/></Pipe>
          </Pipes>
        </PipeNetwork></PipeNetworks></LandXML>
        """;

    [Fact]
    public void PipesDrawnAgainstTheFlow_AreTurned_FromTheInverts()
    {
        var r = Read(ReversedPipes);
        var by = r.Pipes.ToDictionary(p => p.Name);
        Assert.Equal(("MH-2", "OUT", false), (by["P63"].StartStructure, by["P63"].EndStructure, by["P63"].Reversed));
        Assert.Equal(("CB-68", "MH-2", true, 101.30, 100.30), (by["P68"].StartStructure, by["P68"].EndStructure, by["P68"].Reversed, by["P68"].StartInvert!.Value, by["P68"].EndInvert!.Value));
        Assert.Equal(("CB-69", "MH-2", true), (by["P69"].StartStructure, by["P69"].EndStructure, by["P69"].Reversed));
        // P70 is flat: the slope cannot decide it, but MH-2 already has a lower outlet (P63), so it is an inflow
        Assert.Equal(("CB-70", "MH-2", true), (by["P70"].StartStructure, by["P70"].EndStructure, by["P70"].Reversed));
        Assert.Equal(3, r.FlowNotes.Count);
        Assert.Contains(r.FlowNotes, n => n.Contains("P70") && n.Contains("lower outlet invert"));
        Assert.DoesNotContain(r.Problems, p => p.Contains("do not settle"));
        Assert.Contains(r.Problems, p => p.Contains("\"P70\"") && p.Contains("flat"));

        // the network solves with one outlet at MH-2; V1 there is the largest-Q inflow (P68)
        var net = new StormNetwork
        {
            Units = UnitSystem.USCustomary, Tailwater = TailwaterRule.HalfCriticalPlusDiameter,
            Structures = r.Structures.Select(s => new NetworkStructure { Name = s.Name, Kind = s.Kind, RimElevation = s.RimElevation }).ToList(),
            Pipes = r.Pipes.Select(p => new NetworkPipe
            {
                Name = p.Name, From = p.StartStructure, To = p.EndStructure, Rise = p.Rise, Span = p.Span, Length = p.Length!.Value,
                UpstreamInvert = p.StartInvert!.Value, DownstreamInvert = p.EndInvert!.Value, ManningN = 0.013,
                Discharge = p.Name switch { "P63" => 12, "P68" => 6, "P69" => 4, _ => 2 },
            }).ToList(),
        };
        var res = MasterFlow.Hydraulics.StormNetworkSolver.Solve(net);
        Assert.True(res.Succeeded, string.Join("; ", res.Messages.Select(m => m.Text)));
        Assert.DoesNotContain(res.Messages, m => m.Code is "E-NW-009" or "I-NW-005");
        Assert.Equal(3, res.Pipes.Count(p => p.Pipe.To == "MH-2"));
    }

    [Fact]
    public void TiedOutletInverts_AreReported_NotGuessed()
    {
        var r = Read("""
            <LandXML><Units><Imperial linearUnit="foot"/></Units><PipeNetworks><PipeNetwork name="N">
              <Structs>
                <Struct name="M"><Invert elev="100" refPipe="a"/><Invert elev="100" refPipe="b"/></Struct>
                <Struct name="X"><Invert elev="100" refPipe="a"/></Struct>
                <Struct name="Y"><Invert elev="100" refPipe="b"/></Struct>
              </Structs>
              <Pipes>
                <Pipe name="a" refStart="M" refEnd="X" length="50"><CircPipe diameter="1.5"/></Pipe>
                <Pipe name="b" refStart="M" refEnd="Y" length="50"><CircPipe diameter="1.5"/></Pipe>
              </Pipes>
            </PipeNetwork></PipeNetworks></LandXML>
            """);
        Assert.Empty(r.FlowNotes);
        Assert.Contains(r.Problems, p => p.Contains("\"M\"") && p.Contains("do not settle") && p.Contains("a (invert 100 here, 100 at X)"));
    }

    [Fact]
    public void NoNetwork_Unreadable_AndDtd_AreRefused()
    {
        Assert.Contains(Read("<LandXML/>").Problems, p => p.Contains("no pipe networks"));
        Assert.Contains("Not a LandXML", Assert.Single(Read("<Project/>").Problems));
        var dtd = Read("""<?xml version="1.0"?><!DOCTYPE LandXML [<!ENTITY x SYSTEM "file:///etc/hostname">]><LandXML><PipeNetworks><PipeNetwork name="&x;"/></PipeNetworks></LandXML>""");
        Assert.Empty(dtd.Structures);
        Assert.Contains("Not a readable XML", Assert.Single(dtd.Problems));
    }

    [Fact]
    public void Structures_RoundTrip_InTheProjectFile()
    {
        var r = Read(Storm);
        var project = new Project { Structures = r.Structures };
        var back = ProjectFile.Deserialize(ProjectFile.Serialize(project));
        Assert.Equal(3, back.Structures.Count);
        Assert.Equal(r.Structures[1].Inverts, back.Structures[1].Inverts);
        Assert.Empty(ProjectFile.Deserialize(ProjectFile.Serialize(new Project())).Structures);
    }
}
