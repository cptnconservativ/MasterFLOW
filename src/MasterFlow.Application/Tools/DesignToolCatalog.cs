namespace MasterFlow.Application.Tools;

/// <summary>
/// Groups the design tools by the job an engineer is doing, so the tool list reads like the user manual's chapters
/// instead of one long alphabetical list. A tool missing from the map is listed under "Other".
/// </summary>
public static class DesignToolCatalog
{
    public static readonly IReadOnlyList<string> CategoryOrder =
    [
        "Hydrology", "Streets and inlets", "Storm sewer design", "Detention and water quality",
        "Open channels and structures", "Quantities and cost", "Productivity and deliverables", "Land development and construction", "Sanitary sewers", "Other",
    ];

    private static readonly Dictionary<string, string> Map = new()
    {
        ["rational-rapid-city"] = "Hydrology", ["atlas14-idf"] = "Hydrology", ["tr55-hydrology"] = "Hydrology",
        ["regional-regression"] = "Hydrology",
        ["hydrograph-routing"] = "Hydrology", ["flood-frequency"] = "Hydrology",
        ["snowmelt"] = "Hydrology",
        ["gutter-inlets"] = "Streets and inlets", ["street-capacity"] = "Streets and inlets", ["inlet-network"] = "Streets and inlets",
        ["sag-ponding"] = "Streets and inlets",
        ["deck-drainage"] = "Streets and inlets",
        ["network-plan-review"] = "Storm sewer design", ["network-optimizer"] = "Storm sewer design", ["plan-profile-dxf"] = "Storm sewer design",
        ["pump-station"] = "Storm sewer design",
        ["swmm-exchange"] = "Storm sewer design", ["dynamic-wave"] = "Storm sewer design",
        ["sediment-transport"] = "Open channels and structures",
        ["channel-lining"] = "Open channels and structures",
        ["detention-routing"] = "Detention and water quality", ["pond-storage"] = "Detention and water quality",
        ["outlet-designer"] = "Detention and water quality", ["water-quality"] = "Detention and water quality",
        ["spillway-breach"] = "Detention and water quality",
        ["erosion-sediment"] = "Detention and water quality",
        ["groundwater-mounding"] = "Detention and water quality",
        ["backwater-profile"] = "Open channels and structures", ["channel-profile"] = "Open channels and structures",
        ["floodplain-risk"] = "Open channels and structures",
        ["river-profile"] = "Open channels and structures", ["hds5-culvert"] = "Open channels and structures",
        ["aop-culvert"] = "Open channels and structures",
        ["outlet-protection"] = "Open channels and structures", ["stilling-basin"] = "Open channels and structures",
        ["riprap-revetment"] = "Open channels and structures",
        ["bridge-scour"] = "Open channels and structures",
        ["scour-countermeasures"] = "Open channels and structures",
        // Ten structure, measurement and storage tools added together; every PR of that set carries this same block.
        ["manhole-sizing"] = "Storm sewer design", ["pipe-structural"] = "Storm sewer design", ["structure-buoyancy"] = "Storm sewer design",
        ["roof-drainage"] = "Streets and inlets", ["slope-area"] = "Hydrology", ["flow-measurement"] = "Open channels and structures",
        ["trash-rack"] = "Detention and water quality", ["flow-splitter"] = "Detention and water quality",
        ["storage-estimate"] = "Detention and water quality", ["underground-detention"] = "Detention and water quality",
        ["quantity-takeoff"] = "Quantities and cost", ["cost-estimate"] = "Quantities and cost",
        ["batch-scenarios"] = "Productivity and deliverables", ["inverse-design"] = "Productivity and deliverables",
        ["pond-sediment"] = "Detention and water quality",
        ["drainage-report"] = "Productivity and deliverables", ["redesign-diff"] = "Productivity and deliverables",
        ["submittal-precheck"] = "Productivity and deliverables", ["review-comments"] = "Productivity and deliverables",
        ["site-feasibility"] = "Land development and construction", ["grading-solver"] = "Land development and construction",
        ["pavement-edge-drain"] = "Streets and inlets", ["bioretention-underdrain"] = "Detention and water quality",
        ["utility-conflicts"] = "Land development and construction", ["permit-tracker"] = "Land development and construction",
        ["field-to-model"] = "Land development and construction", ["construction-companion"] = "Land development and construction",
        ["stream-stability"] = "Open channels and structures",
        ["climate-stress"] = "Land development and construction", ["memory-search"] = "Land development and construction",
        ["bridge-debris-forces"] = "Open channels and structures",
        // Sanitary sewer tools (the lift station id is listed ahead of its PR so both PRs leave this block alike).
        ["sanitary-network"] = "Sanitary sewers", ["sanitary-lift-station"] = "Sanitary sewers",
        ["sewer-drop-structures"] = "Sanitary sewers",
    };

    public static string Category(IDesignTool tool) => Map.TryGetValue(tool.Id, out var c) ? c : "Other";

    /// <summary>Tools in category order, then by name; <paramref name="filter"/> matches name, description or category (case-insensitive, all words).</summary>
    public static IEnumerable<(string Category, IDesignTool Tool)> Grouped(IEnumerable<IDesignTool> tools, string? filter = null)
    {
        var words = (filter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tools.Select(t => (Category: Category(t), Tool: t))
            .Where(x => words.All(w => x.Tool.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                    || x.Tool.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
                                    || x.Category.Contains(w, StringComparison.OrdinalIgnoreCase)
                                    || x.Tool.Id.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => CategoryOrder.ToList().IndexOf(x.Category)).ThenBy(x => x.Tool.Name, StringComparer.OrdinalIgnoreCase);
    }
}
