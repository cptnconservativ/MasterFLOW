using MasterFlow.Domain;

namespace MasterFlow.Application;

/// <summary>
/// Editing operations on station/elevation points. Each returns a new list; the input is not changed.
/// A point's n applies to the segment from that point to the next, and every operation keeps each n on
/// the same piece of ground.
/// </summary>
public static class SectionPoints
{
    public static IReadOnlyList<StationPoint> ShiftStations(IReadOnlyList<StationPoint> pts, double dx) =>
        pts.Select(p => p with { Station = p.Station + dx }).ToList();

    public static IReadOnlyList<StationPoint> ShiftElevations(IReadOnlyList<StationPoint> pts, double dz) =>
        pts.Select(p => p with { Elevation = p.Elevation + dz }).ToList();

    /// <summary>Moves stations so the first point is at station 0.</summary>
    public static IReadOnlyList<StationPoint> ZeroStations(IReadOnlyList<StationPoint> pts) =>
        pts.Count == 0 ? pts : ShiftStations(pts, -pts[0].Station);

    /// <summary>Moves elevations so the lowest point is at elevation 0 (depths then read as elevations).</summary>
    public static IReadOnlyList<StationPoint> ZeroElevations(IReadOnlyList<StationPoint> pts) =>
        pts.Count == 0 ? pts : ShiftElevations(pts, -pts.Min(p => p.Elevation));

    /// <summary>
    /// Sorts by station (stable, so vertical walls keep their order). Segment n values are re-assigned by
    /// position: after a sort, the n of each segment is the n of the original segment covering its midpoint.
    /// </summary>
    public static IReadOnlyList<StationPoint> SortByStation(IReadOnlyList<StationPoint> pts)
    {
        var sorted = pts.Select((p, i) => (p, i)).OrderBy(t => t.p.Station).ThenBy(t => t.i).Select(t => t.p).ToList();
        if (sorted.SequenceEqual(pts)) return pts;
        double? NAt(double x)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double a = Math.Min(pts[i].Station, pts[i + 1].Station), b = Math.Max(pts[i].Station, pts[i + 1].Station);
                if (x >= a && x <= b) return pts[i].ManningN;
            }
            return null;
        }
        return sorted.Select((p, i) => i + 1 < sorted.Count ? p with { ManningN = NAt(0.5 * (p.Station + sorted[i + 1].Station)) } : p with { ManningN = null }).ToList();
    }

    /// <summary>
    /// Mirrors the section (looking the other way along the channel): stations become (first + last − x),
    /// order is reversed and each segment keeps its own n.
    /// </summary>
    public static IReadOnlyList<StationPoint> Reverse(IReadOnlyList<StationPoint> pts)
    {
        if (pts.Count < 2) return pts;
        double s = pts[0].Station + pts[^1].Station;
        int n = pts.Count;
        var list = new List<StationPoint>(n);
        for (int j = 0; j < n; j++)
        {
            var p = pts[n - 1 - j];
            // New segment j (points j → j+1) is old segment n-2-j.
            double? segN = j < n - 1 ? pts[n - 2 - j].ManningN : null;
            list.Add(p with { Station = s - p.Station, ManningN = segN });
        }
        return list;
    }

    /// <summary>
    /// Station/elevation points of a regular open shape (invert at elevation 0, first point at station 0),
    /// up to <paramref name="height"/>. Closed conduits cannot be converted. The points reproduce the
    /// shape exactly below <paramref name="height"/>; above it an irregular section gets vertical walls.
    /// </summary>
    public static IReadOnlyList<StationPoint> FromRegular(ChannelSection section, double height)
    {
        if (!(height > 0) || !double.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(height), "Height must be a positive number.");
        return section switch
        {
            RectangularSection r => [new(0, height, null, "Left top"), new(0, 0, null, "Left toe"), new(r.BottomWidth, 0, null, "Right toe"), new(r.BottomWidth, height, null, "Right top")],
            TrapezoidalSection t => [new(0, height, null, "Left top"), new(t.LeftSideSlope * height, 0, null, "Left toe"),
                                     new(t.LeftSideSlope * height + t.BottomWidth, 0, null, "Right toe"),
                                     new(t.LeftSideSlope * height + t.BottomWidth + t.RightSideSlope * height, height, null, "Right top")],
            TriangularSection v => [new(0, height, null, "Left top"), new(v.LeftSideSlope * height, 0, null, "Invert"), new((v.LeftSideSlope + v.RightSideSlope) * height, height, null, "Right top")],
            _ => throw new NotSupportedException($"A {section.ShapeName.ToLowerInvariant()} cannot be converted to station/elevation points."),
        };
    }

    public static bool CanConvertToIrregular(ChannelSection s) => s is RectangularSection or TrapezoidalSection or TriangularSection;
}
