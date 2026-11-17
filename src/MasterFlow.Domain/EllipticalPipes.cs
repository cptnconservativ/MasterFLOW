using System.Text.Json.Serialization;

namespace MasterFlow.Domain;

/// <summary>
/// Elliptical conduit, horizontal (span &gt; rise) or vertical (rise &gt; span), flowing partly full.
/// When <see cref="EndRadius"/> is given, the shape is the four-centred arc section of ASTM C507
/// (Fig. 5): arcs of radius R1 at the ends of the major axis, tangent arcs of radius R2 (derived from
/// tangency) at the ends of the minor axis. When it is null, the shape is a true ellipse.
/// Dimensions are inside dimensions in the base length unit.
/// </summary>
public sealed record EllipticalSection(double Rise, double Span, double? EndRadius = null, string? Designation = null) : ChannelSection
{
    [JsonIgnore] public override SectionShape Shape => SectionShape.Elliptical;
    [JsonIgnore] public bool IsHorizontal => Span >= Rise;
    [JsonIgnore] public bool IsStandardShape => EndRadius is not null;
}

/// <summary>One standard size from ASTM C507-22 Fig. 5 (inches and square feet).</summary>
public sealed record EllipticalPipeSize(int EquivalentRound, double RiseIn, double SpanIn, double R1In, double R2In, double TabulatedAreaFt2, bool VerticalAvailable)
{
    /// <summary>Nominal designation as in C507 Tables 1 and 2 (rounded rise x span).</summary>
    public string Label(bool vertical) => vertical
        ? $"VE {EquivalentRound} in (equiv. round) — {RiseIn:0.##} x {SpanIn:0.##} in"
        : $"HE {EquivalentRound} in (equiv. round) — {RiseIn:0.##} x {SpanIn:0.##} in";
}

/// <summary>
/// Standard reinforced-concrete elliptical pipe shapes, ASTM C507-22, Fig. 5 ("Cross-Sectional Shape
/// of Elliptical Pipe"): rise, span, R1 and R2 are the fixed values; full-flow area is as tabulated.
/// The same shapes are used for horizontal (HE, Table 1) and vertical (VE, Table 2, sizes 36–144 in)
/// installation. Values must be checked against the edition used on a project.
/// </summary>
public static class EllipticalPipeCatalog
{
    public static IReadOnlyList<EllipticalPipeSize> Sizes { get; } =
    [
        new(15, 12, 19, 5.25, 16.25, 1.29, false),
        new(18, 14.25, 22.75, 6, 20, 1.83, false),
        new(24, 19.25, 30.25, 8.25, 26.25, 3.28, false),
        new(27, 21.5, 34, 9.25, 29.25, 4.12, false),
        new(30, 24, 37.75, 10.25, 32.75, 5.10, false),
        new(33, 26.75, 42, 11.5, 36.25, 6.33, false),
        new(36, 28.75, 45.5, 12.25, 39.25, 7.36, true),
        new(39, 31.5, 49.5, 13.5, 42.75, 8.78, true),
        new(42, 34, 53.25, 14.5, 46, 10.2, true),
        new(48, 38.25, 60, 16.5, 51.5, 12.9, true),
        new(54, 43.5, 68, 18.75, 58.5, 16.7, true),
        new(60, 48.25, 75.5, 20.75, 65, 20.5, true),
        new(66, 53, 83, 22.75, 71.5, 24.8, true),
        new(72, 57.75, 90.5, 24.75, 78, 29.4, true),
        new(78, 62.75, 98, 27, 84.5, 34.6, true),
        new(84, 67.5, 105.5, 29, 90.75, 40.1, true),
        new(90, 72.5, 113, 31, 97.25, 46.1, true),
        new(96, 77.25, 120.5, 33.25, 103.75, 52.4, true),
        new(102, 82, 128, 35.25, 110, 59.1, true),
        new(108, 87, 135.5, 37.5, 116.25, 66.4, true),
        new(114, 91.75, 143, 39.5, 122.75, 73.9, true),
        new(120, 96.75, 150.75, 41.5, 129.25, 82.1, true),
        new(132, 106.5, 165.5, 45.75, 142, 99.2, true),
        new(144, 116, 180.75, 50, 154.75, 118, true),
    ];

    /// <summary>Creates the section in feet (U.S.) or metres (SI) for a standard size.</summary>
    public static EllipticalSection Create(EllipticalPipeSize s, bool vertical, UnitSystem units)
    {
        double k = units == UnitSystem.USCustomary ? 1.0 / 12.0 : 0.0254;
        double rise = (vertical ? s.SpanIn : s.RiseIn) * k, span = (vertical ? s.RiseIn : s.SpanIn) * k;
        return new EllipticalSection(rise, span, s.R1In * k, $"ASTM C507 {(vertical ? "VE" : "HE")} {s.EquivalentRound}-in equivalent round");
    }
}
