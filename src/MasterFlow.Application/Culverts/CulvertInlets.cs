namespace MasterFlow.Application.Culverts;

/// <summary>Barrel shapes the culvert analysis supports.</summary>
public enum BarrelShape { Circular, Box, Elliptical }

/// <summary>
/// HDS-5 inlet control coefficients for one inlet configuration (FHWA HDS-5, 3rd ed. 2012, Appendix A, Table A.1,
/// and the 2005 charts it carries over). Form 1 or 2 selects the unsubmerged equation; c and Y are for the
/// submerged equation; Ks is the slope correction (−0.5, or +0.7 for mitered inlets). Ke is the outlet-control
/// entrance loss coefficient for the same inlet (HDS-5 Table C.2), used as the default when Ke is not given.
/// </summary>
public sealed record InletCoefficients(string Id, BarrelShape Shape, string Description, int Form, double K, double M, double C, double Y, double Ks, double Ke, string Source);

/// <summary>
/// The inlet configurations built into MasterFlow. Values are as printed in HDS-5 (2012) Table A.1 / A.2. They
/// were transcribed from secondary copies of that table because the FHWA site could not be reached when this was
/// written; a project should check the rows it uses against its copy of HDS-5 (any inlet can also be entered as
/// "custom" with its own coefficients).
/// </summary>
public static class CulvertInlets
{
    public static IReadOnlyList<InletCoefficients> All { get; } =
    [
        // Circular concrete (HDS-5 chart 1)
        new("circ-conc-square-headwall", BarrelShape.Circular, "Concrete pipe, square edge with headwall", 1, 0.0098, 2.0, 0.0398, 0.67, -0.5, 0.5, "HDS-5 Table A.1, chart 1 scale 1"),
        new("circ-conc-groove-headwall", BarrelShape.Circular, "Concrete pipe, groove end (socket) with headwall", 1, 0.0018, 2.0, 0.0292, 0.74, -0.5, 0.2, "HDS-5 Table A.1, chart 1 scale 2"),
        new("circ-conc-groove-projecting", BarrelShape.Circular, "Concrete pipe, groove end (socket) projecting", 1, 0.0045, 2.0, 0.0317, 0.69, -0.5, 0.2, "HDS-5 Table A.1, chart 1 scale 3"),
        // Circular CMP (chart 2)
        new("circ-cmp-headwall", BarrelShape.Circular, "Corrugated metal pipe, headwall", 1, 0.0078, 2.0, 0.0379, 0.69, -0.5, 0.5, "HDS-5 Table A.1, chart 2 scale 1"),
        new("circ-cmp-mitered", BarrelShape.Circular, "Corrugated metal pipe, mitered to conform to slope", 1, 0.0210, 1.33, 0.0463, 0.75, 0.7, 0.7, "HDS-5 Table A.1, chart 2 scale 2"),
        new("circ-cmp-projecting", BarrelShape.Circular, "Corrugated metal pipe, projecting", 1, 0.0340, 1.50, 0.0553, 0.54, -0.5, 0.9, "HDS-5 Table A.1, chart 2 scale 3"),
        // Circular, beveled ring (chart 3)
        new("circ-bevel-45", BarrelShape.Circular, "Circular, beveled ring, 45° bevels", 1, 0.0018, 2.50, 0.0300, 0.74, -0.5, 0.2, "HDS-5 Table A.1, chart 3 scale A"),
        new("circ-bevel-33.7", BarrelShape.Circular, "Circular, beveled ring, 33.7° bevels", 1, 0.0018, 2.50, 0.0243, 0.83, -0.5, 0.2, "HDS-5 Table A.1, chart 3 scale B"),
        // Concrete box (charts 8–13)
        new("box-wingwall-30-75", BarrelShape.Box, "Concrete box, 30° to 75° wingwall flares, square crown edge", 1, 0.026, 1.0, 0.0347, 0.81, -0.5, 0.4, "HDS-5 Table A.1, chart 8 scale 1"),
        new("box-wingwall-90-15", BarrelShape.Box, "Concrete box, 90° and 15° wingwall flares", 1, 0.061, 0.75, 0.0400, 0.80, -0.5, 0.5, "HDS-5 Table A.1, chart 8 scale 2"),
        new("box-wingwall-0", BarrelShape.Box, "Concrete box, 0° wingwall flares (wingwalls parallel, extensions of sides)", 1, 0.061, 0.75, 0.0423, 0.82, -0.5, 0.7, "HDS-5 Table A.1, chart 8 scale 3"),
        new("box-flare-45-top-bevel", BarrelShape.Box, "Concrete box, 45° wingwall flare, top edge bevel d = 0.043D", 2, 0.510, 0.667, 0.0309, 0.80, -0.5, 0.2, "HDS-5 Table A.1, chart 9 scale 1"),
        new("box-flare-18-33.7-top-bevel", BarrelShape.Box, "Concrete box, 18° to 33.7° wingwall flare, top edge bevel d = 0.083D", 2, 0.486, 0.667, 0.0249, 0.83, -0.5, 0.2, "HDS-5 Table A.1, chart 9 scale 2"),
        new("box-headwall-chamfer", BarrelShape.Box, "Concrete box, 90° headwall with 3/4 in. chamfers", 2, 0.515, 0.667, 0.0375, 0.79, -0.5, 0.5, "HDS-5 Table A.1, chart 10 scale 1"),
        new("box-headwall-bevel-45", BarrelShape.Box, "Concrete box, 90° headwall with 45° bevels", 2, 0.495, 0.667, 0.0314, 0.82, -0.5, 0.2, "HDS-5 Table A.1, chart 10 scale 2"),
        new("box-headwall-bevel-33.7", BarrelShape.Box, "Concrete box, 90° headwall with 33.7° bevels", 2, 0.486, 0.667, 0.0252, 0.865, -0.5, 0.2, "HDS-5 Table A.1, chart 10 scale 3"),
        new("box-skew-45-chamfer", BarrelShape.Box, "Concrete box, 45° skewed headwall, 3/4 in. chamfers", 2, 0.545, 0.667, 0.04505, 0.73, -0.5, 0.5, "HDS-5 Table A.1, chart 11 scale 1"),
        new("box-skew-30-chamfer", BarrelShape.Box, "Concrete box, 30° skewed headwall, 3/4 in. chamfers", 2, 0.533, 0.667, 0.0425, 0.705, -0.5, 0.5, "HDS-5 Table A.1, chart 11 scale 2"),
        new("box-skew-15-chamfer", BarrelShape.Box, "Concrete box, 15° skewed headwall, 3/4 in. chamfers", 2, 0.522, 0.667, 0.0402, 0.68, -0.5, 0.5, "HDS-5 Table A.1, chart 11 scale 3"),
        new("box-skew-10-45-bevel", BarrelShape.Box, "Concrete box, 10° to 45° skewed headwall, 45° bevels", 2, 0.498, 0.667, 0.0327, 0.75, -0.5, 0.2, "HDS-5 Table A.1, chart 11 scale 4"),
        new("box-nonoffset-45-chamfer", BarrelShape.Box, "Concrete box, 45° non-offset wingwall flares, 3/4 in. chamfers", 2, 0.497, 0.667, 0.0339, 0.803, -0.5, 0.4, "HDS-5 Table A.1, chart 12 scale 1"),
        new("box-nonoffset-18.4-chamfer", BarrelShape.Box, "Concrete box, 18.4° non-offset wingwall flares, 3/4 in. chamfers", 2, 0.493, 0.667, 0.0361, 0.806, -0.5, 0.5, "HDS-5 Table A.1, chart 12 scale 2"),
        new("box-nonoffset-18.4-skew30", BarrelShape.Box, "Concrete box, 18.4° non-offset wingwall flares, 30° skewed barrel, 3/4 in. chamfers", 2, 0.495, 0.667, 0.0386, 0.71, -0.5, 0.5, "HDS-5 Table A.1, chart 12 scale 3"),
        new("box-offset-45-top-bevel", BarrelShape.Box, "Concrete box, 45° offset wingwall flares, top bevel", 2, 0.497, 0.667, 0.0302, 0.835, -0.5, 0.2, "HDS-5 Table A.1, chart 13 scale 1"),
        new("box-offset-33.7-top-bevel", BarrelShape.Box, "Concrete box, 33.7° offset wingwall flares, top bevel", 2, 0.495, 0.667, 0.0252, 0.881, -0.5, 0.2, "HDS-5 Table A.1, chart 13 scale 2"),
        new("box-offset-18.4-top-bevel", BarrelShape.Box, "Concrete box, 18.4° offset wingwall flares, top bevel", 2, 0.493, 0.667, 0.0227, 0.887, -0.5, 0.2, "HDS-5 Table A.1, chart 13 scale 3"),
        // Elliptical concrete (charts 29–30, 2005 edition, carried in Table A.2)
        new("ellipse-conc-square-headwall", BarrelShape.Elliptical, "Concrete elliptical pipe, square edge with headwall", 1, 0.0100, 2.0, 0.0398, 0.67, -0.5, 0.5, "HDS-5 Table A.2 (2005 chart 29 scale 1)"),
        new("ellipse-conc-groove-headwall", BarrelShape.Elliptical, "Concrete elliptical pipe, groove end with headwall", 1, 0.0018, 2.5, 0.0292, 0.74, -0.5, 0.2, "HDS-5 Table A.2 (2005 chart 29 scale 2)"),
        new("ellipse-conc-groove-projecting", BarrelShape.Elliptical, "Concrete elliptical pipe, groove end projecting", 1, 0.0045, 2.0, 0.0317, 0.69, -0.5, 0.2, "HDS-5 Table A.2 (2005 chart 29 scale 3)"),
    ];

    public static InletCoefficients? Find(string? id) => All.FirstOrDefault(i => string.Equals(i.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Ku: 1.0 for U.S. customary units, 1.811 for SI (HDS-5 eqs. A.1–A.3).</summary>
    public static double Ku(bool si) => si ? 1.811 : 1.0;

    /// <summary>Upper limit of the unsubmerged equations and lower limit of the submerged equation, as Ku·Q/(A·D^0.5).</summary>
    public const double UnsubmergedLimit = 3.5, SubmergedLimit = 4.0;
}
