using System.Text.Json.Serialization;

namespace MasterFlow.Domain;

/// <summary>
/// One surveyed point of an irregular cross-section. <see cref="ManningN"/>, when given, applies to the
/// segment from this point to the next one (the last point's value is unused). When it is null the
/// calculation's Manning's n is used for that segment.
/// </summary>
public sealed record StationPoint(double Station, double Elevation, double? ManningN = null, string? Description = null);

/// <summary>How a section with more than one roughness value is combined.</summary>
public enum CompositeRoughnessMethod
{
    /// <summary>
    /// Conveyance subdivision: the wetted area is split by vertical lines where n changes; each subarea's
    /// conveyance (k/n) A R^(2/3) is computed separately and summed (HEC-RAS approach).
    /// </summary>
    Conveyance,

    /// <summary>
    /// Equal velocity (Horton 1933, Einstein 1934): one composite n = [Σ Pᵢ nᵢ^1.5 / P]^(2/3) applied to the
    /// whole wetted area.
    /// </summary>
    EqualVelocity,

    /// <summary>
    /// Per-segment conveyance (as Flow Calc does): every segment between two consecutive points is its own
    /// subarea, bounded by vertical lines at the points, whether or not n changes; the subarea conveyances
    /// are summed. Gives more conveyance than <see cref="Conveyance"/> in wide, shallow overbanks, so lower depths.
    /// </summary>
    PerSegment,
}

/// <summary>
/// Cross-section defined by station/elevation points, left to right looking downstream. Stations must
/// not decrease (equal consecutive stations form a vertical wall; overhangs are not supported).
/// Depth is measured from the lowest point. If the water surface rises above an end point, a vertical
/// wall is assumed at that end and a warning is reported.
/// </summary>
public sealed record IrregularSection(IReadOnlyList<StationPoint> Points, CompositeRoughnessMethod RoughnessMethod = CompositeRoughnessMethod.Conveyance)
    : ChannelSection
{
    [JsonIgnore] public override SectionShape Shape => SectionShape.Irregular;

    [JsonIgnore] public double MinElevation => Points.Count == 0 ? double.NaN : Points.Min(p => p.Elevation);

    /// <summary>True when any point carries its own n.</summary>
    [JsonIgnore] public bool HasPointRoughness => Points.Any(p => p.ManningN is not null);

    // Records compare lists by reference; compare the point values instead so that equal sections are equal.
    public bool Equals(IrregularSection? other) =>
        other is not null && base.Equals(other) && RoughnessMethod == other.RoughnessMethod && Points.SequenceEqual(other.Points);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), RoughnessMethod, Points.Count);
}
