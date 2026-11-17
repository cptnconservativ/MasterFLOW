using System.Text.Json.Serialization;

namespace MasterFlow.Domain;

/// <summary>
/// A prismatic cross-section definition. Dimensions are in the base length unit of the calculation's
/// <see cref="UnitSystem"/>. Side slopes are expressed as horizontal : vertical (H:V), i.e. z in
/// "z H : 1 V".
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "shape")]
[JsonDerivedType(typeof(RectangularSection), "rectangular")]
[JsonDerivedType(typeof(TrapezoidalSection), "trapezoidal")]
[JsonDerivedType(typeof(TriangularSection), "triangular")]
[JsonDerivedType(typeof(CircularSection), "circular")]
public abstract record ChannelSection
{
    /// <summary>
    /// Optional bank-full / structural depth of the section. It does not limit the calculation: the
    /// engine extends the section geometry above it and reports an overtopping warning instead.
    /// </summary>
    public double? ChannelDepth { get; init; }

    [JsonIgnore] public abstract SectionShape Shape { get; }

    /// <summary>Human-readable shape name.</summary>
    [JsonIgnore] public string ShapeName => Shape switch
    {
        SectionShape.Rectangular => "Rectangular",
        SectionShape.Trapezoidal => "Trapezoidal",
        SectionShape.Triangular => "Triangular",
        SectionShape.Circular => "Circular pipe",
        _ => Shape.ToString(),
    };
}

public enum SectionShape
{
    Rectangular,
    Trapezoidal,
    Triangular,
    Circular,
}

/// <summary>Rectangular open channel of bottom width b.</summary>
public sealed record RectangularSection(double BottomWidth) : ChannelSection
{
    [JsonIgnore] public override SectionShape Shape => SectionShape.Rectangular;
}

/// <summary>Trapezoidal open channel. Left and right side slopes may differ.</summary>
public sealed record TrapezoidalSection(double BottomWidth, double LeftSideSlope, double RightSideSlope) : ChannelSection
{
    [JsonIgnore] public override SectionShape Shape => SectionShape.Trapezoidal;
}

/// <summary>Triangular (V) open channel. Left and right side slopes may differ.</summary>
public sealed record TriangularSection(double LeftSideSlope, double RightSideSlope) : ChannelSection
{
    [JsonIgnore] public override SectionShape Shape => SectionShape.Triangular;
}

/// <summary>
/// Circular conduit of inside diameter D (base length unit), flowing as an open channel (free surface,
/// atmospheric pressure at the crown). Pressurised (surcharged) flow is not modelled.
/// <see cref="ChannelSection.ChannelDepth"/> is not used for conduits.
/// </summary>
public sealed record CircularSection(double Diameter) : ChannelSection
{
    [JsonIgnore] public override SectionShape Shape => SectionShape.Circular;
}
