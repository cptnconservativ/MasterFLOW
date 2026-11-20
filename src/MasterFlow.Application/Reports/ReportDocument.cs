using MasterFlow.Application.Tools;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Reports;

/// <summary>One piece of a report, in reading order. The Reporting project renders blocks to HTML, PDF and Word.</summary>
public abstract record ReportBlock;

/// <summary>Numbered heading; level 1 or 2. Level-1 and level-2 headings make the table of contents.</summary>
public sealed record ReportHeading(int Level, string Text) : ReportBlock;
public sealed record ReportParagraph(string Text, bool Bullet = false) : ReportBlock;
public sealed record ReportTable(ToolTable Table) : ReportBlock;
public sealed record ReportChart(ToolChart Chart) : ReportBlock;
/// <summary>Messages of a calculation, labelled with where they came from.</summary>
public sealed record ReportMessages(string Source, IReadOnlyList<CalcMessage> Messages) : ReportBlock;
/// <summary>Monospaced text (calculation inputs).</summary>
public sealed record ReportCode(string Title, string Text) : ReportBlock;
/// <summary>A solved storm network: profiles, pipe and structure tables and messages.</summary>
public sealed record ReportNetwork(StormNetworkResult Result) : ReportBlock;
public sealed record ReportPageBreak : ReportBlock;
/// <summary>Engineer's certification and stamp area.</summary>
public sealed record ReportCertification(string Statement, string Engineer, string License, string Firm) : ReportBlock;

/// <summary>
/// A drainage report: cover fields, body blocks (portrait) and appendix blocks (landscape, for wide calculation tables).
/// </summary>
public sealed record ReportDocument(string Title, IReadOnlyList<(string Label, string Value)> Cover, IReadOnlyList<ReportBlock> Body, IReadOnlyList<ReportBlock> Appendix)
{
    public string Subtitle { get; init; } = "";
    public bool TableOfContents { get; init; } = true;
    /// <summary>Headings in order with their numbers, for tables of contents.</summary>
    public IEnumerable<ReportHeading> Headings => Body.Concat(Appendix).OfType<ReportHeading>();
}
