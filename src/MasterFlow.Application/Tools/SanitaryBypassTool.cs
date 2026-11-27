using System.Globalization;
using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Bypass pumping plan for a tie-in or repair on a sanitary sewer: the hourly flow at the connection from the diurnal
/// pattern of the loads above it, the pump capacity and duration for the work window, the lowest-flow window, the
/// bypass line size and total dynamic head (Hazen-Williams), suction lift against the NPSH available at the site
/// elevation, and how long the sewer upstream can hold the flow if the pumps stop.
/// </summary>
public sealed class SanitaryBypassTool : IDesignTool
{
    public string Id => "sanitary-bypass";
    public string Name => "Sanitary bypass pumping plan for tie-ins";
    public string Description => "Bypass pumping for a sanitary sewer tie-in or repair: hourly flow at the connection from the diurnal pattern of the loads upstream (from the project's sanitary sewer design, or an entered average flow), pump capacity with standby, pumping duration and volume, the lowest-flow work window, bypass line size and total dynamic head by Hazen-Williams, suction lift and NPSH at the site elevation, and the time the upstream sewer can store the flow if the pumps stop.";
    public bool UsesNetwork => false;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double GpmPerCfs = 448.831, G = 32.174, GalPerFt3 = 7.48052;

    /// <summary>Atmospheric pressure head (ft of water at 60 °F) at an elevation (ft): 33.96·(1 − 6.8754×10⁻⁶·z)^5.2559 (standard atmosphere).</summary>
    public static double AtmosphericHead(double elevationFt) => 33.96 * Math.Pow(1 - 6.8754e-6 * elevationFt, 5.2559);

    /// <summary>
    /// Storage (ft³) gained in a circular pipe (D ft, inverts z1 to z2 over L ft) when the water rises from depth
    /// <paramref name="y0"/> (normal depth of the flow already in it) to a level plane at <paramref name="h"/>; 200 slices.
    /// </summary>
    public static double PipeStorage(double h, double z1, double z2, double l, double d, double y0 = 0)
    {
        const int n = 200;
        double v = 0, a0 = y0 > 0 ? SanitarySewerTool.Area(Math.Min(y0, d), d) : 0;
        for (int i = 0; i < n; i++)
        {
            double z = z1 + (z2 - z1) * (i + 0.5) / n, y = Math.Clamp(h - z, 0, d);
            if (y > y0) v += (SanitarySewerTool.Area(y, d) - a0) * l / n;
        }
        return v;
    }

    public string SampleInput(DesignToolContext context)
    {
        var saved = SanitaryDesign.FromProject(context)?.Design;
        string conn = saved?.Outlets.FirstOrDefault()?.Name ?? "S-5";
        string src = saved is null
            ? "# No saved Sanitary sewer network design in this project: the network below is the sanitary-network sample.\n" + new SanitarySewerTool().SampleInput(context)
            : "# The project's Sanitary sewer network design gives the flow, pattern, inverts and rims at the connection.";
        return $"""
            # Bypass pumping plan for a tie-in at a sanitary sewer. U.S. units: gpm, ft, in., hours.
            connection = {conn}               # the pipe to be plugged; the bypass draws from its upstream manhole
            discharge to =                  # manhole the bypass discharges into; blank = the connection's downstream manhole
            average flow =                  # gpm; blank = the design's average flow in the connection (with its infiltration)
            pattern =                       # 24 hourly multipliers, residential, commercial or flat; blank = the loads' blend
            wet weather factor = 1.0        # multiplier on the dry-weather flow (rain during the work)
            work start = 22:00              # clock time the line is plugged
            work duration = 6               # h with the line plugged
            setup time = 1                  # h of pumping before the plug and after it is pulled (each)
            design flow = daily peak        # daily peak, window peak, design peak (IDCM Qpeak of the pipe), or gpm
            safety factor = 1.0             # on the design flow
            standby pumps = 1               # of the duty pump's size, on site and connected
            pump capacity =                 # gpm of one pump; blank = one duty pump for the whole design flow
            pump efficiency = 0.60
            discharge length =              # ft of bypass line; blank = the connection length + 20 ft of risers
            suction length = 25             # ft of suction hose from the wet manhole to the pump
            line sizes = 4, 6, 8, 10, 12, 14, 16, 18   # in. inside diameter of the discharge line (HDPE or lay-flat)
            suction diameter =              # in.; blank = the discharge line size
            max line velocity = 8           # ft/s in the discharge line
            C = 150                         # Hazen-Williams (HDPE 150; lay-flat hose about 140)
            fittings K = 5                  # sum of fitting and valve K on the discharge side (entrance, elbows, check valve, exit)
            riser height = 3                # ft the discharge line rises above the discharge manhole rim
            site elevation =                # ft, for atmospheric pressure; blank = the suction manhole rim
            water temperature = 60          # °F (vapor pressure)
            NPSH required =                 # ft at the design flow, from the pump curve; blank = not checked
            response time = 30              # min the crew needs to restore pumping (standby start, repair)
            backup elevation =              # ft, lowest service or basement floor upstream (optional)
            # Without a sanitary design: give average flow and pattern, and static head (ft, pump-down level to the
            # high point of the discharge line) and suction lift (ft, pump-down level to the pump).
            # static head =
            # suction lift =
            {src}
            """;
    }

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Sanitary bypass pumping plan";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-BP-001", "The sanitary tools work in U.S. units (gpm, in., ft); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();

        // ---- design (optional) ----
        SanitaryDesign? design = null;
        bool own = p.HasSection("Pipes") || p.HasSection("Loads");
        var sd = own ? SanitarySewerTool.Design(input, context) : SanitaryDesign.FromProject(context);
        if (sd is { Design: { } d0 }) design = d0;
        else if (sd is { } bad && own)
        {
            msgs.Add(CalcMessage.Error("E-BP-002", "The sanitary sewer tables have errors:"));
            msgs.AddRange(bad.Result.Messages.Where(m => m.Severity == MessageSeverity.Error));
            return new(title, [], [], msgs);
        }
        string? connName = p.Text("connection");
        SanitaryDesignPipe? conn = null;
        if (connName is not null)
        {
            if (design is null) msgs.Add(CalcMessage.Error("E-BP-002", $"connection = {connName}: there is no sanitary sewer design in the project. Design the sewers with the Sanitary sewer network design tool, paste its tables here, or leave connection blank and give the average flow."));
            else if ((conn = design.Pipe(connName)) is null) msgs.Add(CalcMessage.Error("E-BP-003", $"connection = {connName} is not a pipe of the sanitary design ({string.Join(", ", design.Pipes.Select(x => x.Name))})."));
        }
        double? qAveIn = p.OptionalNumber("average flow");
        if (conn is null && qAveIn is null && !msgs.Any(m => m.Severity == MessageSeverity.Error))
            msgs.Add(CalcMessage.Error("E-BP-004", "Give the connection (a pipe of the sanitary design) or the average flow (gpm) at the tie-in."));
        if (qAveIn is <= 0) msgs.Add(CalcMessage.Error("E-BP-004", "average flow must be above zero."));

        // ---- pattern at the connection ----
        List<SanitaryDesignLoad> upLoads = [];
        List<SanitaryDesignPipe> upPipes = [];
        if (conn is not null && design is not null)
        {
            upPipes = design.Upstream(conn);
            var mhs = upPipes.Select(x => x.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
            upLoads = design.Loads.Where(l => mhs.Contains(l.Manhole)).ToList();
        }
        double[] blend = upLoads.Count > 0 ? SanitaryDiurnal.Blend(upLoads.Select(l => (l.QaveGpm, (IReadOnlyList<double>)SanitaryDiurnal.For(l.Use)))) : SanitaryDiurnal.Residential;
        double[] pattern = SanitaryDiurnal.Parse(p.Text("pattern"), blend, "pattern", msgs, "E-BP-005");
        string patternName = p.Text("pattern") is { } pt ? (pt.Contains(',') ? "entered" : pt.Trim().ToLowerInvariant()) : upLoads.Count > 0 ? "blend of the loads upstream (MasterFlow default land-use patterns)" : "residential (MasterFlow default)";
        double gwi = conn is not null ? conn.IiGpm - conn.InflowGpm : 0;
        // An entered average flow replaces the design's (it is the whole flow); the design's infiltration is added, unpatterned, only to the design's average.
        double qSan = qAveIn ?? conn?.QaveGpm ?? 0;
        if (qAveIn is not null) gwi = 0;
        double wet = p.Number("wet weather factor", 1.0), sf = p.Number("safety factor", 1.0);
        if (!(wet >= 1) || !(sf >= 1)) msgs.Add(CalcMessage.Error("E-BP-006", "wet weather factor and safety factor must be 1 or more."));

        // ---- window ----
        double? start = SanitaryDiurnal.ParseClock(p.Text("work start") ?? "22:00");
        if (start is null) msgs.Add(CalcMessage.Error("E-BP-006", $"work start \"{p.Text("work start")}\" is not a clock time (22:00 or 10 PM)."));
        double work = p.Number("work duration", 6), setup = p.Number("setup time", 1);
        if (!(work > 0) || !(setup >= 0) || work + 2 * setup > 24) msgs.Add(CalcMessage.Error("E-BP-006", "work duration must be above zero, setup time not negative, and the pumping period (work + 2 × setup) at most 24 h."));

        // ---- pumps and line ----
        string basis = (p.Text("design flow") ?? "daily peak").Trim().ToLowerInvariant();
        double? basisNum = ToolInput.TryNumber(basis, out double bq) ? bq : null;
        if (basisNum is null && !basis.StartsWith("daily") && !basis.StartsWith("window") && !basis.StartsWith("design")) msgs.Add(CalcMessage.Error("E-BP-007", $"design flow \"{basis}\" is not daily peak, window peak, design peak or a flow in gpm."));
        if (basisNum is <= 0) msgs.Add(CalcMessage.Error("E-BP-007", "A design flow in gpm must be above zero."));
        if (basis.StartsWith("design") && conn is null) msgs.Add(CalcMessage.Error("E-BP-007", "design flow = design peak needs a connection pipe of the sanitary design."));
        int standby = (int)Math.Round(p.Number("standby pumps", 1));
        double? pumpCap = p.OptionalNumber("pump capacity");
        double eff = p.Number("pump efficiency", 0.60);
        double suctionLen = p.Number("suction length", 25), vMax = p.Number("max line velocity", 8), c = p.Number("C", 150), kFit = p.Number("fittings K", 5), riser = p.Number("riser height", 3);
        double tempF = p.Number("water temperature", 60), respMin = p.Number("response time", 30);
        double? npshR = p.OptionalNumber("NPSH required"), dSuctIn = p.OptionalNumber("suction diameter"), lenIn = p.OptionalNumber("discharge length"), siteEl = p.OptionalNumber("site elevation");
        var sizes = new List<double>();
        foreach (var t in (p.Text("line sizes") ?? "4,6,8,10,12,14,16,18").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (ToolInput.TryNumber(t, out double v) && v > 0) sizes.Add(v); else msgs.Add(CalcMessage.Error("E-BP-008", $"Line size \"{t}\" is not a positive number."));
        sizes.Sort();
        if (standby < 0) msgs.Add(CalcMessage.Error("E-BP-008", "standby pumps cannot be negative."));
        if (pumpCap is <= 0 || !(eff is > 0 and <= 1) || !(c > 0) || !(vMax > 0) || kFit < 0 || suctionLen < 0 || riser < 0 || dSuctIn is <= 0 || lenIn is <= 0 || !(tempF is > 32 and < 212) || !(respMin >= 0))
            msgs.Add(CalcMessage.Error("E-BP-008", "pump capacity, efficiency (0–1), C, max velocity, lengths, diameters and the water temperature (32–212 °F) must be valid; K, riser and response time cannot be negative."));

        // ---- elevations ----
        string? dischargeName = p.Text("discharge to") ?? conn?.To;
        SanitaryDesignManhole? suctMh = conn is not null ? design!.Manhole(conn.From) : null, disMh = dischargeName is not null ? design?.Manhole(dischargeName) : null;
        if (design is not null && p.Text("discharge to") is { } dt0 && disMh is null) msgs.Add(CalcMessage.Error("E-BP-003", $"discharge to = {dt0} is not a manhole of the sanitary design."));
        double? suctWs = null, disHigh = null, pumpEl = null;
        if (conn is not null && design is not null)
        {
            // Pump-down level: the lowest invert at the suction manhole (the bypass keeps the manhole drawn down).
            suctWs = Math.Min(conn.UpInvert, design.Pipes.Where(x => x.To.Equals(conn.From, StringComparison.OrdinalIgnoreCase)).Select(x => x.DownInvert).DefaultIfEmpty(conn.UpInvert).Min());
            pumpEl = suctMh?.Rim;
            if (disMh?.Rim is { } dr) disHigh = dr + riser;
        }
        double? statIn = p.OptionalNumber("static head"), liftIn = p.OptionalNumber("suction lift");
        if (statIn is not null) disHigh = null;
        if (conn is null && (statIn is null || liftIn is null))
            msgs.Add(CalcMessage.Error("E-BP-009", "Without a connection pipe, give the static head (ft, pump-down level to the high point of the discharge line) and the suction lift (ft, pump-down level to the pump)."));
        else if (conn is not null && statIn is null && (disHigh is null || pumpEl is null))
            msgs.Add(CalcMessage.Error("E-BP-009", "The rims of the suction and discharge manholes are needed for the lift (give them under [Manholes]), or give static head and suction lift."));
        double lineLen = lenIn ?? (conn is not null ? conn.Length + 20 : 100);

        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        double qAve = qSan + gwi;
        double Flow(double h) => (qSan * SanitaryDiurnal.At(pattern, h) + gwi) * wet;
        double s0 = start!.Value, pumpStart = s0 - setup, pumpHours = work + 2 * setup;
        double WindowPeak(double from, double hours) => Enumerable.Range(0, (int)Math.Ceiling(hours * 4 - 1e-9)).Max(i => Flow(from + Math.Min(hours - 1e-9, i * 0.25)));
        double WindowVol(double from, double hours) { double v = 0; int n = (int)Math.Round(hours * 60); for (int i = 0; i < n; i++) v += Flow(from + (i + 0.5) / 60); return v; } // gal
        double dailyPeak = Enumerable.Range(0, 24).Max(h => Flow(h));
        int peakHour = Enumerable.Range(0, 24).MaxBy(h => Flow(h));
        double winPeak = WindowPeak(pumpStart, pumpHours);
        double winVol = WindowVol(pumpStart, pumpHours);
        double designPeak = conn is not null ? (conn.QpeakGpm + conn.IiGpm) * wet : double.NaN;
        double qDesign = (basisNum ?? (basis.StartsWith("window") ? winPeak : basis.StartsWith("design") ? designPeak : dailyPeak)) * sf;
        string basisText = basisNum is not null ? "entered" : basis.StartsWith("window") ? "the highest hour in the pumping period" : basis.StartsWith("design") ? "the IDCM design peak of the connection" : "the highest hour of the day";

        // Best window: the start (quarter hours) with the lowest peak, then the lowest volume, over the pumping period.
        double bestStart = Enumerable.Range(0, 96).Select(i => i * 0.25).OrderBy(t => Math.Round(WindowPeak(t - setup, pumpHours), 3)).ThenBy(t => WindowVol(t - setup, pumpHours)).First();

        // ---- pumps ----
        int duty = pumpCap is { } pc ? (int)Math.Ceiling(qDesign / pc - 1e-9) : 1;
        double onePump = pumpCap ?? qDesign;

        // ---- line ----
        double stat = statIn ?? disHigh!.Value - suctWs!.Value;
        double lift = liftIn ?? pumpEl!.Value - suctWs!.Value;
        var lineRows = new List<IReadOnlyList<object?>>();
        double? dLine = null;
        foreach (var dIn in sizes)
        {
            double v = qDesign / GpmPerCfs / (Math.PI * Math.Pow(dIn / 12, 2) / 4);
            double hf = SanitaryLiftStationTool.FrictionGpm(qDesign, lineLen, dIn, c);
            double tdhX = SanitaryLiftStationTool.SystemHead(qDesign, stat, lineLen, dIn, c, kFit);
            bool ok = v <= vMax + 1e-9;
            if (ok && dLine is null) dLine = dIn;
            lineRows.Add([dIn, R(v, 2), R(hf, 2), R(tdhX - stat - hf, 2), R(tdhX, 1), ok ? (dLine == dIn ? "selected" : "") : $"over {vMax:G3} ft/s"]);
        }
        if (dLine is null) { dLine = sizes[^1]; msgs.Add(CalcMessage.Warning("W-BP-001", $"No listed line size keeps the velocity at {qDesign:F0} gpm under {vMax:G3} ft/s; the largest, {dLine:G3} in., is used. Run parallel lines or add larger sizes.")); }
        double dL = dLine.Value, dS = dSuctIn ?? dL;
        double vLine = qDesign / GpmPerCfs / (Math.PI * Math.Pow(dL / 12, 2) / 4);
        double hfLine = SanitaryLiftStationTool.FrictionGpm(qDesign, lineLen, dL, c);
        double hfSuct = SanitaryLiftStationTool.SystemHead(qDesign, 0, suctionLen, dS, c, 1.0);   // entrance + strainer K = 1.0
        double tdh = SanitaryLiftStationTool.SystemHead(qDesign, stat, lineLen, dL, c, kFit) + hfSuct;
        double whp = qDesign * tdh / 3960, bhp = whp / eff;
        double motor = SanitaryLiftStationTool.MotorHp.FirstOrDefault(m => m >= (pumpCap is not null ? onePump * tdh / 3960 / eff : bhp) - 1e-9, double.NaN);
        double vAve = qAve * wet / GpmPerCfs / (Math.PI * Math.Pow(dL / 12, 2) / 4);

        // NPSH available: atmospheric − suction lift − suction losses − vapor pressure.
        double elev = siteEl ?? pumpEl ?? 0;
        double hAtm = AtmosphericHead(elev);
        double hVap = VaporHead(tempF);
        double npshA = hAtm - lift - hfSuct - hVap;
        if (siteEl is null && pumpEl is null) msgs.Add(CalcMessage.Info("I-BP-001", "No site elevation: atmospheric pressure at sea level is used for the NPSH available."));
        double maxPractical = npshA + lift;   // lift at which NPSHa = 0
        if (npshR is { } nr && npshA < nr + 1e-9)
            msgs.Add(CalcMessage.Warning("W-BP-002", $"NPSH available {npshA:F1} ft is below the pump's NPSH required {nr:G3} ft: the pump will cavitate. Lower the pump, shorten the suction or use a submersible pump in the manhole."));
        else if (npshA < 3)
            msgs.Add(CalcMessage.Warning("W-BP-002", $"NPSH available is only {npshA:F1} ft at a {lift:F1} ft suction lift (site at {elev:F0} ft, atmospheric head {hAtm:F1} ft): above-ground suction-lift pumps are unlikely to work. Use a submersible pump in the manhole."));
        if (vAve < 2 - 1e-9) msgs.Add(CalcMessage.Info("I-BP-002", $"At the average flow the {dL:G3} in. line runs at {vAve:F2} ft/s, below 2 ft/s: solids can settle at low flow. Flush the line at teardown, or use the pump's on/off float control."));
        if (pumpCap is null) msgs.Add(CalcMessage.Info("I-BP-003", $"One duty pump of {qDesign:F0} gpm at {tdh:F1} ft TDH, with {standby} standby pump(s) of the same size connected to the line (the usual bypass specification). Give pump capacity to split the duty."));
        if (standby == 0) msgs.Add(CalcMessage.Warning("W-BP-003", "No standby pump: a pump failure backs the sewer up at once. Bypass specifications commonly require 100 % standby capacity."));
        if (conn is not null && basisNum is null && !basis.StartsWith("design") && qDesign < designPeak - 1e-6)
            msgs.Add(CalcMessage.Info("I-BP-004", $"The bypass is sized for {qDesign:F0} gpm ({basisText}), below the IDCM design peak of {conn.Name}, {designPeak:F0} gpm. Set design flow = design peak for a bypass that can run for days or through wet weather."));
        if (Math.Abs(bestStart - s0) > 0.01 && WindowPeak(bestStart - setup, pumpHours) < winPeak - 0.5)
            msgs.Add(CalcMessage.Info("I-BP-005", $"Starting at {SanitaryDiurnal.Clock(bestStart)} instead of {SanitaryDiurnal.Clock(s0)} lowers the highest flow while pumping from {winPeak:F0} to {WindowPeak(bestStart - setup, pumpHours):F0} gpm."));

        // ---- storage upstream if the pumps stop ----
        var storageRows = new List<IReadOnlyList<object?>>();
        double? minutesToOverflow = null;
        if (conn is not null && design is not null)
        {
            var storePipes = upPipes.Where(x => x != conn).ToList();
            var storeMhs = storePipes.Select(x => x.From).Append(conn.From).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => design.Manhole(n)!).ToList();
            double mhArea = Math.PI * design.ManholeDiameterFt * design.ManholeDiameterFt / 4;
            double MhInvert(string n) => design.Pipes.Where(x => x.From.Equals(n, StringComparison.OrdinalIgnoreCase)).Select(x => x.UpInvert)
                .Concat(design.Pipes.Where(x => x.To.Equals(n, StringComparison.OrdinalIgnoreCase)).Select(x => x.DownInvert)).Min();
            // Water already in the pipes: normal depth at each pipe's share of the pumping-period average flow.
            double qRun = winVol / (pumpHours * 60);
            double Y0(SanitaryDesignPipe x)
            {
                double frac = conn.QaveGpm + gwi > 0 ? (x.QaveGpm + x.IiGpm - x.InflowGpm) / (conn.QaveGpm + gwi) : 0;
                double y = SanitarySewerTool.NormalDepth(Math.Max(0, frac * qRun) / GpmPerCfs, x.DiameterIn / 12, x.N, x.Slope);
                return double.IsFinite(y) ? y : x.DiameterIn / 12;
            }
            double Storage(double h) => storePipes.Sum(x => PipeStorage(h, x.UpInvert, x.DownInvert, x.Length, x.DiameterIn / 12, Y0(x))) + storeMhs.Sum(m => mhArea * Math.Max(0, h - MhInvert(m.Name)));
            var levels = new List<(string Label, double H)>();
            var inc = design.Pipes.Where(x => x.To.Equals(conn.From, StringComparison.OrdinalIgnoreCase)).ToList();
            if (inc.Count > 0) levels.Add(($"Crown of the incoming pipe{(inc.Count > 1 ? "s (lowest)" : "")} at {conn.From} (surcharge begins)", inc.Min(x => x.DownInvert + x.DiameterIn / 12)));
            if (p.OptionalNumber("backup elevation") is { } be) levels.Add(("Backup elevation (lowest service or basement)", be));
            var rims = storeMhs.Where(m => m.Rim is not null).ToList();
            if (rims.Count > 0) { var low = rims.MinBy(m => m.Rim!.Value)!; levels.Add(($"Lowest rim upstream, {low.Name} (overflow)", low.Rim!.Value)); }
            foreach (var (label, h) in levels.OrderBy(l => l.H))
            {
                double vol = Storage(h) * GalPerFt3;
                double tPk = vol / winPeak, tDes = vol / qDesign;
                storageRows.Add([label, R(h, 2), R(vol, 0), R(tPk, 0), R(tDes, 0)]);
                if (label.Contains("overflow")) minutesToOverflow = tPk;
            }
            if (minutesToOverflow is { } mo && mo < respMin)
                msgs.Add(CalcMessage.Warning("W-BP-004", $"If the bypass stops at the highest flow while pumping, the sewer upstream overflows in {mo:F0} min, less than the {respMin:G3} min response time. Keep the standby pump running in auto with a high-level alarm, or start at a lower-flow hour."));
            if (storageRows.FirstOrDefault(r => ((string)r[0]!).StartsWith("Crown"))?[3] is double t0 && t0 < respMin)
                msgs.Add(CalcMessage.Info("I-BP-006", $"The incoming sewer at {conn.From} surcharges {t0:F0} min after a pump stoppage; services below its crown may back up before the overflow."));
        }

        // ---- tables ----
        var hourRows = new List<IReadOnlyList<object?>>();
        for (int h = 0; h < 24; h++)
        {
            bool inWin = InWindow(h + 0.5, pumpStart, pumpHours), plugged = InWindow(h + 0.5, s0, work);
            hourRows.Add([$"{SanitaryDiurnal.Clock(h)}–{SanitaryDiurnal.Clock(h + 1)}", R(pattern[h], 3), R(Flow(h), 1), R(Flow(h) / GpmPerCfs, 3), plugged ? "plugged" : inWin ? "pumping (setup)" : ""]);
        }
        var plan = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Average flow at the connection", R(qAve, 1), "gpm", conn is not null ? $"{conn.Name}: design Qave {conn.QaveGpm:F1} + infiltration {gwi:F1}" : "entered" },
            new object?[] { "Highest hour of the day", R(dailyPeak, 1), "gpm", $"{SanitaryDiurnal.Clock(peakHour)}–{SanitaryDiurnal.Clock(peakHour + 1)} × wet weather factor {wet:G3}" },
            new object?[] { "Pumping period", $"{SanitaryDiurnal.Clock(pumpStart)} to {SanitaryDiurnal.Clock(pumpStart + pumpHours)}", "", $"{work:G3} h plugged from {SanitaryDiurnal.Clock(s0)} + {setup:G3} h setup and {setup:G3} h teardown" },
            new object?[] { "Highest flow while pumping", R(winPeak, 1), "gpm", "" },
            new object?[] { "Volume pumped", R(winVol, 0), "gal", $"{R(pumpHours, 2)} h of pumping" },
        };
        if (conn is not null) plan.Add(["IDCM design peak of the connection", R(designPeak, 1), "gpm", "Qpeak + I/I of the sanitary design"]);
        plan.AddRange(
        [
            ["Bypass design flow", R(qDesign, 0), "gpm", $"{basisText}{(sf > 1 ? $" × safety factor {sf:G3}" : "")}"],
            ["Pumps", pumpCap is not null ? $"{duty} duty + {standby} standby" : $"1 duty + {standby} standby", "", $"{onePump:F0} gpm each"],
            ["Discharge line", $"{dL:G3} in., {lineLen:F0} ft", "", $"V = {vLine:F2} ft/s; C = {c:G3}"],
            ["Static head", R(stat, 2), "ft", statIn is not null ? "entered" : $"{dischargeName} rim + {riser:G3} ft riser ({disHigh:F2}) − pump-down level at {conn!.From} ({suctWs:F2})"],
            ["Friction, discharge line", R(hfLine, 2), "ft", "Hazen-Williams"],
            ["Fittings, discharge", R(tdh - stat - hfLine - hfSuct, 2), "ft", $"K = {kFit:G3}"],
            ["Suction losses", R(hfSuct, 2), "ft", $"{suctionLen:G3} ft of {dS:G3} in. + entrance K 1.0"],
            ["Total dynamic head", R(tdh, 1), "ft", ""],
            ["Water and brake horsepower", $"{whp:F1} / {bhp:F1}", "hp", $"Q·TDH/3960, efficiency {eff:G2}" + (double.IsFinite(motor) ? $"; {motor:G3} hp drive per pump" : "")],
            ["Suction lift", R(lift, 2), "ft", liftIn is not null ? "entered" : $"pump at the {conn!.From} rim ({pumpEl:F2}) − pump-down level"],
            ["NPSH available", R(npshA, 1), "ft", $"atmospheric {hAtm:F1} ft at {elev:F0} ft − lift − suction losses − vapor {hVap:F2} ft ({tempF:G3} °F)"],
            ["Suction lift at which NPSHa = 0", R(maxPractical, 1), "ft", "practical lifts are several feet less; see the pump's NPSH curve"],
        ]);
        if (minutesToOverflow is { } mo2) plan.Add(["Time to overflow if pumping stops", R(mo2, 0), "min", $"at the highest flow while pumping; response time {respMin:G3} min"]);

        var tables = new List<ToolTable>
        {
            new("Bypass pumping plan", ["Item", "Value", "Unit", "Basis"], plan,
                [$"Pattern: {patternName}. Flow = (average sanitary flow × hourly multiplier + infiltration) × wet weather factor.",
                 "Bypass specifications usually also require the plugs, pumps and line to be tested before the plug is set, 24-hour attendance or alarms while pumping, and the discharge to enter the receiving manhole without surcharging it."]),
            new("Hourly flow at the connection", ["Hour", "Multiplier", "Flow (gpm)", "Flow (cfs)", "Work"], hourRows),
            new("Discharge line sizes", ["D (in)", "V (ft/s)", "Friction (ft)", "Fittings (ft)", "TDH less suction (ft)", "Note"], lineRows,
                [$"At {qDesign:F0} gpm over {lineLen:F0} ft, C = {c:G3}, fittings K = {kFit:G3}. The smallest size at or under {vMax:G3} ft/s is selected."]),
        };
        if (storageRows.Count > 0)
            tables.Add(new("Storage upstream if the bypass stops", ["Level", "Elevation", "Storage (gal)", "Minutes at the highest pumping flow", "Minutes at the design flow"], storageRows,
                ["Storage = the volume of the pipes upstream of the suction manhole between the normal-depth water surface of the pumping-period flow and the level, plus the manholes (including the suction manhole) from their inverts. The plugged connection holds nothing. Time = storage / inflow."]));
        var bestRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Entered", SanitaryDiurnal.Clock(s0), R(winPeak, 1), R(winVol, 0) },
            new object?[] { "Lowest flow", SanitaryDiurnal.Clock(bestStart), R(WindowPeak(bestStart - setup, pumpHours), 1), R(WindowVol(bestStart - setup, pumpHours), 0) },
        };
        tables.Add(new("Work window", ["Window", "Plug at", "Highest flow while pumping (gpm)", "Volume pumped (gal)"], bestRows,
            [$"Searched every 15 min over the day for the {work:G3} h work and {setup:G3} h setup on each side; lowest highest-flow first, then lowest volume."]));

        var charts = new List<ToolChart>
        {
            new("Flow at the connection", "Hour of day", "Q (gpm)",
            [
                new ChartSeries("Hourly flow", Enumerable.Range(0, 24).SelectMany(h => new[] { ((double)h, Flow(h)), (h + 1.0, Flow(h)) }).ToList()),
                new ChartSeries("Pumping period", WindowPoints(pumpStart, pumpHours, qDesign), Dashed: true),
            ],
            [new ChartLine("Bypass design flow", qDesign)] ),
        };

        var summary = new List<string>
        {
            $"Bypass {(conn is not null ? $"{conn.Name} from {conn.From} to {dischargeName}" : "the tie-in")}: {duty} duty + {standby} standby pump(s), {onePump:F0} gpm each, at {tdh:F1} ft TDH ({bhp:F1} bhp at {eff:G2}); {dL:G3} in. discharge line, {lineLen:F0} ft.",
            $"Pump for {pumpHours:G3} h ({SanitaryDiurnal.Clock(pumpStart)}–{SanitaryDiurnal.Clock(pumpStart + pumpHours)}): {winVol:F0} gal, highest flow {winPeak:F0} gpm; the day's highest hour is {dailyPeak:F0} gpm at {SanitaryDiurnal.Clock(peakHour)}. Design flow {qDesign:F0} gpm ({basisText}).",
            $"Lowest-flow window: plug at {SanitaryDiurnal.Clock(bestStart)}.",
        };
        if (minutesToOverflow is { } mo3) summary.Add($"If the pumps stop, the sewer upstream overflows in about {mo3:F0} min at the highest pumping flow.");
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };

        static bool InWindow(double h, double from, double hours) { double d = ((h - from) % 24 + 24) % 24; return d < hours; }
        static List<(double, double)> WindowPoints(double from, double hours, double y)
        {
            double a = ((from % 24) + 24) % 24, b = a + hours;
            return b <= 24 ? [(a, y), (b, y)] : [(a, y), (24, y)];
        }
    }

    /// <summary>Vapor pressure head of water (ft) at <paramref name="tempF"/> °F: Antoine equation (1–100 °C) over the specific weight at that temperature (≈ 62.4 lb/ft³).</summary>
    public static double VaporHead(double tempF)
    {
        double tc = (tempF - 32) / 1.8;
        double mmHg = Math.Pow(10, 8.07131 - 1730.63 / (233.426 + tc));
        double psf = mmHg * 2.78450;   // 1 mmHg = 2.7845 lb/ft²
        return psf / 62.4;
    }

    private static double R(double v, int d) => double.IsFinite(v) ? Math.Round(v, d) + 0.0 : double.NaN;
}
