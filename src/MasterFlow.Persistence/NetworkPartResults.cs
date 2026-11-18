using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Persistence;

/// <summary>Hydraulic results for one pipe, flattened for export to Civil 3D (LandXML, AutoLISP write-back, CSV).</summary>
public sealed record PipePartResult(
    string Name, string From, string To, double Discharge, double FullFlowCapacity, double CapacityRatio,
    double VelocityUp, double VelocityDown, double FullFlowVelocity, double DepthUp, double DepthDown,
    double NormalDepth, double CriticalDepth, double HglUp, double HglDown, double EglUp, double EglDown,
    double FrictionLoss, string Regime, string Condition, bool FlowsFull, double MinorDischarge, double MinorVelocity,
    IReadOnlyList<string> Failures);

/// <summary>Hydraulic results for one structure, flattened for export to Civil 3D.</summary>
public sealed record StructurePartResult(
    string Name, string Kind, double? Rim, double Hgl, double Egl, double Loss, string LossMethod,
    double? HglClearance, double? EglClearance, bool IsOutfall, double OutletHgl, IReadOnlyList<string> Failures);

/// <summary>
/// Turns a solved <see cref="StormNetworkResult"/> into per-part rows, with the criteria warnings that name each
/// part (the solver words them as Pipe "name" / Structure "name"). Values stay in the network's units.
/// </summary>
public static class NetworkPartResults
{
    public static IReadOnlyList<PipePartResult> Pipes(StormNetworkResult r) => r.Pipes.Select(p => new PipePartResult(
        p.Pipe.Name, p.Pipe.From, p.Pipe.To, p.Pipe.Discharge, p.FullFlowCapacity,
        double.IsFinite(p.FullFlowCapacity) && p.FullFlowCapacity > 0 ? p.Pipe.Discharge / p.FullFlowCapacity : double.NaN,
        p.VelocityUp, p.VelocityDown, p.FullFlowVelocity, p.DepthUp, p.DepthDown, p.NormalDepth, p.CriticalDepth,
        p.HglUp, p.HglDown, p.EglUp, p.EglDown, p.EnergyLoss, p.Regime, p.Condition, p.FlowsFull, p.MinorDischarge, p.MinorVelocity,
        Failures(r, $"Pipe \"{p.Pipe.Name}\""))).ToList();

    public static IReadOnlyList<StructurePartResult> Structures(StormNetworkResult r) => r.Structures.Select(s =>
    {
        // HGL leaving the structure = upstream-end HGL of its outlet pipe (the structure HGL less the structure loss).
        var outlet = r.Pipes.FirstOrDefault(p => p.Pipe.From == s.Structure.Name);
        return new StructurePartResult(s.Structure.Name, s.Structure.Kind, s.Structure.RimElevation, s.Hgl, s.Egl,
            s.IsOutfall ? double.NaN : s.Loss, s.LossMethod, s.HglClearance, s.EglClearance, s.IsOutfall,
            outlet?.HglUp ?? s.Hgl, Failures(r, $"Structure \"{s.Structure.Name}\""));
    }).ToList();

    /// <summary>"OK" when no criteria warning names the part, otherwise the warning codes (e.g. "W-NW-001 W-NW-005").</summary>
    public static string Status(IReadOnlyList<string> failures) => failures.Count == 0 ? "OK" : string.Join(" ", failures);

    private static IReadOnlyList<string> Failures(StormNetworkResult r, string who) => r.Messages
        .Where(m => m.Severity == MessageSeverity.Warning && m.Text.StartsWith(who, StringComparison.Ordinal)
                    && (m.Text.Length == who.Length || m.Text[who.Length] is ':' or ' '))
        .Select(m => m.Code).Distinct().ToList();

    internal static string Num(double v, string format = "0.######") => double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "";

    /// <summary>Unit labels for the network's unit system.</summary>
    public static (string Length, string Flow, string Velocity) UnitLabels(UnitSystem u) =>
        u == UnitSystem.SI ? ("m", "m³/s", "m/s") : ("ft", "cfs", "ft/s");
}
