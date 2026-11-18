namespace MasterFlow.Domain;

/// <summary>One reference roughness entry: minimum, normal and maximum Manning's n.</summary>
public sealed record RoughnessEntry(string Category, string Description, double Minimum, double Normal, double? Maximum)
{
    public string Range => Maximum is { } max ? $"{Minimum:0.000}–{max:0.000}" : $"{Minimum:0.000}–";
}

/// <summary>
/// Reference values of Manning's n for picking a starting value. Values are the minimum / normal /
/// maximum ranges of V. T. Chow, <i>Open-Channel Hydraulics</i> (1959), Table 5-6, a selection of common
/// cases with descriptions written for MasterFlow. They are a guide only: the engineer selects n for
/// the actual site, condition and season and remains responsible for it.
/// </summary>
public static class ManningRoughnessTable
{
    public const string Source = "Chow (1959), Open-Channel Hydraulics, Table 5-6 (selection; descriptions paraphrased).";

    public static IReadOnlyList<RoughnessEntry> Entries { get; } =
    [
        new("Closed conduit, part full", "Concrete culvert, straight, no debris", 0.010, 0.011, 0.013),
        new("Closed conduit, part full", "Concrete culvert with bends and some debris", 0.011, 0.013, 0.014),
        new("Closed conduit, part full", "Concrete sewer with manholes and inlets, straight", 0.013, 0.015, 0.017),
        new("Closed conduit, part full", "Concrete pipe, unfinished, steel form", 0.012, 0.013, 0.014),
        new("Closed conduit, part full", "Concrete pipe, unfinished, smooth wood form", 0.012, 0.014, 0.016),
        new("Closed conduit, part full", "Vitrified clay sewer", 0.011, 0.014, 0.017),
        new("Closed conduit, part full", "Cast iron, coated", 0.010, 0.013, 0.014),
        new("Closed conduit, part full", "Corrugated metal subdrain", 0.017, 0.019, 0.021),
        new("Closed conduit, part full", "Corrugated metal storm drain", 0.021, 0.024, 0.030),
        new("Closed conduit, part full", "Brickwork, glazed", 0.011, 0.013, 0.015),
        new("Closed conduit, part full", "Brickwork in cement mortar", 0.012, 0.015, 0.017),

        new("Lined channel", "Concrete, trowel finish", 0.011, 0.013, 0.015),
        new("Lined channel", "Concrete, float finish", 0.013, 0.015, 0.016),
        new("Lined channel", "Concrete, finished, gravel on bottom", 0.015, 0.017, 0.020),
        new("Lined channel", "Concrete, unfinished", 0.014, 0.017, 0.020),
        new("Lined channel", "Gunite, good section", 0.016, 0.019, 0.023),
        new("Lined channel", "Gunite, wavy section", 0.018, 0.022, 0.025),
        new("Lined channel", "Asphalt, smooth", 0.013, 0.013, null),
        new("Lined channel", "Asphalt, rough", 0.016, 0.016, null),
        new("Lined channel", "Cemented rubble masonry", 0.017, 0.025, 0.030),
        new("Lined channel", "Dry rubble or riprap", 0.023, 0.032, 0.035),

        new("Excavated or dredged", "Earth, straight and uniform, clean, recently completed", 0.016, 0.018, 0.020),
        new("Excavated or dredged", "Earth, straight and uniform, clean, after weathering", 0.018, 0.022, 0.025),
        new("Excavated or dredged", "Earth, straight and uniform, short grass, few weeds", 0.022, 0.027, 0.033),
        new("Excavated or dredged", "Earth, winding and sluggish, no vegetation", 0.023, 0.025, 0.030),
        new("Excavated or dredged", "Earth, winding and sluggish, grass and some weeds", 0.025, 0.030, 0.033),
        new("Excavated or dredged", "Earth, winding, dense weeds or aquatic plants in deep channels", 0.030, 0.035, 0.040),
        new("Excavated or dredged", "Rock cut, smooth and uniform", 0.025, 0.035, 0.040),
        new("Excavated or dredged", "Rock cut, jagged and irregular", 0.035, 0.040, 0.050),
        new("Excavated or dredged", "Unmaintained, dense weeds as high as flow depth", 0.050, 0.080, 0.120),
        new("Excavated or dredged", "Unmaintained, clean bottom, brush on sides", 0.040, 0.050, 0.080),

        new("Natural stream (top width < 100 ft)", "Clean, straight, full stage, no riffles or deep pools", 0.025, 0.030, 0.033),
        new("Natural stream (top width < 100 ft)", "As above, more stones and weeds", 0.030, 0.035, 0.040),
        new("Natural stream (top width < 100 ft)", "Clean, winding, some pools and shoals", 0.033, 0.040, 0.045),
        new("Natural stream (top width < 100 ft)", "Sluggish reaches, weedy, deep pools", 0.050, 0.070, 0.080),
        new("Natural stream (top width < 100 ft)", "Very weedy reaches, deep pools or heavy brush and timber", 0.075, 0.100, 0.150),
        new("Natural stream (top width < 100 ft)", "Mountain stream, gravel, cobbles and few boulders", 0.030, 0.040, 0.050),
        new("Natural stream (top width < 100 ft)", "Mountain stream, cobbles with large boulders", 0.040, 0.050, 0.070),

        new("Floodplain", "Pasture, no brush, short grass", 0.025, 0.030, 0.035),
        new("Floodplain", "Pasture, no brush, high grass", 0.030, 0.035, 0.050),
        new("Floodplain", "Cultivated, no crop", 0.020, 0.030, 0.040),
        new("Floodplain", "Cultivated, mature row crops", 0.025, 0.035, 0.045),
        new("Floodplain", "Cultivated, mature field crops", 0.030, 0.040, 0.050),
        new("Floodplain", "Scattered brush, heavy weeds", 0.035, 0.050, 0.070),
        new("Floodplain", "Light brush and trees, in summer", 0.040, 0.060, 0.080),
        new("Floodplain", "Medium to dense brush, in summer", 0.070, 0.100, 0.160),
        new("Floodplain", "Cleared land with tree stumps, no sprouts", 0.030, 0.040, 0.050),
        new("Floodplain", "Heavy stand of timber, few down trees, flow below branches", 0.080, 0.100, 0.120),
    ];
}
