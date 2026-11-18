using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

/// <summary>One case of a sensitivity table: the base case, or n or slope multiplied by a factor.</summary>
public sealed record SensitivityRow(
    string Case, string Parameter, double Factor, double ManningN, double Slope,
    double Depth, double Discharge, double Velocity, double Froude, FlowRegime Regime,
    double Unknown, double UnknownChangePercent, string? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Sensitivity of a single calculation to Manning's n and slope. Each case is a full re-solve of the same
/// calculation (same unknown, same validation and convergence checks) with one input multiplied by a factor;
/// a case that fails is shown as failed with its error, never interpolated.
/// </summary>
public sealed record SensitivityTable(SolveFor Unknown, string UnknownLabel, IReadOnlyList<SensitivityRow> Rows, IReadOnlyList<string> Notes)
{
    public SensitivityRow? Base => Rows.FirstOrDefault(r => r.Parameter == "");
}

public static class SensitivityAnalysis
{
    /// <summary>n ± 10 % and ± 20 %: the usual spread of published n for one material or channel condition.</summary>
    public static IReadOnlyList<double> DefaultRoughnessFactors { get; } = [0.8, 0.9, 1.1, 1.2];
    /// <summary>Slope − 50 %, − 25 %, + 25 %, + 50 %: survey and grading tolerance on flat slopes.</summary>
    public static IReadOnlyList<double> DefaultSlopeFactors { get; } = [0.5, 0.75, 1.25, 1.5];

    public static string UnknownLabelOf(SolveFor f, UnitConstants c) => f switch
    {
        SolveFor.Discharge => $"Q ({c.Discharge})",
        SolveFor.NormalDepth => $"yn ({c.Length})",
        SolveFor.Slope => "S",
        _ => "n",
    };

    public static SensitivityTable Build(UniformFlowInput input, IReadOnlyList<double>? roughnessFactors = null, IReadOnlyList<double>? slopeFactors = null)
    {
        var c = UnitConstants.For(input.Units);
        var notes = new List<string>();
        var rows = new List<SensitivityRow>();
        var baseResult = UniformFlowCalculator.Solve(input);
        rows.Add(Row("Base", "", 1, baseResult, null));
        double baseUnknown = rows[0].Unknown;

        if (input.SolveFor == SolveFor.Roughness) notes.Add("n is the unknown, so n is not varied.");
        else
            foreach (double f in roughnessFactors ?? DefaultRoughnessFactors)
                rows.Add(Row($"n {Pct(f)}", "n", f, UniformFlowCalculator.Solve(ScaleN(input, f)), baseUnknown));
        if (input.SolveFor == SolveFor.Slope) notes.Add("Slope is the unknown, so slope is not varied.");
        else
            foreach (double f in slopeFactors ?? DefaultSlopeFactors)
                rows.Add(Row($"S {Pct(f)}", "S", f, UniformFlowCalculator.Solve(input with { Slope = input.Slope * f }), baseUnknown));

        if (input.Section is IrregularSection { HasPointRoughness: true })
            notes.Add("n varied by multiplying every per-point n (and the default n) by the factor.");
        notes.Add(input.SolveFor switch
        {
            SolveFor.Discharge => "Depth is held; the discharge the channel carries at that depth changes.",
            SolveFor.NormalDepth => "Discharge is held; the normal depth changes.",
            SolveFor.Slope => "Discharge and depth are held; the slope needed changes.",
            _ => "Discharge and depth are held; the n implied changes.",
        });
        if (!baseResult.Succeeded) notes.Add("The base case did not solve; changes are not computed.");
        return new SensitivityTable(input.SolveFor, UnknownLabelOf(input.SolveFor, c), rows, notes);

        SensitivityRow Row(string label, string par, double f, UniformFlowResult r, double? baseU)
        {
            if (!r.Succeeded)
                return new SensitivityRow(label, par, f, Scaled(input.ManningN, par == "n" ? f : 1), input.Slope * (par == "S" ? f : 1),
                    double.NaN, double.NaN, double.NaN, double.NaN, FlowRegime.Undefined, double.NaN, double.NaN,
                    string.Join(" ", r.Errors.Select(e => e.Text)) is { Length: > 0 } t ? t : "Did not solve.");
            double u = input.SolveFor switch
            {
                SolveFor.Discharge => r.Discharge,
                SolveFor.NormalDepth => r.Normal.Depth,
                SolveFor.Slope => r.Slope,
                _ => r.ManningN,
            };
            double ch = baseU is { } b && double.IsFinite(b) && b != 0 ? 100 * (u - b) / b : par == "" ? 0 : double.NaN;
            return new SensitivityRow(label, par, f, r.ManningN, r.Slope, r.Normal.Depth, r.Discharge, r.Normal.Velocity, r.Normal.Froude, r.Regime, u, ch, null);
        }
    }

    private static double Scaled(double v, double f) => v * f;

    private static string Pct(double f)
    {
        double p = Math.Round(100 * (f - 1), 1);
        return (p > 0 ? "+" : p < 0 ? "−" : "±") + Math.Abs(p).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " %";
    }

    private static UniformFlowInput ScaleN(UniformFlowInput input, double f)
    {
        var scaled = input with { ManningN = input.ManningN * f };
        if (input.Section is IrregularSection { HasPointRoughness: true } ir)
            scaled = scaled with { Section = ir with { Points = ir.Points.Select(p => p.ManningN is { } n ? p with { ManningN = n * f } : p).ToList() } };
        return scaled;
    }

    /// <summary>Column headings shared by the reports, the workbook and the copy text.</summary>
    public static IReadOnlyList<string> Headers(SensitivityTable t, UnitConstants c) =>
        ["Case", "n", "S", $"yn ({c.Length})", $"Q ({c.Discharge})", $"V ({c.Velocity})", "Fr", "Regime", $"Change in {t.UnknownLabel} (%)"];

    /// <summary>Formatted cells for one row (failed rows show the error in the last column).</summary>
    public static IReadOnlyList<string> Cells(SensitivityRow r)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v, string fmt) => double.IsFinite(v) ? v.ToString(fmt, inv) : "—";
        if (!r.Succeeded) return [r.Case, F(r.ManningN, "0.0000"), F(r.Slope, "0.000000"), "—", "—", "—", "—", "failed", r.Error!];
        return [r.Case, F(r.ManningN, "0.0000"), F(r.Slope, "0.000000"), F(r.Depth, "F3"), F(r.Discharge, "F3"), F(r.Velocity, "F3"), F(r.Froude, "F3"), r.Regime.ToString(),
            r.Parameter == "" ? "base" : (r.UnknownChangePercent > 0 ? "+" : "") + F(r.UnknownChangePercent, "F1")];
    }

    /// <summary>Tab-separated text for pasting into Excel.</summary>
    public static string ToDelimited(SensitivityTable t, UnitConstants c) =>
        string.Join("\n", new[] { string.Join("\t", Headers(t, c)) }.Concat(t.Rows.Select(r => string.Join("\t", Cells(r)))).Concat(t.Notes)) + "\n";
}
