using MasterFlow.Domain;

namespace MasterFlow.Hydraulics;

public enum FlowRegime
{
    Subcritical,
    NearCritical,
    Supercritical,
    /// <summary>Froude number undefined (conduit flowing full: no free-surface width).</summary>
    Undefined,
}

/// <summary>Additional results for closed conduits flowing partly full.</summary>
public sealed record ConduitResult
{
    public double Rise { get; init; }
    /// <summary>Depth as a percentage of the rise (d/D × 100).</summary>
    public double PercentFull { get; init; }
    public double FullArea { get; init; }
    /// <summary>Discharge with the conduit just full at the channel slope (gravity flow, no surcharge).</summary>
    public double FullFlowDischarge { get; init; }
    public double FullFlowVelocity { get; init; }
    /// <summary>Slope at which the conduit would carry the discharge just full.</summary>
    public double FullFlowSlope { get; init; }
    /// <summary>Maximum free-surface discharge (occurs below the crown).</summary>
    public double MaxDischarge { get; init; }
    public double DepthAtMaxDischarge { get; init; }
    /// <summary>Second depth carrying the same discharge (exists when Q &gt; full-flow Q), otherwise NaN.</summary>
    public double AlternateNormalDepth { get; init; } = double.NaN;
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

    /// <summary>Present only for closed conduits.</summary>
    public ConduitResult? Conduit { get; init; }

    public IEnumerable<CalcMessage> Errors => Messages.Where(m => m.Severity == MessageSeverity.Error);
    public IEnumerable<CalcMessage> Warnings => Messages.Where(m => m.Severity == MessageSeverity.Warning);
}
