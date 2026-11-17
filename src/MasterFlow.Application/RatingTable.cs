using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application;

public enum RatingTableMode
{
    /// <summary>Rows at fixed depth increments; discharge is computed (depth → flow).</summary>
    DischargeFromDepth,
    /// <summary>Rows at fixed discharge increments; normal depth is solved (flow → depth).</summary>
    DepthFromDischarge,
}

public sealed record RatingTableRequest(UniformFlowInput Base, RatingTableMode Mode, double Start, double End, double Increment);

public sealed record RatingTableRow(double Depth, double Discharge, double Area, double Velocity, double TopWidth, double Froude, double CriticalDepth, FlowRegime Regime);

public sealed record RatingTable(RatingTableRequest Request, IReadOnlyList<RatingTableRow> Rows, IReadOnlyList<CalcMessage> Messages)
{
    public bool Succeeded => Messages.All(m => m.Severity != MessageSeverity.Error);
}

/// <summary>
/// Builds rating tables by running the same <see cref="UniformFlowCalculator"/> once per row, so every
/// row is subject to the same validation and convergence checks as a single calculation. A row that
/// fails is omitted and reported; it is never interpolated.
/// </summary>
public static class RatingTableBuilder
{
    public const int MaxRows = 5000;

    public static RatingTable Build(RatingTableRequest request)
    {
        var messages = new List<CalcMessage>();
        if (!(request.Increment > 0) || !double.IsFinite(request.Increment))
            messages.Add(CalcMessage.Error("E-RT-001", "The increment must be greater than zero."));
        if (!(request.Start > 0) || !double.IsFinite(request.Start))
            messages.Add(CalcMessage.Error("E-RT-002", "The starting value must be greater than zero."));
        if (!(request.End >= request.Start) || !double.IsFinite(request.End))
            messages.Add(CalcMessage.Error("E-RT-003", "The ending value must be greater than or equal to the starting value."));
        if (messages.Count == 0 && (request.End - request.Start) / request.Increment + 1 > MaxRows)
            messages.Add(CalcMessage.Error("E-RT-004", $"The table would exceed {MaxRows} rows. Increase the increment."));
        if (messages.Count > 0) return new RatingTable(request, [], messages);

        var rows = new List<RatingTableRow>();
        int count = (int)Math.Floor((request.End - request.Start) / request.Increment + 1e-9) + 1;
        var values = Enumerable.Range(0, count).Select(i => request.Start + i * request.Increment).ToList();
        if (request.End - values[^1] > 1e-9 * Math.Max(1, request.End)) values.Add(request.End);

        foreach (double v in values)
        {
            var input = request.Mode == RatingTableMode.DischargeFromDepth
                ? request.Base with { SolveFor = SolveFor.Discharge, Depth = v }
                : request.Base with { SolveFor = SolveFor.NormalDepth, Discharge = v };
            var r = UniformFlowCalculator.Solve(input);
            if (!r.Succeeded)
            {
                foreach (var e in r.Errors) messages.Add(e with { Text = $"Row {v:G6}: {e.Text}" });
                continue;
            }
            rows.Add(new RatingTableRow(r.Normal.Depth, r.Discharge, r.Normal.Area, r.Normal.Velocity, r.Normal.TopWidth, r.Normal.Froude, r.Critical.Depth, r.Regime));
            foreach (var w in r.Warnings.Where(w => w.Code == MessageCodes.Overtopping))
                if (!messages.Any(m => m.Code == w.Code))
                    messages.Add(w with { Text = "One or more rows exceed the channel depth (overtopping)." });
        }
        return new RatingTable(request, rows, messages);
    }

    /// <summary>Tab-separated text suitable for pasting into Excel.</summary>
    public static string ToDelimited(RatingTable table, UnitConstants c, char separator = '\t')
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendJoin(separator, $"Depth ({c.Length})", $"Discharge ({c.Discharge})", $"Area ({c.Area})", $"Velocity ({c.Velocity})", $"Top width ({c.Length})", "Froude", $"Critical depth ({c.Length})", "Regime").AppendLine();
        foreach (var r in table.Rows)
            sb.AppendJoin(separator,
                r.Depth.ToString("F4", inv), r.Discharge.ToString("F4", inv), r.Area.ToString("F4", inv), r.Velocity.ToString("F4", inv),
                r.TopWidth.ToString("F4", inv), r.Froude.ToString("F4", inv), r.CriticalDepth.ToString("F4", inv), r.Regime.ToString()).AppendLine();
        return sb.ToString();
    }
}
