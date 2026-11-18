using MasterFlow.Domain;

namespace MasterFlow.Application;

/// <summary>A reusable starting point for a new calculation.</summary>
public sealed record CalculationTemplate(string Name, string Category, string Description, ChannelCalculation Calculation, bool BuiltIn = false);

/// <summary>
/// MasterFlow's own starting templates. Dimensions are generic illustrations, not any agency's
/// standard: the engineer edits them to the governing standard detail.
/// </summary>
public static class BuiltInTemplates
{
    private static StationPoint P(double x, double z, double? n = null, string? d = null) => new(x, z, n, d);

    private static CalculationTemplate T(string name, string cat, string desc, ChannelSection s, double n, double slope, double q) =>
        new(name, cat, desc, new ChannelCalculation
        {
            Title = name,
            Description = desc,
            Input = new UniformFlowInput { Section = s, SolveFor = SolveFor.NormalDepth, ManningN = n, Slope = slope, Discharge = q, Depth = 0.5 },
        }, BuiltIn: true);

    public static IReadOnlyList<CalculationTemplate> All { get; } =
    [
        T("Street half-section, curb and gutter", "Street",
          "Curb face 6 in, 2-ft concrete gutter at 1:12 (n 0.013), 13-ft pavement at 2 % to the crown (n 0.016). Generic dimensions; edit to the local standard.",
          new IrregularSection([P(0, 0.5, 0.013, "Top of curb"), P(0, 0, 0.013, "Flow line"), P(2, 0.1667, 0.016, "Gutter lip"), P(15, 0.4267, null, "Crown")]),
          0.016, 0.01, 2),
        T("Street, crown to curbs (two gutters)", "Street",
          "30-ft street, 6-in curbs, 2-ft gutters at 1:12, pavement at 2 % to a centre crown. Generic dimensions.",
          new IrregularSection([P(0, 0.5, 0.013, "Left top of curb"), P(0, 0, 0.013, "Left flow line"), P(2, 0.1667, 0.016), P(15, 0.4267, 0.016, "Crown"),
                                P(28, 0.1667, 0.013), P(30, 0, 0.013, "Right flow line"), P(30, 0.5, null, "Right top of curb")]),
          0.016, 0.01, 4),
        T("Roadside V-ditch, 4:1 front, 3:1 back", "Ditch",
          "Triangular roadside ditch, 4H:1V foreslope and 3H:1V backslope, 2 ft deep, grass lined.",
          new TriangularSection(4, 3) { ChannelDepth = 2 }, 0.030, 0.01, 10),
        T("Grass swale, 4-ft bottom, 4:1 sides", "Ditch",
          "Trapezoidal grassed swale, 4-ft bottom, 4H:1V sides, 2 ft deep.",
          new TrapezoidalSection(4, 4, 4) { ChannelDepth = 2 }, 0.035, 0.005, 25),
        T("Concrete-lined trapezoidal channel", "Channel",
          "10-ft bottom, 2H:1V sides, 5 ft deep, float-finished concrete.",
          new TrapezoidalSection(10, 2, 2) { ChannelDepth = 5 }, 0.015, 0.002, 300),
        T("Rectangular concrete flume", "Channel",
          "8-ft wide rectangular flume, 4 ft deep.",
          new RectangularSection(8) { ChannelDepth = 4 }, 0.013, 0.002, 120),
        T("18-in RCP storm drain", "Pipe", "Circular reinforced concrete pipe, 18 in.", new CircularSection(1.5), 0.013, 0.005, 5),
        T("24-in corrugated metal culvert", "Pipe", "Circular corrugated metal pipe, 24 in.", new CircularSection(2), 0.024, 0.01, 10),
        T("HE 24-in equivalent RCP (19¼ × 30¼)", "Pipe", "ASTM C507 horizontal elliptical pipe.",
          EllipticalPipeCatalog.Create(EllipticalPipeCatalog.Sizes.Single(s => s.EquivalentRound == 24), false, UnitSystem.USCustomary), 0.013, 0.005, 10),
        T("Natural channel with overbanks", "Natural",
          "Main channel n 0.035, overbanks n 0.06; replace the points with surveyed data.",
          new IrregularSection([P(0, 8, 0.06, "Left overbank"), P(40, 5, 0.06), P(50, 5, 0.035, "Left bank"), P(55, 0, 0.035), P(65, 0, 0.035),
                                P(70, 5, 0.06, "Right bank"), P(80, 5, 0.06), P(120, 8, null, "Right overbank")]),
          0.06, 0.001, 500),
    ];

    /// <summary>Copy of a template's calculation with a fresh id and timestamp, ready to add to a project.</summary>
    public static ChannelCalculation Instantiate(CalculationTemplate t) =>
        t.Calculation with { Id = Guid.NewGuid(), CreatedUtc = DateTimeOffset.UtcNow };
}
