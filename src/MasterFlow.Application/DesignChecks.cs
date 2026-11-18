using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

public enum CheckStatus { Pass, Fail, NotEvaluated }

/// <summary>One design check: the computed value, the limit, the outcome and the basis.</summary>
public sealed record DesignCheck(string Name, string Symbol, double Actual, double Limit, string Unit, CheckStatus Status, string Basis)
{
    public string StatusText => Status switch { CheckStatus.Pass => "OK", CheckStatus.Fail => "FAILS", _ => "Not evaluated" };
    /// <summary>Limit ÷ actual for upper limits (factor of safety), actual ÷ limit for lower limits; NaN when not evaluated.</summary>
    public double Ratio { get; init; } = double.NaN;
}

/// <summary>
/// Checks normal-flow results against <see cref="DesignCriteria"/>. All values stay in the calculation's
/// unit system. Shear follows FHWA HEC-15 (3rd ed., 2005), eq. 2.3: the design shear at the deepest
/// point of a straight channel is τd = γ·d·S, with d the maximum depth (yn) and S the channel slope;
/// the lining is acceptable when τd ≤ τp (factor of safety τp/τd ≥ 1). Bend shear (HEC-15 Kb) and
/// steep-slope methods are not included. Shear and freeboard apply to open channels only.
/// </summary>
public static class DesignChecks
{
    public static IReadOnlyList<DesignCheck> Evaluate(DesignCriteria? criteria, UniformFlowResult r)
    {
        if (criteria is null || criteria.IsEmpty || !r.Succeeded) return [];
        var c = r.Constants;
        var list = new List<DesignCheck>();
        bool conduit = r.Input.Section is CircularSection or EllipticalSection;

        if (criteria.PermissibleShear is { } tp)
        {
            double td = c.UnitWeight * r.Normal.Depth * r.Slope;
            if (conduit)
                list.Add(new("Lining shear", "τd ≤ τp", td, tp, c.Stress, CheckStatus.NotEvaluated, "Shear check applies to open-channel linings, not pipes."));
            else
                list.Add(new("Lining shear", "τd = γ·yn·S ≤ τp", td, tp, c.Stress, td <= tp ? CheckStatus.Pass : CheckStatus.Fail,
                    $"FHWA HEC-15 eq. 2.3, straight channel; γ = {c.UnitWeight.ToString("G4", System.Globalization.CultureInfo.InvariantCulture)} {(c.System == UnitSystem.SI ? "N/m³" : "lb/ft³")}") { Ratio = tp / td });
        }

        if (criteria.RequiredFreeboard is { } fbReq)
        {
            double? available = null; string basis;
            if (conduit) { basis = "Freeboard applies to open channels; for pipes use d/D and capacity."; }
            else if (r.Input.Section is IrregularSection ir && r.Irregular is { } irr)
            {
                double bank = Math.Min(ir.Points[0].Elevation, ir.Points[^1].Elevation);
                available = bank - irr.WaterSurfaceElevation;
                basis = $"Lower end point (elevation {UnitFormat.Number(bank, 3)} {c.Length}) minus the normal water surface.";
            }
            else if (r.Input.Section.ChannelDepth is { } cd && cd > 0)
            {
                available = cd - r.Normal.Depth;
                basis = "Channel depth minus normal depth.";
            }
            else basis = "Set the channel depth to check freeboard.";
            list.Add(available is { } a
                ? new("Freeboard", "f = top − WSE ≥ f req.", a, fbReq, c.Length, a >= fbReq ? CheckStatus.Pass : CheckStatus.Fail, basis) { Ratio = fbReq > 0 ? a / fbReq : double.PositiveInfinity }
                : new("Freeboard", "f ≥ f req.", double.NaN, fbReq, c.Length, CheckStatus.NotEvaluated, basis));
        }

        double v = r.Normal.Velocity;
        if (criteria.MaxVelocity is { } vmax)
            list.Add(new("Maximum velocity", "V ≤ Vmax", v, vmax, c.Velocity, v <= vmax ? CheckStatus.Pass : CheckStatus.Fail, "Normal-flow mean velocity.") { Ratio = vmax / v });
        if (criteria.MinVelocity is { } vmin)
            list.Add(new("Minimum velocity", "V ≥ Vmin", v, vmin, c.Velocity, v >= vmin ? CheckStatus.Pass : CheckStatus.Fail, "Normal-flow mean velocity.") { Ratio = vmin > 0 ? v / vmin : double.PositiveInfinity });
        return list;
    }

    /// <summary>Warnings for failed checks, in the same form as engine messages (codes W-C-00x).</summary>
    public static IReadOnlyList<CalcMessage> Messages(IReadOnlyList<DesignCheck> checks)
    {
        var list = new List<CalcMessage>();
        foreach (var k in checks.Where(k => k.Status == CheckStatus.Fail))
        {
            string code = k.Name switch { "Lining shear" => "W-C-001", "Freeboard" => "W-C-002", "Maximum velocity" => "W-C-003", _ => "W-C-004" };
            list.Add(new CalcMessage(MessageSeverity.Warning, code,
                $"{k.Name} check fails: {UnitFormat.Number(k.Actual, 3)} {k.Unit} against a limit of {UnitFormat.Number(k.Limit, 3)} {k.Unit}."));
        }
        return list;
    }
}
