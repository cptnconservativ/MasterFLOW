namespace MasterFlow.Hydraulics;

public enum MessageSeverity
{
    /// <summary>Engineering information worth reporting (for example, flow regime notes).</summary>
    Info,
    /// <summary>A valid result was produced but the engineer should review an assumption or limit.</summary>
    Warning,
    /// <summary>No result: inputs are invalid or the solution failed. Numeric outputs must not be shown.</summary>
    Error,
}

/// <summary>A structured validation or calculation message. <see cref="Code"/> is stable and testable.</summary>
public sealed record CalcMessage(MessageSeverity Severity, string Code, string Text, string? Field = null)
{
    public static CalcMessage Error(string code, string text, string? field = null) => new(MessageSeverity.Error, code, text, field);
    public static CalcMessage Warning(string code, string text, string? field = null) => new(MessageSeverity.Warning, code, text, field);
    public static CalcMessage Info(string code, string text, string? field = null) => new(MessageSeverity.Info, code, text, field);
}

/// <summary>Stable message codes.</summary>
public static class MessageCodes
{
    public const string RoughnessNotPositive = "E-N-001";
    public const string SlopeNotPositive = "E-S-001";
    public const string DischargeNotPositive = "E-Q-001";
    public const string DepthNotPositive = "E-Y-001";
    public const string GeometryInvalid = "E-G-001";
    public const string NonFinite = "E-X-001";
    public const string NoConvergence = "E-X-002";
    public const string ExceedsConduitCapacity = "E-P-001";
    public const string DepthExceedsRise = "E-P-002";
    public const string TwoNormalDepths = "W-P-001";
    public const string NearCrown = "W-P-002";
    public const string FlowingFull = "I-P-001";
    public const string RoughnessSolveNeedsSingleN = "E-N-002";
    public const string MultipleNormalDepths = "W-I-001";
    public const string SeparateWettedAreas = "W-I-002";
    public const string WallExtended = "W-I-003";
    public const string MultipleCriticalDepths = "W-I-004";
    public const string OutOfBank = "W-I-005";

    public const string RoughnessUnusual = "W-N-001";
    public const string SlopeSteep = "W-S-001";
    public const string SlopeVeryMild = "W-S-002";
    public const string Overtopping = "W-G-001";
    public const string NearCritical = "W-F-001";
    public const string Supercritical = "I-F-001";
    public const string Subcritical = "I-F-002";
}
