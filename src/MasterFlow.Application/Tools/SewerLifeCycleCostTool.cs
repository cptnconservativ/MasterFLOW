using MasterFlow.Domain;
using MasterFlow.Hydraulics;

namespace MasterFlow.Application.Tools;

/// <summary>
/// Gravity sewer versus lift station and force main: life-cycle cost over the design life. Capital, replacement at the end
/// of each item's service life, annual O&amp;M, pumping energy (growing from the initial to the build-out average flow)
/// and the salvage value left at the end of the period are brought to present worth in constant dollars (NIST Handbook
/// 135 factors) and compared, with the equivalent annual cost, break-even values and the sensitivity to the discount rate
/// and the power price. The engineering economic analysis and power costs Rapid City IDCM §3.13.3 2g–h asks for.
/// </summary>
public sealed class SewerLifeCycleCostTool : IDesignTool
{
    public string Id => "sewer-life-cycle-cost";
    public string Name => "Gravity sewer vs lift station life-cycle cost";
    public string Description => "Life-cycle cost of a gravity sewer route against a lift station and force main over the design life (IDCM §3.12.1 75 years, §3.13.3 25-year mechanical life): capital, replacements, O&M, pumping power from the force main system head as the flow grows to build-out, and salvage, as present worth and equivalent annual cost, with break-even capital and power price and sensitivity to the discount rate.";
    public bool UsesNetwork => false;

    private const double KwPerHp = 0.7457;

    /// <summary>One cost item of an alternative.</summary>
    public sealed record Item(string Name, string Unit, double Qty, double UnitCost, double Life, double AnnualPerUnit)
    {
        public double Capital => Qty * UnitCost;
        public double Annual => Qty * AnnualPerUnit;
    }

    /// <summary>Single present value factor 1/(1+d)^t (NIST Handbook 135 SPV).</summary>
    public static double Spv(double d, double t) => Math.Pow(1 + d, -t);

    /// <summary>Uniform present value factor for N years, Σ 1/(1+d)^t = ((1+d)^N − 1)/(d(1+d)^N); N when d = 0.</summary>
    public static double Upv(double d, int n) => Math.Abs(d) < 1e-12 ? n : (Math.Pow(1 + d, n) - 1) / (d * Math.Pow(1 + d, n));

    /// <summary>Capital recovery factor d(1+d)^N/((1+d)^N − 1) = 1/UPV: present worth to equivalent uniform annual cost.</summary>
    public static double Crf(double d, int n) => 1 / Upv(d, n);

    /// <summary>Years at which an item of service life <paramref name="life"/> is replaced within an <paramref name="n"/>-year period (none at year N itself).</summary>
    public static IEnumerable<int> ReplacementYears(double life, int n)
    {
        if (!(life > 0) || life >= n) yield break;
        for (int k = 1; k * life < n - 1e-9; k++) yield return (int)Math.Round(k * life);
    }

    /// <summary>Straight-line salvage fraction at the end of the period: the unused share of the last installation's life.</summary>
    public static double SalvageFraction(double life, int n)
    {
        if (!(life > 0)) return 0;
        double age = n - (ReplacementYears(life, n).LastOrDefault() is var last && last > 0 ? last : 0);
        return Math.Max(0, (life - age) / life);
    }

    /// <summary>Average flow (gpm) in year <paramref name="year"/>: linear from the initial in year 1 to the design average at build-out, then constant.</summary>
    public static double FlowInYear(int year, double qInit, double qDesign, int buildOut)
        => buildOut <= 1 || year >= buildOut ? qDesign : qInit + (qDesign - qInit) * (year - 1) / (buildOut - 1.0);

    /// <summary>Annual pumping energy (kWh) for an average flow <paramref name="qAvg"/> lifted at TDH <paramref name="tdh"/> (ft) with wire-to-water efficiency η: Q·H/(3960·η)·0.7457·8760.</summary>
    public static double AnnualKwh(double qAvg, double tdh, double eta) => qAvg * tdh / (3960 * eta) * KwPerHp * 8760;

    public string SampleInput(DesignToolContext context) => """
        # Gravity sewer vs lift station life-cycle cost. Constant (today's) dollars; U.S. units (gpm, ft, in.).
        # EXAMPLE COSTS ONLY: use your own unit prices, e.g. from the cost estimate tool or recent bids.
        analysis period = 75        # years (IDCM §3.12.1 design life of sewer hardware)
        discount rate = 3.0         # % per year, real (net of inflation); use your agency's rate
        energy escalation = 0       # % per year above general inflation
        power price = 0.11          # $/kWh
        demand charge = 0           # $/kW-month on the running pump(s); 0 if billed by energy only
        price index from =          # optional: escalate all unit costs by a cost index (e.g. ENR CCI then and now)
        price index to =
        salvage = straight line     # straight line or none

        # Pumping (lift station alternative). TDH from the force main, or give "TDH" directly.
        initial average flow = 60   # gpm in year 1
        design average flow = 120   # gpm at build-out
        build-out year = 20         # flow grows linearly to the design average by this year
        pump rate = 330             # gpm of the duty pump (e.g. from the lift station tool)
        static head = 42            # ft, discharge (or force main high point) minus the wet well average level
        force main length = 2600
        force main diameter = 6     # in. inside
        hazen C = 120
        minor K = 6.5
        efficiency = 0.55           # wire-to-water (pump × motor × drive)
        pumps running = 1           # pumps that run at once (for the demand charge)

        # Item	Unit	Quantity	Unit cost ($)	Life (yr; blank = whole period)	O&M ($ per unit per yr)
        [Gravity items]
        Item	Unit	Quantity	Unit cost	Life	O&M
        Deep gravity sewer 15 in PVC, 18-26 ft cut	LF	3200	310	100	1.20
        Manholes 60 in, deep	each	9	18500	75	150
        Extra excavation and shoring	LS	1	145000
        Easement and restoration	LS	1	60000

        [Lift station items]
        Item	Unit	Quantity	Unit cost	Life	O&M
        Shallow gravity sewer 10 in PVC	LF	1100	120	100	1.20
        Manholes 48 in	each	4	7500	75	150
        Wet well and valve vault	LS	1	210000	75
        Pumps, motors and rails	each	2	32000	25
        Controls, SCADA and electrical	LS	1	95000	15
        Standby generator	LS	1	85000	25	2500
        Force main 6 in PVC C900	LF	2600	65	75	0.50
        Air release valves and vaults	each	2	12000	25	400
        Station operation (labor, inspection, cleaning)	LS	1	0		18000
        """;

    public ToolResult Run(string input, DesignToolContext context)
    {
        const string title = "Gravity sewer vs lift station life-cycle cost";
        if (context.Units == UnitSystem.SI)
            return ToolResult.Failed(title, CalcMessage.Error("E-LC-001", "The life-cycle cost tool works in U.S. units (gpm, ft, in.); switch the project to U.S. units."));
        var p = ToolInput.Parse(input);
        var msgs = new List<CalcMessage>();
        int n = (int)Math.Round(p.Number("analysis period", 75));
        double d = p.Number("discount rate", 3.0) / 100, e = p.Number("energy escalation", 0) / 100;
        double price = p.Number("power price", 0.11), demand = p.Number("demand charge", 0);
        double? idxFrom = p.OptionalNumber("price index from"), idxTo = p.OptionalNumber("price index to");
        string salvageMode = (p.Text("salvage") ?? "straight line").Trim().ToLowerInvariant();
        bool salvage = !salvageMode.StartsWith("none") && !salvageMode.StartsWith("no");
        double qInit = p.Number("initial average flow", double.NaN), qDes = p.Number("design average flow", double.NaN);
        if (double.IsNaN(qInit) && !double.IsNaN(qDes)) qInit = qDes;
        if (double.IsNaN(qDes) && !double.IsNaN(qInit)) qDes = qInit;
        int buildOut = (int)Math.Round(p.Number("build-out year", 1));
        double? tdhGiven = p.OptionalNumber("TDH"), qPump = p.OptionalNumber("pump rate");
        double stat = p.Number("static head", double.NaN), fmL = p.Number("force main length", 0), fmD = p.Number("force main diameter", double.NaN);
        double c = p.Number("hazen C", 120), k = p.Number("minor K", 0), eta = p.Number("efficiency", 0.55);
        int running = (int)Math.Round(p.Number("pumps running", 1));

        if (n < 1 || n > 200) msgs.Add(CalcMessage.Error("E-LC-002", $"The analysis period must be 1 to 200 years (got {n})."));
        if (!(d > -0.99 && d < 0.5)) msgs.Add(CalcMessage.Error("E-LC-002", "The discount rate must be between −99 % and 50 %."));
        if (!(e > -0.99 && e < 0.5)) msgs.Add(CalcMessage.Error("E-LC-002", "The energy escalation must be between −99 % and 50 %."));
        if (!(price >= 0) || !(demand >= 0)) msgs.Add(CalcMessage.Error("E-LC-002", "Power price and demand charge must be zero or more."));
        if ((idxFrom is null) != (idxTo is null)) msgs.Add(CalcMessage.Error("E-LC-003", "Give both price index from and price index to, or neither."));
        else if (idxFrom is { } f0 && idxTo is { } t0 && !(f0 > 0 && t0 > 0)) msgs.Add(CalcMessage.Error("E-LC-003", "Price indexes must be more than zero."));
        double esc = idxFrom is { } fi && idxTo is { } ti && fi > 0 && ti > 0 ? ti / fi : 1;
        if (double.IsNaN(qDes)) msgs.Add(CalcMessage.Error("E-LC-004", "Give the pumped flow (initial average flow and design average flow, gpm)."));
        else if (!(qInit > 0 && qDes > 0)) msgs.Add(CalcMessage.Error("E-LC-004", "Average flows must be above zero."));
        if (buildOut < 1) msgs.Add(CalcMessage.Error("E-LC-004", "The build-out year must be 1 or more."));
        if (!(eta > 0 && eta <= 1)) msgs.Add(CalcMessage.Error("E-LC-005", "Efficiency must be above 0 and at most 1 (wire-to-water)."));
        if (running < 1) msgs.Add(CalcMessage.Error("E-LC-005", "pumps running must be 1 or more."));
        if (tdhGiven is null)
        {
            if (double.IsNaN(stat)) msgs.Add(CalcMessage.Error("E-LC-005", "Give TDH (ft), or the static head with the force main (length, diameter, C) and the pump rate."));
            else if (fmL > 0 && (double.IsNaN(fmD) || !(fmD > 0) || !(c > 0) || qPump is not > 0 || k < 0))
                msgs.Add(CalcMessage.Error("E-LC-005", "The force main needs a diameter (in.), Hazen C and the pump rate (gpm) above zero, and K zero or more."));
        }
        else if (!(tdhGiven >= 0)) msgs.Add(CalcMessage.Error("E-LC-005", "TDH must be zero or more."));

        List<Item> ReadItems(string section)
        {
            var list = new List<Item>();
            var head = p.Header(section);
            int Find(string w, int dflt) { int i = head.ToList().FindIndex(h => h.Trim().StartsWith(w, StringComparison.OrdinalIgnoreCase)); return i >= 0 ? i : dflt; }
            int cItem = Find("item", 0), cUnit = Find("unit", 1), cQty = Find("quant", 2), cCost = Find("unit cost", 3), cLife = Find("life", 4), cOm = Find("o&m", 5);
            if (cCost == cUnit) cCost = 3;
            foreach (var r in p.Rows(section))
            {
                string S(int i) => i < r.Length ? r[i].Trim() : "";
                double? Num(int i, string what)
                {
                    if (S(i).Length == 0) return null;
                    if (ToolInput.TryNumber(S(i), out var v)) return v;
                    msgs.Add(CalcMessage.Error("E-LC-006", $"[{section}] '{S(0)}': {what} '{S(i)}' is not a number.")); return double.NaN;
                }
                string name = S(cItem);
                if (name.Length == 0) continue;
                double qty = Num(cQty, "quantity") ?? 1, cost = Num(cCost, "unit cost") ?? 0, life = Num(cLife, "life") ?? double.PositiveInfinity, om = Num(cOm, "O&M") ?? 0;
                if (qty < 0 || cost < 0 || om < 0 || !(life > 0)) msgs.Add(CalcMessage.Error("E-LC-006", $"[{section}] '{name}': quantity, unit cost and O&M must be zero or more and life above zero."));
                list.Add(new Item(name, S(cUnit), qty, cost * esc, life, om * esc));
            }
            return list;
        }
        var grav = ReadItems("Gravity items");
        var lift = ReadItems("Lift station items");
        // A lump sum from the cost estimate tool (or elsewhere) can stand in for an item table.
        if (p.OptionalNumber("gravity capital") is { } gc) grav.Add(new Item("Gravity capital (lump sum)", "LS", 1, gc * esc, double.PositiveInfinity, 0));
        if (p.OptionalNumber("lift station capital") is { } lc) lift.Add(new Item("Lift station capital (lump sum)", "LS", 1, lc * esc, double.PositiveInfinity, 0));
        if (grav.Count == 0) msgs.Add(CalcMessage.Error("E-LC-007", "Give the gravity alternative's costs under [Gravity items] (or gravity capital = …)."));
        if (lift.Count == 0) msgs.Add(CalcMessage.Error("E-LC-007", "Give the lift station alternative's costs under [Lift station items] (or lift station capital = …)."));
        msgs.InsertRange(0, p.Errors);
        if (msgs.Any(m => m.Severity == MessageSeverity.Error)) return new(title, [], [], msgs);

        // ---- pumping head and energy
        double tdh = tdhGiven ?? stat + (fmL > 0 ? SanitaryLiftStationTool.SystemHead(qPump!.Value, 0, fmL, fmD, c, k) : 0);
        double qRun = qPump ?? qDes;
        double kwRun = qRun * tdh / (3960 * eta) * KwPerHp;
        double Life(Item i) => double.IsPositiveInfinity(i.Life) ? n : i.Life;

        // ---- year-by-year cash flows (constant dollars) and present worth
        var gYear = new double[n + 1]; var lYear = new double[n + 1]; var energyYear = new double[n + 1]; var kwhYear = new double[n + 1];
        foreach (var i in grav) gYear[0] += i.Capital;
        foreach (var i in lift) lYear[0] += i.Capital;
        double PwItems(List<Item> items, double[] yearly, out double pwCap, out double pwRep, out double pwOm, out double pwSalv, List<IReadOnlyList<object?>> rows, string alt)
        {
            pwCap = pwRep = pwOm = pwSalv = 0;
            foreach (var i in items)
            {
                double rep = 0;
                foreach (int y in ReplacementYears(Life(i), n)) { rep += i.Capital * Spv(d, y); yearly[y] += i.Capital; }
                double om = i.Annual * Upv(d, n);
                for (int y = 1; y <= n; y++) yearly[y] += i.Annual;
                double sf = salvage ? SalvageFraction(Life(i), n) : 0;
                double sv = -sf * i.Capital * Spv(d, n);
                yearly[n] -= sf * i.Capital;
                pwCap += i.Capital; pwRep += rep; pwOm += om; pwSalv += sv;
                var reps = ReplacementYears(Life(i), n).ToList();
                rows.Add([alt, i.Name, i.Unit, R(i.Qty, 2), R(i.UnitCost, 2), R(i.Capital, 0), double.IsPositiveInfinity(i.Life) ? "period" : i.Life.ToString("G4"),
                    reps.Count == 0 ? "—" : string.Join(", ", reps), R(rep, 0), R(i.Annual, 0), R(om, 0), R(sv, 0), R(i.Capital + rep + om + sv, 0)]);
            }
            return pwCap + pwRep + pwOm + pwSalv;
        }
        var itemRows = new List<IReadOnlyList<object?>>();
        double pwG = PwItems(grav, gYear, out var gCap, out var gRep, out var gOm, out var gSalv, itemRows, "Gravity");
        double pwL0 = PwItems(lift, lYear, out var lCap, out var lRep, out var lOm, out var lSalv, itemRows, "Lift station");
        double pwEnergyPerDollar = 0, pwDemand = 0, kwhTotal = 0;   // PW of energy at $1/kWh, so the break-even price is linear
        for (int y = 1; y <= n; y++)
        {
            double q = FlowInYear(y, qInit, qDes, buildOut);
            double kwh = AnnualKwh(q, tdh, eta);
            double f = Math.Pow(1 + e, y) * Spv(d, y);
            kwhYear[y] = kwh; kwhTotal += kwh;
            pwEnergyPerDollar += kwh * f;
            double demandYr = demand * 12 * kwRun * running;
            pwDemand += demandYr * f;
            energyYear[y] = kwh * price * Math.Pow(1 + e, y) + demandYr * Math.Pow(1 + e, y);
            lYear[y] += energyYear[y];
        }
        double pwEnergy = pwEnergyPerDollar * price;
        double pwL = pwL0 + pwEnergy + pwDemand;
        double crf = Crf(d, n);
        double diff = pwL - pwG;
        string lower = diff > 0 ? "gravity" : "lift station";
        // Break-even: power price at which the two are equal; gravity capital change that makes them equal.
        double bePrice = pwEnergyPerDollar > 0 ? (pwG - pwL0 - pwDemand) / pwEnergyPerDollar : double.NaN;

        double Total(double dd, double priceMult)
        {
            double Alt(List<Item> items) => items.Sum(i => i.Capital + ReplacementYears(Life(i), n).Sum(y => i.Capital * Spv(dd, y)) + i.Annual * Upv(dd, n)
                - (salvage ? SalvageFraction(Life(i), n) * i.Capital * Spv(dd, n) : 0));
            double en = 0;
            for (int y = 1; y <= n; y++) en += (kwhYear[y] * price * priceMult + demand * 12 * kwRun * running) * Math.Pow(1 + e, y) * Spv(dd, y);
            return Alt(lift) + en - Alt(grav);
        }

        var summary = new List<string>
        {
            $"Present worth over {n} years at {d:P1} real: gravity ${pwG:N0}, lift station and force main ${pwL:N0}. The {lower} alternative is lower by ${Math.Abs(diff):N0} ({Math.Abs(diff) / Math.Max(pwG, pwL):P0}).",
            $"Equivalent annual cost: gravity ${pwG * crf:N0}/yr, lift station ${pwL * crf:N0}/yr (capital recovery factor {crf:F5}).",
            $"Pumping: TDH {tdh:F1} ft at {qRun:F0} gpm, {kwRun:F1} kW per running pump; {kwhYear[1]:N0} kWh in year 1 rising to {kwhYear[n]:N0} kWh/yr; power present worth ${pwEnergy + pwDemand:N0}.",
            !(bePrice > 0)
                ? $"Gravity is lower even with free power: the lift station's capital, replacements, O&M and demand charges alone (${pwL0 + pwDemand:N0}) exceed the gravity present worth."
                : diff > 0
                    ? $"Break-even power price ${bePrice:F3}/kWh (now ${price:F3}); the gravity capital could rise by ${diff:N0} before the lift station is cheaper."
                    : $"Break-even power price ${bePrice:F3}/kWh (now ${price:F3}); the gravity capital would have to fall by ${-diff:N0} to match the lift station.",
        };

        msgs.Add(CalcMessage.Info("I-LC-001", "Rapid City IDCM §3.13.3 2g–h asks the lift station design report for power costs, equipment replacement and an engineering economic analysis; §3.12.1 sets a 75-year design life for sewer hardware and §3.13.3 1) 25 years for mechanical components."));
        msgs.Add(CalcMessage.Info("I-LC-002", $"Constant dollars at a real discount rate (NIST Handbook 135): PW = capital + Σ replacements/(1+d)^t + O&M·UPV + Σ energy·(1+e)^t/(1+d)^t − salvage/(1+d)^N. Replacements at each item's life before year {n}; {(salvage ? "straight-line salvage of the remaining life at year " + n : "no salvage")}."));
        if (esc != 1) msgs.Add(CalcMessage.Info("I-LC-003", $"All unit costs escalated by {idxTo:G6} / {idxFrom:G6} = {esc:F4}."));
        if (n != 75) msgs.Add(CalcMessage.Warning("W-LC-001", $"The analysis period is {n} years; IDCM §3.12.1 sets a 75-year design life for sewer collection hardware."));
        static bool Mechanical(string name) =>
            System.Text.RegularExpressions.Regex.IsMatch(name, "pump|motor|generator|control|valve|electric|scada|mechanical|drive", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && !System.Text.RegularExpressions.Regex.IsMatch(name, "well|vault|structure|building|pipe|force main|sewer", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var i in lift.Where(i => i.Capital > 0 && i.Life > 25 && double.IsFinite(i.Life) && Mechanical(i.Name)))
            msgs.Add(CalcMessage.Warning("W-LC-002", $"'{i.Name}' has a {i.Life:G4}-year life; IDCM §3.13.3 1) sets a 25-year design life goal for mechanical components."));
        if (!lift.Any(i => i.Capital > 0 && double.IsFinite(i.Life) && i.Life < n)) msgs.Add(CalcMessage.Warning("W-LC-003", "No lift station item is replaced within the period; pumps, controls and generators normally are (25 years or less)."));
        if (lift.Sum(i => i.Annual) <= 0) msgs.Add(CalcMessage.Warning("W-LC-004", "The lift station has no annual O&M; operator visits, cleaning and inspections usually cost more than the gravity sewer's."));
        if (buildOut > n) msgs.Add(CalcMessage.Warning("W-LC-005", $"Build-out (year {buildOut}) is after the analysis period ({n} years)."));
        if (Math.Abs(diff) < 0.1 * Math.Max(pwG, pwL)) msgs.Add(CalcMessage.Info("I-LC-004", "The alternatives are within 10 % of each other: at planning-level costs treat them as equal and decide on reliability, odor, risk and operator preference."));

        // ---- tables
        var pwRows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "Capital", R(gCap, 0), R(lCap, 0) },
            new object?[] { "Replacements", R(gRep, 0), R(lRep, 0) },
            new object?[] { "O&M", R(gOm, 0), R(lOm, 0) },
            new object?[] { "Pumping energy", 0.0, R(pwEnergy, 0) },
            new object?[] { "Demand charge", 0.0, R(pwDemand, 0) },
            new object?[] { "Salvage (credit)", R(gSalv, 0), R(lSalv, 0) },
            new object?[] { "Total present worth", R(pwG, 0), R(pwL, 0) },
            new object?[] { "Equivalent annual cost", R(pwG * crf, 0), R(pwL * crf, 0) },
        };
        var sensRows = new List<IReadOnlyList<object?>>();
        foreach (double dd in new[] { 0.01, 0.02, 0.03, 0.05, 0.07 }.Append(d).Distinct().OrderBy(x => x))
            sensRows.Add([R(dd * 100, 2), R(Total(dd, 0.5), 0), R(Total(dd, 1), 0), R(Total(dd, 1.5), 0)]);
        var cashRows = new List<IReadOnlyList<object?>>();
        var cumG = new List<(double, double)>(); var cumL = new List<(double, double)>();
        double sg = 0, sl = 0;
        for (int y = 0; y <= n; y++)
        {
            sg += gYear[y] * Spv(d, y); sl += lYear[y] * Spv(d, y);
            cumG.Add((y, R(sg, 0))); cumL.Add((y, R(sl, 0)));
            if (y <= 5 || y % 5 == 0 || y == n || gYear[y] > 0 && y > 0 && grav.Any(i => ReplacementYears(Life(i), n).Contains(y)) || lift.Any(i => ReplacementYears(Life(i), n).Contains(y)))
                cashRows.Add([y, y == 0 ? 0.0 : R(FlowInYear(y, qInit, qDes, buildOut), 1), R(kwhYear[y], 0), R(gYear[y], 0), R(lYear[y], 0), R(sg, 0), R(sl, 0)]);
        }
        var tables = new List<ToolTable>
        {
            new("Present worth by component", ["Component", "Gravity ($)", "Lift station and force main ($)"], pwRows,
                [$"Real discount rate {d:P2}, {n}-year period; energy escalation {e:P1}/yr above inflation; power ${price:F3}/kWh" + (demand > 0 ? $", demand ${demand:F2}/kW-month." : ".")]),
            new("Cost items", ["Alternative", "Item", "Unit", "Quantity", "Unit cost ($)", "Capital ($)", "Life (yr)", "Replaced in year", "PW replacements ($)", "O&M ($/yr)", "PW O&M ($)", "PW salvage ($)", "PW total ($)"], itemRows,
                [$"Uniform present value factor UPV({d:P2}, {n}) = {Upv(d, n):F4}; salvage = unused share of the last installation's life × its cost, at year {n}."]),
            new("Sensitivity: lift station minus gravity present worth ($)", ["Discount rate (%)", "Power price × 0.5", "Power price × 1.0", "Power price × 1.5"], sensRows,
                ["Positive: gravity is cheaper. Negative: the lift station is cheaper."]),
            new("Cash flow (constant dollars) and cumulative present worth", ["Year", "Average flow (gpm)", "Energy (kWh)", "Gravity ($)", "Lift station ($)", "Cumulative PW gravity ($)", "Cumulative PW lift station ($)"], cashRows,
                ["Years 0–5, every fifth year, replacement years and the last year are listed; salvage is credited in the last year."]),
        };
        var charts = new List<ToolChart>
        {
            new("Cumulative present worth", "Year", "Present worth ($)", [new("Gravity", cumG), new("Lift station and force main", cumL, Dashed: true)]),
        };
        return new ToolResult(title, summary, tables, msgs) { Charts = charts };
    }

    private static double R(double v, int dgt) => double.IsFinite(v) ? Math.Round(v, dgt) + 0.0 : double.NaN;
}
