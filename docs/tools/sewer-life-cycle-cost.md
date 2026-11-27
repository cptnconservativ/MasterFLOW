# Gravity sewer vs lift station life-cycle cost

Tool id `sewer-life-cycle-cost` (free, U.S. units). Compares a gravity sewer route (often deeper or longer) with a
lift station and force main over the design life. It gives the engineering economic analysis and power costs that
Rapid City IDCM §3.13.3 2g–h asks the lift station design report for. The design life is 75 years for sewer hardware
(IDCM §3.12.1) and 25 years for lift station mechanical components (§3.13.3 1).

## Input

Economics: `analysis period` (75 yr), `discount rate` (% real, 3.0), `energy escalation` (% per year above
inflation, 0), `power price` ($/kWh), `demand charge` ($/kW-month, 0), `price index from` / `price index to`
(optional cost-index escalation of every unit cost and O&M rate, as in the cost estimate tool), `salvage`
(`straight line` or `none`).

Pumping: `initial average flow` and `design average flow` (gpm), `build-out year` (linear growth to the design average,
then constant), and either `TDH` (ft) or `static head` (ft) with `force main length`, `force main diameter` (in.),
`hazen C`, `minor K` and `pump rate` (gpm). Also `efficiency` (wire-to-water, 0.55) and `pumps running` (for the demand
charge). The values can be copied from the lift station tool.

Costs: `[Gravity items]` and `[Lift station items]`, each with columns Item, Unit, Quantity, Unit cost ($), Life (yr;
blank = the whole period, never replaced) and O&M ($ per unit per year). A lump sum can stand in for a table:
`gravity capital = …` and `lift station capital = …`, for example the totals from the cost estimate tool.

## Method

All amounts are in constant (today's) dollars, discounted at a real rate. The factors are from NIST Handbook 135
(Fuller and Petersen, *Life-Cycle Costing Manual for the Federal Energy Management Program*, 1995 edition).

- SPV = 1/(1+d)^t for one-time amounts; UPV = ((1+d)^N − 1)/(d(1+d)^N) for uniform annual amounts (N when d = 0).
- Capital at year 0. Each item is replaced at every multiple of its life before year N, at its capital cost.
- Salvage is straight line: the unused share of the last installation's life times its cost, credited at year N.
  A 100-year pipe in a 75-year analysis keeps 25 %.
- O&M = Σ quantity × O&M rate × UPV.
- Energy in year t is Q_avg(t)·TDH/(3960·η) × 0.7457 kW/hp × 8,760 h. This holds whatever the pump rate, because the
  pump runs Q_avg/Q_pump of the time. The year's energy is priced at power price × (1+e)^t and discounted. The TDH is
  static + Hazen-Williams + K·V²/2g at the pump rate, using the lift station tool's `SystemHead`.
- Demand charge = rate × 12 × kW of the running pumps, escalated like energy.
- Equivalent annual cost = PW × CRF, CRF = 1/UPV.
- Break-even power price: the price at which the two present worths are equal (PW is linear in the price). Break-even
  capital: the change in gravity capital that makes them equal.
- Sensitivity: lift station minus gravity PW at discount rates of 1, 2, 3, 5 and 7 % (and the rate given), with the
  power price at 0.5, 1.0 and 1.5 times.
- Chart: cumulative present worth of both alternatives against the year.

## Check by hand

Gravity: 1,000 LF at $1,000 (100-yr life), $2/LF·yr O&M. Lift station: $400,000 structure (75 yr), $100,000 pumps (25
yr), $5,000/yr O&M. Pumping: 100 gpm at 50 ft, η 0.5, $0.10/kWh. Over 75 yr at 3 %, UPV = 29.7018 and
SPV(75) = 0.10895.

- Gravity = 1,000,000 + 2,000 × 29.7018 − 0.25 × 1,000,000 × 0.10895 = $1,032,167.
- Energy = 100 × 50/1,980 × 0.7457 × 8,760 = 16,496 kWh/yr. Its PW is 16,496 × 0.10 × 29.7018 = $48,996.
- Lift station = 500,000 + 100,000 × (0.47761 + 0.22811) + 5,000 × 29.7018 + 48,996 = $768,076.

`SewerLifeCycleCostTests.SmallCase_ByHand` checks these values and the break-even price.

## Decisions

- **D-770** Constant dollars and a real discount rate, defaulting to 3 %. The IDCM gives no rate, so the agency's or
  funding program's rate should be entered. Energy can escalate faster than inflation (`energy escalation`).
- **D-771** The analysis period is the IDCM 75-year design life. A mechanical item (pump, motor, generator, controls,
  valves) with a life over 25 years gets a warning (W-LC-002), from IDCM §3.13.3 1). Structures, pipes and vaults
  don't.
- **D-772** Straight-line salvage at year N, because a 100-year pipe still has value at 75 years. Choose `salvage =
  none` to leave it out.
- **D-773** Pumping energy follows the average flow, growing linearly to build-out, at one TDH: the duty point from
  the force main system curve. Variable-speed savings, wet well level swing and peak-hour tariffs are not modeled; a
  demand charge covers the peak kW.
- **D-774** Free tool. Costs are entered, or taken as lump sums from the cost estimate tool. The sample costs are
  examples only.

## Messages

| Code | Meaning |
|---|---|
| E-LC-001 | SI project |
| E-LC-002 | Analysis period, discount rate, escalation or power price out of range |
| E-LC-003 | Only one price index given, or one not above zero |
| E-LC-004 | Flows missing or not above zero; build-out year below 1 |
| E-LC-005 | TDH or force main inputs missing; efficiency not 0–1 |
| E-LC-006 | A cost item has a non-number or negative value |
| E-LC-007 | An alternative has no costs |
| W-LC-001 | Analysis period is not the IDCM 75 years |
| W-LC-002 | Mechanical item life over 25 years |
| W-LC-003 | No lift station item is replaced in the period |
| W-LC-004 | Lift station has no annual O&M |
| W-LC-005 | Build-out after the analysis period |
| I-LC-001 … 004 | IDCM report items; method; escalation used; alternatives within 10 % |
