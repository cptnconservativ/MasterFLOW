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
    public static readonly IReadOnlyList<PremiumFeature> All =
    [
        new("hydrograph-routing", "Hydrograph network routing (NRCS unit hydrograph, TR-55, pipe and pond routing)", ["hydrograph-routing"]),
        new("drainage-report", "Automated drainage report builder", ["drainage-report"]),
        new("network-optimizer", "Storm network optimizer (least cost or least depth)", ["network-optimizer"]),
        new("water-quality", "Water quality and LID sizing (WQCV, sand filter, bioretention, infiltration)", ["water-quality"]),
        new("cost-estimate", "Quantity takeoff and cost estimate with bid tab", ["cost-estimate"]),
        new("batch-scenarios", "Batch scenario manager", ["batch-scenarios"]),
    ];

    /// <summary>The premium feature that gates <paramref name="toolId"/>, or null for a free tool.</summary>
    public static PremiumFeature? ForTool(string toolId) => All.FirstOrDefault(f => f.ToolIds.Contains(toolId, StringComparer.OrdinalIgnoreCase));

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
            return ToolResult.Failed(tool.Name, CalcMessage.Error("E-LIC-001",
                $"{tool.Name} is a MasterFlow Premium tool ({f.Name}). " +
                (license.Status == LicenseStatus.Valid
                    ? "Your license does not include this feature."
                    : license.Status == LicenseStatus.None ? "Enter a license key under License in this window to unlock it." : license.Message)));
        return tool.Run(input, context);
    }
}
