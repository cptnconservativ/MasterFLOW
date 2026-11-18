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
        Assert.Equal(-0.02, r.Pipes[0].Slope!.Value, 12);            // reported as adverse, kept
        Assert.Null(r.Pipes[1].Slope);
        Assert.Contains(r.Problems, p => p.Contains("looks like inches"));
        Assert.Contains(r.Problems, p => p.Contains("adverse"));
        Assert.Contains(r.Problems, p => p.Contains("cannot be computed"));
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
