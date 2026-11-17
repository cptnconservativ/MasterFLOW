using MasterFlow.Domain;

namespace MasterFlow.Hydraulics;

public enum FlowRegime
{
    Subcritical,
    NearCritical,
    Supercritical,
}

/// <summary>Hydraulic properties of the section at a given depth and discharge.</summary>
public sealed record SectionState
{
    public double Depth { get; init; }
    public double Area { get; init; }
    public double WettedPerimeter { get; init; }
    public double HydraulicRadius { get; init; }
    public double TopWidth { get; init; }
    /// <summary>Hydraulic depth D = A / T.</summary>
    public double HydraulicDepth { get; init; }
    public double Velocity { get; init; }
    public double VelocityHead { get; init; }
    /// <summary>Specific energy E = y + V²/2g (relative to the channel invert).</summary>
    public double SpecificEnergy { get; init; }
    /// <summary>Froude number Fr = V / √(g D).</summary>
    public double Froude { get; init; }
}

/// <summary>
/// The complete result of a steady uniform-flow calculation. When <see cref="Succeeded"/> is false,
/// numeric properties are NaN and must not be displayed as results.
/// </summary>
public sealed record UniformFlowResult
{
    public required UniformFlowInput Input { get; init; }
    public required UnitConstants Constants { get; init; }
    public bool Succeeded { get; init; }
    public IReadOnlyList<CalcMessage> Messages { get; init; } = [];
    public IReadOnlyList<string> Trace { get; init; } = [];

    // Governing values (solved unknown filled in).
    public double ManningN { get; init; } = double.NaN;
    public double Slope { get; init; } = double.NaN;
    public double Discharge { get; init; } = double.NaN;

    /// <summary>Normal-depth state.</summary>
    public SectionState Normal { get; init; } = new();

    /// <summary>Conveyance K = (k/n) A R^(2/3), so that Q = K √S.</summary>
    public double Conveyance { get; init; } = double.NaN;

    /// <summary>Uniform-flow section factor A R^(2/3) = Q n / (k √S).</summary>
    public double UniformFlowSectionFactor { get; init; } = double.NaN;

    /// <summary>State at critical depth for the same discharge.</summary>
    public SectionState Critical { get; init; } = new();

    /// <summary>Critical-flow section factor Z = A √D at critical depth (= Q / √g).</summary>
    public double CriticalSectionFactor { get; init; } = double.NaN;

    /// <summary>Slope at which the given discharge would flow uniformly at critical depth (same n).</summary>
    public double CriticalSlope { get; init; } = double.NaN;

    public FlowRegime Regime { get; init; }

    public IEnumerable<CalcMessage> Errors => Messages.Where(m => m.Severity == MessageSeverity.Error);
    public IEnumerable<CalcMessage> Warnings => Messages.Where(m => m.Severity == MessageSeverity.Warning);
}
