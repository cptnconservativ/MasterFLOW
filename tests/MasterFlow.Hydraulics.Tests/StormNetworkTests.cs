using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Hydraulics.Tests;

/// <summary>
/// Reference values from validation/storm_hgl.py (independent implementation; output in
/// validation/storm_hgl_output.txt). Tolerances set before comparison: 0.002 ft for HGL/EGL and
/// structure losses, 0.001 ft/s for velocities.
/// </summary>
public class StormNetworkTests
{
    private const double Tol = 0.002, TolV = 0.001;

    private static NetworkPipe Pipe(string name, string from, string to, double d, double l, double up, double dn, double q) =>
        new() { Name = name, From = from, To = to, Rise = d, Span = d, Length = l, UpstreamInvert = up, DownstreamInvert = dn, ManningN = 0.013, Discharge = q };

    private static readonly NetworkStructure[] Structs =
    [
        new() { Name = "OUT", Kind = "Outlet", RimElevation = 105 },
        new() { Name = "MH-2", Kind = "Manhole", RimElevation = 106.0 },
        new() { Name = "CB-3", Kind = "Inlet", RimElevation = 106.5 },
        new() { Name = "CB-4", Kind = "Inlet", RimElevation = 104.0 },
    ];

    private static StormNetwork NetA() => new()
    {
        Structures = Structs, Tailwater = TailwaterRule.Elevation, TailwaterElevation = 102.50,
        Pipes = [Pipe("P1", "MH-2", "OUT", 2.0, 200, 100.80, 100.00, 20), Pipe("P2", "CB-3", "MH-2", 1.5, 150, 102.00, 101.10, 12), Pipe("P3", "CB-4", "MH-2", 1.25, 60, 101.80, 101.30, 8)],
        Criteria = NetworkCriteria.RapidCity2012,
    };

    private static StormNetwork NetB() => new()
    {
        Structures = Structs, Tailwater = TailwaterRule.HalfCriticalPlusDiameter,
        Pipes = [Pipe("P1", "MH-2", "OUT", 2.0, 200, 100.40, 100.00, 8), Pipe("P2", "CB-3", "MH-2", 1.5, 150, 101.45, 101.10, 5), Pipe("P3", "CB-4", "MH-2", 1.25, 60, 101.50, 101.30, 3)],
    };

    [Fact]
    public void NetworkA_PressureFlow_MatchesIndependentCalculation()
    {
        var r = StormNetworkSolver.Solve(NetA());
        Assert.True(r.Succeeded, string.Join("; ", r.Messages.Select(m => m.Text)));
        void P(string n, double hu, double hd, double eu, double ed, double vu)
        {
            var p = r.Pipe(n)!;
            Assert.Equal(hu, p.HglUp, Tol); Assert.Equal(hd, p.HglDown, Tol); Assert.Equal(eu, p.EglUp, Tol); Assert.Equal(ed, p.EglDown, Tol); Assert.Equal(vu, p.VelocityUp, TolV);
            Assert.True(p.FlowsFull);
        }
        P("P1", 104.063193, 102.500000, 104.693026, 103.129833, 6.366198);
        P("P2", 106.471439, 104.513873, 107.188049, 105.230483, 6.790611);
        P("P3", 105.434098, 104.513873, 106.094525, 105.174301, 6.518986);
        Assert.Equal(104.513873, r.Structure("MH-2")!.Hgl, Tol);
        Assert.Equal(0.450680, r.Structure("MH-2")!.Loss, Tol);
        Assert.Equal(107.367201, r.Structure("CB-3")!.Hgl, Tol);
        Assert.Equal(0.895762, r.Structure("CB-3")!.Loss, Tol);
        Assert.Equal(106.259632, r.Structure("CB-4")!.Hgl, Tol);
        Assert.Equal(1.486127, r.Structure("MH-2")!.HglClearance!.Value, Tol);
        Assert.Contains("Case IV", r.Structure("MH-2")!.LossMethod);
        Assert.Contains("Case VI", r.Structure("CB-3")!.LossMethod);
    }

    [Fact]
    public void NetworkA_RapidCityCriteria_FlagRimClearanceAndPressureFlow()
    {
        var r = StormNetworkSolver.Solve(NetA());
        var w = r.Messages.Where(m => m.Code == "W-NW-001").Select(m => m.Text).ToList();
        Assert.Equal(2, w.Count); // CB-3 and CB-4 have the HGL above the rim; MH-2 has 1.49 ft ≥ 1 ft
        Assert.Contains(w, t => t.Contains("CB-3"));
        Assert.Contains(w, t => t.Contains("CB-4"));
        Assert.Equal(3, r.Messages.Count(m => m.Code == "W-NW-005"));
    }

    [Fact]
    public void NetworkB_PartFullBackwater_MatchesIndependentCalculation()
    {
        var r = StormNetworkSolver.Solve(NetB());
        Assert.True(r.Succeeded, string.Join("; ", r.Messages.Select(m => m.Text)));
        void P(string n, double hu, double hd, double eu, double ed, double vu, double vd)
        {
            var p = r.Pipe(n)!;
            Assert.Equal(hu, p.HglUp, Tol); Assert.Equal(hd, p.HglDown, Tol); Assert.Equal(eu, p.EglUp, Tol); Assert.Equal(ed, p.EglDown, Tol);
            Assert.Equal(vu, p.VelocityUp, TolV); Assert.Equal(vd, p.VelocityDown, TolV);
            Assert.False(p.FlowsFull);
        }
        P("P1", 101.807148, 101.503431, 101.985421, 101.658405, 3.386961, 3.157881);
        P("P2", 102.592242, 101.960386, 102.778595, 102.313752, 3.462867, 4.768478);
        P("P3", 102.330448, 101.996644, 102.517075, 102.279690, 3.465406, 4.267723);
        Assert.Equal(101.897080, r.Structure("MH-2")!.Hgl, Tol);
        Assert.Equal(0.089931, r.Structure("MH-2")!.Loss, Tol);
        Assert.Equal(102.825184, r.Structure("CB-3")!.Hgl, Tol);
        Assert.Equal(102.563731, r.Structure("CB-4")!.Hgl, Tol);
        // P2 drops into MH-2: its outlet is at critical depth, above the structure water level.
        Assert.Equal(r.Pipe("P2")!.CriticalDepth, r.Pipe("P2")!.DepthDown, 6);
    }

    [Fact]
    public void Profile_IsContinuous_AndEglNeverBelowHgl()
    {
        var r = StormNetworkSolver.Solve(NetB());
        foreach (var p in r.Pipes)
        {
            Assert.Equal(p.HglUp, p.Profile[0].Hgl, 12);
            Assert.Equal(p.HglDown, p.Profile[^1].Hgl, 12);
            Assert.All(p.Profile, pt => Assert.True(pt.Egl >= pt.Hgl));
            Assert.True(p.EnergyLoss > 0);
        }
        Assert.Equal(["P2", "P1"], r.PathToOutfall("CB-3").Select(p => p.Pipe.Name));
    }

    [Fact]
    public void SteepPipe_FreeOutfall_IsSupercritical_WithEntranceAtCritical()
    {
        var net = new StormNetwork
        {
            Structures = [new() { Name = "A", RimElevation = 120 }, new() { Name = "B" }],
            Pipes = [Pipe("S", "A", "B", 1.5, 200, 110, 100, 10)], // 5 % slope
            Tailwater = TailwaterRule.FreeOutfall,
            Criteria = NetworkCriteria.RapidCity2012,
        };
        var r = StormNetworkSolver.Solve(net);
        Assert.True(r.Succeeded);
        var p = r.Pipe("S")!;
        Assert.Equal("Steep", p.Regime);
        Assert.Equal(p.CriticalDepth, p.DepthUp, 9);
        Assert.True(p.DepthDown < p.CriticalDepth);
        Assert.True(p.DepthDown > p.NormalDepth - 1e-3);
        Assert.Null(p.JumpStation);
        Assert.Contains(r.Messages, m => m.Code == "W-NW-007");
    }

    [Fact]
    public void SteepPipe_HighTailwater_JumpsInsideThePipe()
    {
        var net = new StormNetwork
        {
            Structures = [new() { Name = "A" }, new() { Name = "B" }],
            Pipes = [Pipe("S", "A", "B", 2.0, 300, 106, 100, 10)], // 2 % slope, steep for 10 cfs in 24 in
            Tailwater = TailwaterRule.Elevation, TailwaterElevation = 102.6, // outlet submerged above the crown
        };
        var r = StormNetworkSolver.Solve(net);
        var p = r.Pipe("S")!;
        Assert.Equal("Steep", p.Regime);
        Assert.NotNull(p.JumpStation);
        Assert.InRange(p.JumpStation!.Value, 1, 299);
        Assert.Equal(102.6, p.HglDown, 9);
        Assert.Equal(p.CriticalDepth, p.DepthUp, 9);
        // Momentum is conserved across the jump: sequent depths straddle the jump station.
        var before = p.Profile.Last(pt => pt.Station < p.JumpStation);
        var after = p.Profile.First(pt => pt.Station >= p.JumpStation);
        Assert.True(after.Hgl - after.Invert > before.Hgl - before.Invert);
    }

    [Fact]
    public void SteepPipe_LowTailwater_SweptOut_NoJumpInPipe()
    {
        // Tailwater depth 1.6 ft is below the sequent depth of the supercritical flow: the jump forms
        // outside the pipe and the outlet runs supercritical (HGL below the tailwater).
        var net = new StormNetwork
        {
            Structures = [new() { Name = "A" }, new() { Name = "B" }],
            Pipes = [Pipe("S", "A", "B", 2.0, 300, 106, 100, 10)],
            Tailwater = TailwaterRule.Elevation, TailwaterElevation = 101.6,
        };
        var p = StormNetworkSolver.Solve(net).Pipe("S")!;
        Assert.Null(p.JumpStation);
        Assert.Equal(p.NormalDepth, p.DepthDown, 3);
    }

    [Fact]
    public void Elliptical_And_Surcharged_Pipes_Solve()
    {
        var net = new StormNetwork
        {
            Structures = [new() { Name = "A" }, new() { Name = "B" }],
            Pipes = [new() { Name = "E", From = "A", To = "B", Shape = NetworkPipeShape.Elliptical, Rise = 1.9333, Span = 3.0833, Length = 100, UpstreamInvert = 100.5, DownstreamInvert = 100, ManningN = 0.013, Discharge = 60 }],
            Tailwater = TailwaterRule.Crown,
        };
        var r = StormNetworkSolver.Solve(net);
        Assert.True(r.Succeeded);
        var p = r.Pipe("E")!;
        Assert.Equal("Pressure", p.Regime);   // 60 cfs exceeds the free-surface capacity
        Assert.True(p.FlowsFull);
        Assert.True(p.HglUp > 100.5 + 1.9333);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("split")]
    [InlineData("loop")]
    [InlineData("noQ")]
    [InlineData("noTw")]
    public void InvalidNetworks_AreRefused_WithNoNumbers(string which)
    {
        var s = new NetworkStructure[] { new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" } };
        var a = Pipe("1", "A", "B", 1.5, 100, 101, 100, 5);
        StormNetwork net = which switch
        {
            "missing" => new() { Structures = s, Pipes = [a with { To = "X" }] },
            "split" => new() { Structures = s, Pipes = [a, a with { Name = "2", To = "C" }] },
            "loop" => new() { Structures = s, Pipes = [a, a with { Name = "2", From = "B", To = "A" }] },
            "noQ" => new() { Structures = s, Pipes = [a with { Discharge = double.NaN }] },
            _ => new() { Structures = s, Pipes = [a], Tailwater = TailwaterRule.Elevation },
        };
        var r = StormNetworkSolver.Solve(net);
        Assert.False(r.Succeeded);
        Assert.Empty(r.Pipes);
        Assert.Contains(r.Messages, m => m.Severity == MessageSeverity.Error);
    }

    [Theory]
    [InlineData(StructureLossMethod.InletOnMainLine, 0.5)]
    [InlineData(StructureLossMethod.InletWithBranchLateral, 0.25)]
    [InlineData(StructureLossMethod.ManholeWith45Lateral, 0.5)]
    [InlineData(StructureLossMethod.ManholeWith90Lateral, 0.25)]
    [InlineData(StructureLossMethod.ManholeWith60Lateral, 0.35)]
    [InlineData(StructureLossMethod.ManholeWith22Lateral, 0.75)]
    [InlineData(StructureLossMethod.Wye45, 0.75)]
    public void Idcm_Figure46_47_Coefficients(StructureLossMethod m, double c)
    {
        var (_, _, h) = StormNetworkSolver.Loss(new NetworkStructure { LossMethod = m }, new StormNetwork(), 1, 1.0, 0.8);
        Assert.Equal(1.0 - c * 0.8, h, 12);
    }

    [Theory]
    [InlineData(StructureLossMethod.BeginningOfLine, 1.25)]
    [InlineData(StructureLossMethod.Bend90, 0.50)]
    [InlineData(StructureLossMethod.Bend60, 0.43)]
    [InlineData(StructureLossMethod.Bend45, 0.35)]
    [InlineData(StructureLossMethod.Bend22, 0.20)]
    public void Idcm_Figure47_OutletVelocityCases(StructureLossMethod m, double k)
    {
        var (_, _, h) = StormNetworkSolver.Loss(new NetworkStructure { LossMethod = m }, new StormNetwork(), 1, 2.0, 0.5);
        Assert.Equal(k * 2.0, h, 12);
    }

    [Fact]
    public void StandardMethod_UsesStructureK_ElseNetworkDefault()
    {
        var net = new StormNetwork { DefaultJunctionK = 0.4 };
        Assert.Equal(0.8, StormNetworkSolver.Loss(new NetworkStructure { LossMethod = StructureLossMethod.StandardK }, net, 1, 2.0, 0).Loss, 12);
        Assert.Equal(3.0, StormNetworkSolver.Loss(new NetworkStructure { LossMethod = StructureLossMethod.StandardK, JunctionK = 1.5 }, net, 1, 2.0, 0).Loss, 12);
    }
}
