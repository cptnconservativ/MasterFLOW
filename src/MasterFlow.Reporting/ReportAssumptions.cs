using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Reporting;

/// <summary>
/// Method lead-in and assumptions note for a single-element (uniform flow) report, worded for the
/// element type: closed conduits (circular and elliptical pipe) get a gravity-pipe note, open
/// channels a prismatic-channel note, and irregular sections with point roughness a composite-n note.
/// Shared by the PDF and HTML reports so the two never disagree.
/// </summary>
public static class ReportAssumptions
{
    /// <summary>True for sections that are closed conduits (pipes).</summary>
    public static bool IsPipe(ChannelSection section) => section is CircularSection or EllipticalSection;

    /// <summary>First phrase of the Method paragraph (without the equation).</summary>
    public static string MethodLead(ChannelSection section) => IsPipe(section)
        ? "Steady, uniform gravity flow in a closed conduit flowing partly full (free water surface at atmospheric pressure) by Manning's equation"
        : "Steady, uniform open-channel flow by Manning's equation";

    /// <summary>The Info note at the end of "Warnings, notes and assumptions".</summary>
    public static string Note(UniformFlowResult result)
    {
        var section = result.Input.Section;
        if (IsPipe(section))
        {
            string shape = section is EllipticalSection ? "elliptical pipe" : "circular pipe";
            string full = result.Conduit is { } cr && result.Normal.Depth >= cr.Rise*(1-1e-9)
                ? " At this depth the pipe is just full at the crown: the result is the gravity full-flow condition, not a surcharged (pressure) condition."
                : "";
            return $"Assumes steady, uniform gravity flow in a straight {shape} of constant size, slope and roughness between structures, flowing partly full with a free water surface (friction slope = pipe slope). " +
                   "Manning's n is uniform around the wetted perimeter and constant with depth. " +
                   "Surcharged (pressure) flow, the hydraulic grade line, and entrance, exit, junction and bend losses are not included; model those in a storm network (HGL/EGL)." +
                   full +
                   " Manning's n is the engineer's selection; results are only as reliable as n.";
        }
        if (section is IrregularSection { HasPointRoughness: true })
            return "Assumes a prismatic channel, small slope (depth measured vertically) and hydrostatic pressure. Roughness varies across the section and is combined as stated in the input; the velocity head uses the mean velocity (α = 1). Manning's n values are the engineer's selection; results are only as reliable as n.";
        return "Assumes a prismatic channel, uniform roughness across the wetted perimeter, small slope (depth measured vertically) and hydrostatic pressure. Manning's n is the engineer's selection; results are only as reliable as n.";
    }
}
