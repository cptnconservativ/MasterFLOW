using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Sewer main in a casing pipe (trenchless crossing) by Rapid City IDCM §3.12.11: casing inside diameter at least
/// 6 in. over the carrier's largest bell OD, the City's minimum steel wall thickness table (35,000 psi steel) or
/// reinforced concrete casing, centering chocks at each side of every joint, at mid-length and at 10 ft maximum, end
/// seals, and two installation checks the IDCM leaves to the design: whether the bore can hold the sewer's grade
/// within the room the casing leaves around the bells, and the jacking force against the casing's axial capacity.
/// The §3.12.12 concrete pipe cap at stream crossings is sized when <c>protection = cap</c>.
/// </summary>
public sealed class SewerCasingTool : IDesignTool
{
    public string Id => "sewer-casing";
    public string Name => "Sewer casing and trenchless crossing";
    public string Description => "Sewer main in a casing pipe (Rapid City IDCM §3.12.11): casing size from the carrier bell OD plus 6 in., the City's minimum steel wall thickness by casing diameter (35,000 psi), or RCP casing; casing chocks at each side of every joint, at mid-length and 10 ft maximum; casing inverts from the carrier grade; the longest bore that holds the sewer grade within the casing clearance; and jacking force against the casing capacity. Also sizes the §3.12.12 concrete pipe cap for a stream crossing.";
    public bool UsesNetwork => false;

    /// <summary>Standard steel casing outside diameters (in.).</summary>
    public static readonly double[] SteelOd = [8.625, 10.75, 12.75, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 54, 60, 66, 72];

    /// <summary>Reinforced concrete jacking pipe inside diameters (in.).</summary>
    public static readonly double[] RcpId = [12, 15, 18, 21, 24, 27, 30, 33, 36, 42, 48, 54, 60, 66, 72, 78, 84];

    /// <summary>IDCM §3.12.11 item 2 table, as printed: casing diameter up to (in.) → minimum wall (in.).</summary>
    public static readonly (double UpTo, double Wall)[] IdcmWall =
    [
        (13.999, 0.188), (16, 0.282), (18, 0.312), (20, 0.344), (22, 0.375), (24, 0.406), (26, 0.436), (30, 0.500), (32, 0.500), (36, 0.531), (42, 0.563),
    ];

    /// <summary>IDCM minimum steel casing wall (in.) for a casing of diameter <paramref name="d"/> in.; null above 42 in. (the table stops there).</summary>
    public static double? MinWall(double d)
    {
        foreach (var (upTo, wall) in IdcmWall) if (d <= upTo + 1e-9) return wall;
        return null;
    }

    /// <summary>Chocks in one carrier length <paramref name="lj"/> ft: one each side of the joints (at <paramref name="offset"/> ft), one at mid-length, more so no gap exceeds <paramref name="max"/> ft.</summary>
    public static (int PerLength, double Spacing) ChocksPerLength(double lj, double offset, double max)
    {
        double span = Math.Max(0, lj - 2 * offset);
        int m = Math.Max(2, 2 * (int)Math.Ceiling(span / (2 * max) - 1e-9));   // even, so one chock sits at mid-length
        return (m + 1, span / m);
    }

    /// <summary>Jacking force (lb): skin friction (psf) on the outside surface plus face pressure (psf) on the face area.</summary>
    public static double JackingForce(double odIn, double lengthFt, double frictionPsf, double facePsf)
        => frictionPsf * Math.PI * odIn / 12 * lengthFt + facePsf * Math.PI * Math.Pow(odIn / 12, 2) / 4;

    public string SampleInput(DesignToolContext context) => """
        # Sewer main in a casing pipe under a street (Rapid City IDCM §3.12.11). U.S. units: in., ft, psi.
        carrier = 10 in. PVC C900 DR18 restrained joint
        carrier OD = 11.10          # in., pipe barrel
        carrier ID = 9.87           # in.
        bell OD = 13.60             # in., largest OD at the restrained joint (from the joint maker)
        joint length = 20           # ft, carrier lay length
        casing length = 120         # ft
        casing material = steel     # steel or RCP
        casing yield = 35000        # psi (IDCM minimum)
        upstream invert = 3412.40   # carrier invert at the upstream end of the casing (ft)
        carrier slope = 0.40        # %, the sewer grade through the casing
        bore accuracy = 0.2         # % of the bore length the casing may stray (guided auger boring; about 1 % unguided)
        min chock height = 0.75     # in., the lowest runner a chock can have
        skin friction = 200         # psf on the casing surface while jacking
        face pressure = 0           # psf at the cutting face (auger boring removes the face)
        soil resistivity = 3500     # ohm-cm, from the corrosion tests
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sewer casing and trenchless crossing";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-CS-001", "The casing tool works in U.S. units (in., ft, psi); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        double? Need(string key, string what)
        {
            var v = p.OptionalNumber(key, what);
            if (v is null && !p.Has(key)) msgs.Add(CalcMessage.Error("E-CS-002", $"Give {what} ({key} = …)."));
            return v;
        }
        string protection = (p.Text("protection") ?? "casing").ToLowerInvariant();
        bool cap = protection.StartsWith("cap") || protection.Contains("concrete");
        double? od = Need("carrier OD", "the carrier pipe outside diameter in in.");
        double? bell = cap ? p.OptionalNumber("bell OD") : Need("bell OD", "the largest outside bell (joint) diameter of the carrier in in.");
        double? len = cap ? p.OptionalNumber("casing length") : Need("casing length", "the casing length in ft");
        double? carrierId = p.OptionalNumber("carrier ID");
        string material = (p.Text("casing material") ?? "steel").ToLowerInvariant();
        bool rcp = material.Contains("rcp") || material.Contains("concrete");
        double lj = p.Number("joint length", 20), clearance = p.Number("clearance", 6), maxSpacing = p.Number("max chock spacing", 10);
        double offset = p.Number("chock offset", 1.0), fy = p.Number("casing yield", 35000), accuracy = p.Number("bore accuracy", 1.0);
        double hMin = p.Number("min chock height", 0.75), friction = p.Number("skin friction", 200), face = p.Number("face pressure", 0);
        double? givenOd = p.OptionalNumber("casing OD"), givenId = p.OptionalNumber("casing ID"), givenWall = p.OptionalNumber("casing wall");
        double? usInv = p.OptionalNumber("upstream invert"), slopePct = p.OptionalNumber("carrier slope"), resist = p.OptionalNumber("soil resistivity");
        double? streamLen = p.OptionalNumber("crossing length");
        bool restrained = p.Flag("restrained joints", true);

        if (od is <= 0 || bell is <= 0 || len is <= 0) msgs.Add(CalcMessage.Error("E-CS-003", "Carrier OD, bell OD and casing length must be above zero."));
        if (od is { } o0 && bell is { } b0 && b0 < o0) msgs.Add(CalcMessage.Error("E-CS-003", "The bell OD cannot be smaller than the carrier barrel OD."));
        if (carrierId is { } ci0 && od is { } o1 && (ci0 <= 0 || ci0 >= o1)) msgs.Add(CalcMessage.Error("E-CS-003", "The carrier ID must be above zero and below its OD."));
        if (!(lj > 0) || !(maxSpacing > 0) || offset < 0 || offset * 2 >= lj || clearance < 0 || !(fy > 0) || accuracy < 0 || hMin < 0 || friction < 0 || face < 0)
            msgs.Add(CalcMessage.Error("E-CS-003", "Joint length, chock spacing and yield must be above zero, the chock offset less than half a joint, and clearance, accuracy, chock height, friction and face pressure zero or more."));
        if (givenOd is <= 0 || givenId is <= 0 || givenWall is <= 0) msgs.Add(CalcMessage.Error("E-CS-003", "Casing OD, ID and wall must be above zero."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        return cap ? RunCap(title, od!.Value, streamLen, msgs) : RunCasing(title, msgs, od!.Value, bell!.Value, len!.Value, carrierId, rcp, lj, clearance, maxSpacing, offset, fy,
            accuracy, hMin, friction, face, givenOd, givenId, givenWall, usInv, slopePct, resist, restrained, p.Text("carrier"));
    }

    private static ToolResult RunCasing(string title, List<CalcMessage> msgs, double od, double bell, double len, double? carrierId, bool rcp, double lj, double clearance,
        double maxSpacing, double offset, double fy, double accuracy, double hMin, double friction, double face, double? givenOd, double? givenId, double? givenWall,
        double? usInv, double? slopePct, double? resist, bool restrained, string? carrierName)
    {
        double needId = bell + clearance;
        double cOd, cId, wall; double? minWall = null; string sizeNote;
        if (rcp)
        {
            cId = givenId ?? RcpId.FirstOrDefault(d => d >= needId - 1e-9, double.NaN);
            if (double.IsNaN(cId)) { cId = Math.Ceiling(needId / 6) * 6; msgs.Add(CalcMessage.Warning("W-CS-004", $"The casing needs {needId:F2} in. inside, above the listed RCP jacking sizes; {cId:G3} in. is assumed.")); }
            wall = givenWall ?? cId / 12 + 1;   // ASTM C76 wall B
            cOd = cId + 2 * wall;
            sizeNote = $"RCP casing {cId:G3} in. ID, wall {wall:F2} in.{(givenWall is null ? " (ASTM C76 wall B)" : "")}, OD {cOd:F2} in.";
        }
        else
        {
            if (givenOd is { } go)
            {
                cOd = go; minWall = MinWall(go);
                wall = givenWall ?? (givenId is { } gi ? (go - gi) / 2 : minWall ?? 0.563);
            }
            else
            {
                cOd = double.NaN; wall = double.NaN;
                foreach (var d in SteelOd)
                {
                    double t = Math.Max(MinWall(d) ?? 0.563, givenWall ?? 0);
                    if (d - 2 * t >= needId - 1e-9) { cOd = d; wall = t; break; }
                }
                if (double.IsNaN(cOd)) { wall = Math.Max(0.563, givenWall ?? 0); cOd = Math.Ceiling((needId + 2 * wall) / 6) * 6; }
                minWall = MinWall(cOd);
            }
            cId = givenId ?? cOd - 2 * wall;
            if (givenId is { } gi2 && givenOd is { } go2) wall = (go2 - gi2) / 2;
            sizeNote = $"Steel casing {cOd:G4} in. OD × {wall:F3} in. wall, {cId:F2} in. ID ({fy:N0} psi).";
            if (minWall is { } mw && wall < mw - 0.0015)
                msgs.Add(CalcMessage.Warning("W-CS-002", $"Casing wall {wall:F3} in. is below the IDCM §3.12.11 minimum {mw:F3} in. for a {cOd:G4} in. casing."));
            if (minWall is null)
                msgs.Add(CalcMessage.Warning("W-CS-004", $"The IDCM §3.12.11 wall table stops at 42 in.; for this {cOd:G4} in. casing {wall:F3} in. is used{(givenWall is null ? " (the 42 in. value, as a floor)" : "")}. Set the wall from a structural design (earth and live load, jacking) and the owner of the crossing (AREMA for railroads)."));
            if (fy < 35000 - 1e-9) msgs.Add(CalcMessage.Warning("W-CS-003", $"Casing steel yield {fy:N0} psi is below the 35,000 psi IDCM §3.12.11 requires."));
        }
        if (cId < needId - 1e-9)
            msgs.Add(CalcMessage.Warning("W-CS-001", $"Casing ID {cId:F2} in. is less than the bell OD {bell:F2} in. plus {clearance:G3} in. = {needId:F2} in. (IDCM §3.12.11 item 3)."));
        if (!restrained) msgs.Add(CalcMessage.Warning("W-CS-008", "Sewer mains in a casing must have restrained joints (IDCM §3.12.11 item 1)."));
        if (maxSpacing > 10 + 1e-9) msgs.Add(CalcMessage.Warning("W-CS-007", $"Chock spacing {maxSpacing:G3} ft exceeds the 10 ft maximum (IDCM §3.12.11 item 4)."));
        if (2 * offset > maxSpacing + 1e-9) msgs.Add(CalcMessage.Warning("W-CS-007", $"The chocks either side of a joint are {2 * offset:G3} ft apart, more than the {maxSpacing:G3} ft maximum."));

        // Chocks
        var (perLen, spacing) = ChocksPerLength(lj, offset, maxSpacing);
        int lengths = (int)Math.Ceiling(len / lj - 1e-9);
        int chocks = lengths * perLen;

        // Line and grade: how far the casing may stray before the carrier can no longer be set on grade inside it.
        double room = (cId - bell) / 2;                       // in. above and below a centered bell
        double tolerance = Math.Max(0, room - hMin);          // in. the casing may be off grade (custom-height chocks take it up)
        double maxLen = accuracy > 0 ? tolerance / 12 / (accuracy / 100) : double.PositiveInfinity;
        double expected = accuracy / 100 * len * 12;          // in.
        double? needIdForGrade = accuracy > 0 && expected > tolerance ? bell + 2 * (expected + hMin) : null;
        if (needIdForGrade is { } ng)
        {
            double? fitOd = rcp ? null : SteelOd.Cast<double?>().FirstOrDefault(d => d - 2 * Math.Max(MinWall(d!.Value) ?? 0.563, wall) >= ng - 1e-9);
            double? fitId = rcp ? RcpId.Cast<double?>().FirstOrDefault(d => d >= ng - 1e-9) : null;
            string fix = rcp ? (fitId is { } fi ? $"a {fi:G3} in. RCP casing" : $"a casing over {ng:F1} in. ID") : (fitOd is { } fo ? $"a {fo:G4} in. steel casing" : $"a casing over {ng:F1} in. ID");
            msgs.Add(CalcMessage.Warning("W-CS-005", $"At {accuracy:G3} % accuracy a {len:F0} ft bore may stray {expected:F1} in.; the casing leaves {tolerance:F1} in. to set the carrier on grade, enough for {maxLen:F0} ft. Use {fix} (needs {ng:F1} in. ID), shorten the bore, or use a guided method (pilot-tube or guided auger boring) for the gravity sewer."));
        }

        // Jacking
        double force = JackingForce(cOd, len, friction, face);
        double steelArea = Math.PI / 4 * (cOd * cOd - cId * cId);
        double capacity = rcp ? double.NaN : 0.5 * fy * steelArea;
        if (!rcp && force > capacity) msgs.Add(CalcMessage.Warning("W-CS-006", $"Jacking force {force / 2000:F0} tons exceeds half the casing yield load {capacity / 2000:F0} tons; use a heavier wall, lubrication or intermediate jacking."));

        // Inverts
        double cWall = carrierId is { } ci ? (od - ci) / 2 : double.NaN;
        double drop = (cId - od) / 2 / 12 + (double.IsNaN(cWall) ? 0 : cWall / 12);   // carrier invert to casing invert (ft), carrier centered
        double? casUs = usInv is { } ui && !double.IsNaN(cWall) ? ui - drop : null;
        double? casDs = casUs is { } cu && slopePct is { } sp ? cu - sp / 100 * len : null;

        // Corrosion
        string corrosion = resist switch
        {
            null => "not given",
            < 1000 => "very corrosive",
            < 2000 => "corrosive",
            < 5000 => "moderately corrosive",
            < 10000 => "mildly corrosive",
            _ => "slightly corrosive or less",
        };

        var summary = new List<string>
        {
            $"{(carrierName is null ? "Carrier" : carrierName)}: bell OD {bell:F2} in. + {clearance:G3} in. needs {needId:F2} in. inside. {sizeNote}",
            $"Chocks: {perLen} per {lj:G3} ft length (one each side of the joint, one at mid-length, {spacing:F1} ft apart) × {lengths} lengths = {chocks} chocks, plus 2 end seals (no grout).",
            $"Grade: {tolerance:F1} in. of room to set the carrier on grade; at {accuracy:G3} % that holds for a {(double.IsInfinity(maxLen) ? "any" : maxLen.ToString("F0") + " ft")} bore against {len:F0} ft — {(needIdForGrade is null ? "OK" : "SHORT")}.",
            rcp ? $"Jacking force {force / 2000:F0} tons at {friction:G3} psf skin friction; check against the RCP jacking pipe maker's allowable load."
                : $"Jacking force {force / 2000:F0} tons at {friction:G3} psf skin friction against {capacity / 2000:F0} tons (½ × yield × steel area) — {(force <= capacity ? "OK" : "OVER")}.",
        };
        if (casUs is { } a1) summary.Add($"Casing invert {a1:F2} ft at the upstream end{(casDs is { } a2 ? $", {a2:F2} ft downstream" : "")} (carrier centered).");

        msgs.Add(CalcMessage.Info("I-CS-001", "IDCM §3.12.11: restrained joints; centering and restraining chocks of non-corrosive material, detailed on the drawings; prefabricated non-corrosive end seals and bands (grout seals of the annular space are not permitted); corrosion protection is part of the casing design."));
        msgs.Add(CalcMessage.Info("I-CS-002", $"Soil resistivity: {(resist is { } r ? $"{r:N0} ohm-cm, {corrosion} (NACE / Roberge classes)" : "not given")}. IDCM §3.9.14 for water mains asks for corrosion protection when the resistivity tests call for it and a casing life equal to the carrier's."));
        if (!rcp && cOd is >= 28 and <= 30)
            msgs.Add(CalcMessage.Info("I-CS-003", "The IDCM prints 0.500 in. for 28 and 30 in. casings in §3.12.11 (sewer) and 0.469 in. in §3.9.14 (water); the sewer value is used."));
        msgs.Add(CalcMessage.Info("I-CS-004", "Earth and live load on the casing are not checked here; run the pipe structural tool for the casing, and add the crossing owner's requirements (railroad, SDDOT) for length, depth and wall."));

        var wallRows = IdcmWall.Select(w => (IReadOnlyList<object?>)[w.UpTo < 14 ? "Under 14" : w.UpTo.ToString("G3"), w.Wall]).ToList();
        List<IReadOnlyList<object?>> sizeRows =
        [
            ["Carrier barrel OD (in.)", od], ["Carrier bell OD (in.)", bell], ["Required casing ID = bell OD + " + clearance.ToString("G3") + " in. (in.)", R(needId, 2)],
            ["Casing OD (in.)", R(cOd, 3)], ["Casing wall (in.)", R(wall, 3)], ["IDCM minimum wall (in.)", rcp ? "RCP" : minWall is { } m2 ? m2 : "not tabulated"], ["Casing ID (in.)", R(cId, 3)],
            ["Room above and below a centered bell (in.)", R(room, 2)], ["Grade tolerance after the lowest chock (in.)", R(tolerance, 2)],
            [$"Expected deviation at {accuracy:G3} % of {len:F0} ft (in.)", R(expected, 2)], ["Longest bore that holds grade (ft)", R(maxLen, 0)],
            ["Jacking force (tons)", R(force / 2000, 1)], ["Casing capacity, ½·Fy·As (tons)", rcp ? "maker" : R(capacity / 2000, 1)],
        ];
        if (casUs is { } u1) sizeRows.Add(["Casing invert, upstream (ft)", R(u1, 2)]);
        if (casDs is { } d1) sizeRows.Add(["Casing invert, downstream (ft)", R(d1, 2)]);

        var chockRows = new List<IReadOnlyList<object?>>();
        for (int i = 0; i <= perLen - 1; i++) chockRows.Add([i + 1, R(offset + i * spacing, 2), i == 0 || i == perLen - 1 ? "joint" : (i == (perLen - 1) / 2 ? "mid-length" : "intermediate")]);

        var tables = new List<ToolTable>
        {
            new("Casing size and checks", ["Item", "Value"], sizeRows,
                [$"Carrier set centered on chocks; the room above and below the bell, less the lowest chock runner ({hMin:G3} in.), is how far the casing can stray before custom chocks can no longer hold the carrier on grade."]),
            new("Chock layout, one carrier length", ["Chock", "Distance from the joint (ft)", "Position"], chockRows,
                [$"IDCM §3.12.11 item 4: each side of every joint, at mid-length, {maxSpacing:G3} ft maximum. {lengths} lengths in {len:F0} ft."]),
        };
        if (!rcp) tables.Add(new ToolTable("IDCM §3.12.11 minimum steel casing wall", ["Casing diameter (in.)", "Minimum wall (in.)"], wallRows,
            ["Steel with 35,000 psi minimum yield; additional thickness may be needed for long bores or poor soil."]));
        return new ToolResult(title, summary, tables, msgs);
    }

    private static ToolResult RunCap(string title, double od, double? crossingLength, List<CalcMessage> msgs)
    {
        double dFt = od / 12;
        double width = 3 * dFt;                              // ft, three pipe diameters
        double height = dFt / 2 + 0.5;                       // ft, spring line to 6 in. above the crown
        double area = width * height - Math.PI * dFt * dFt / 8;   // ft² of concrete, less the upper half of the pipe
        double perFt = area / 27;
        var summary = new List<string>
        {
            $"Concrete pipe cap (IDCM §3.12.12): {width:F2} ft wide (3 × {od:G4} in.), from the spring line to 6 in. above the crown ({height:F2} ft tall), reinforced, 6 in. minimum thickness.",
            $"Concrete {perFt:F3} yd³ per ft{(crossingLength is { } cl ? $"; {cl:F0} ft crossing = {perFt * cl:F1} yd³" : "")}.",
        };
        msgs.Add(CalcMessage.Info("I-CS-005", "IDCM §3.12.12: the cap runs the full stream crossing, which starts and ends 20 ft from the stream banks or where scour and erosion will not occur; pipe and installation by Ten States Standards, SDDENR and City specifications. Check scour depth below the cap with the scour tools."));
        List<IReadOnlyList<object?>> rows = [["Pipe OD (in.)", od], ["Cap width, 3 × D (ft)", R(width, 2)], ["Cap height, spring line to crown + 6 in. (ft)", R(height, 2)], ["Concrete over the crown (in.)", 6], ["Concrete per ft (yd³)", R(perFt, 3)]];
        if (crossingLength is { } c2) rows.Add(["Crossing length (ft)", c2]);
        return new ToolResult(title, summary, [new ToolTable("Stream crossing pipe cap", ["Item", "Value"], rows)], msgs);
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
