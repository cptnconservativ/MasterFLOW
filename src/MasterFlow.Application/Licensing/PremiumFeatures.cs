using MasterFlow.Application.Tools;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Licensing;

/// <summary>A premium feature: its id (used in license keys), its name, and the design tool ids it unlocks.</summary>
public sealed record PremiumFeature(string Id, string Name, IReadOnlyList<string> ToolIds);

/// <summary>
/// The premium catalog. A design tool is premium when its id is listed here; the tools themselves know nothing about
/// licensing, so a tool can move between Free and Premium by editing this list. Ids of tools not built yet are listed
/// so a key issued today keeps working when they arrive.
/// </summary>
public static class PremiumFeatures
{
    /// <summary>Feature ids for premium work outside the Design tools window (Storm networks window buttons).</summary>
    public const string PipeSizing = "pipe-sizing", Civil3DExport = "civil3d-export";

    public static readonly IReadOnlyList<PremiumFeature> All =
    [
        new("hydrograph-routing", "Hydrograph network routing (NRCS unit hydrograph, TR-55, pipe and pond routing)", ["hydrograph-routing"]),
        new("drainage-report", "Automated drainage report builder", ["drainage-report"]),
        new("network-optimizer", "Storm network optimizer (least cost or least depth)", ["network-optimizer"]),
        new("water-quality", "Water quality and LID sizing (WQCV, sand filter, bioretention, infiltration)", ["water-quality"]),
        new("cost-estimate", "Quantity takeoff and cost estimate with bid tab", ["cost-estimate"]),
        new("batch-scenarios", "Batch scenario manager", ["batch-scenarios"]),
        new("inlet-routing", "Inlet capacity and bypass routing across a network", ["inlet-network"]),
        new("atlas14-idf", "NOAA Atlas 14 rainfall lookup (IDF tables, comparison with IDCM Table 4-2)", ["atlas14-idf"]),
        new("outlet-designer", "Multi-stage detention outlet designer (WQCV plate, 2-, 10-, 100-yr stages)", ["outlet-designer"]),
        new("plan-profile-dxf", "Plan and profile DXF sheets with HGL and EGL", ["plan-profile-dxf"]),
        new("plan-review", "Storm network plan review (Rapid City IDCM §4.6)", ["network-plan-review"]),
        new("utility-conflicts", "Utility conflict finder (3D clashes and separation rules)", ["utility-conflicts"]),
        new("field-to-model", "Field-to-model: as-built survey and photos update the network, HGL certification", ["field-to-model"]),
        new("inverse-design", "Inverse design (largest or smallest input that still meets the criteria)", ["inverse-design"]),
        new("submittal-precheck", "Plan-review pre-checker: drainage report and plans against Rapid City criteria", ["submittal-precheck"]),
        new("redesign-diff", "What changed? Redesign diff: re-runs every calculation and lists new failures and margin changes", ["redesign-diff"]),
        new("site-feasibility", "Site feasibility: public data, lot yield, drainage concept, cost range and go/no-go", ["site-feasibility"]),
        new(PipeSizing, "Automatic storm network pipe sizing (Storm networks → Size pipes)", []),
        new(Civil3DExport, "Civil 3D export: LandXML pipe network and HGL/EGL write-back (Storm networks → LandXML…, Civil 3D results…)", []),
    ];

    /// <summary>The premium feature that gates <paramref name="toolId"/>, or null for a free tool.</summary>
    public static PremiumFeature? ForTool(string toolId) => All.FirstOrDefault(f => f.ToolIds.Contains(toolId, StringComparer.OrdinalIgnoreCase));

    /// <summary>The premium feature with id <paramref name="featureId"/>.</summary>
    public static PremiumFeature Get(string featureId) => All.First(f => string.Equals(f.Id, featureId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Why <paramref name="what"/> is locked under <paramref name="license"/>: the feature it needs and how to unlock it.</summary>
    public static string LockedMessage(string what, PremiumFeature f, LicenseState license) =>
        $"{what} is a MasterFlow Premium feature ({f.Name}). " +
        (license.Status == LicenseStatus.Valid
            ? "Your license does not include this feature."
            : license.Status == LicenseStatus.None ? "Enter a license key under License in the Design tools window to unlock it." : license.Message);

    public static bool IsPremium(IDesignTool tool) => ForTool(tool.Id) is not null;

    /// <summary>Tool name for lists: premium tools carry a "★ Premium" tag.</summary>
    public static string Label(IDesignTool tool) => IsPremium(tool) ? tool.Name + "  ★ Premium" : tool.Name;

    /// <summary>
    /// Runs <paramref name="tool"/> when the license allows it; otherwise returns a failed result that says which
    /// feature is needed. This is the only gate: every caller that runs a tool for the user goes through it.
    /// </summary>
    public static ToolResult Run(IDesignTool tool, string input, DesignToolContext context, LicenseState license)
    {
        if (ForTool(tool.Id) is { } f && !license.Allows(f.Id))
            return ToolResult.Failed(tool.Name, CalcMessage.Error("E-LIC-001", LockedMessage(tool.Name, f, license)));
        return tool.Run(input, context);
    }
}
