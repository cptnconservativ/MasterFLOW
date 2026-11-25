# Flood risk and least total expected cost (HEC-17)

Tool id `floodplain-risk` (free). Risk-based design of a crossing by FHWA HEC-17, *Highways in the River Environment:
Floodplains, Extreme Events, Risk, and Resilience* (2nd ed., 2016). The floodway encroachment part of HEC-17 is in the
natural channel profile tool (`river-profile`, `floodway profile = 100-yr`).

## Input

| Key | Meaning |
|---|---|
| `service life` | Years (default 50) |
| `discount rate` | Percent per year, real (default 3) |
| `design T` | Return period for the risk statement (default 100) |

`[Alternatives]`: Name, Capital, Annual O&M. `[Damages]`: T (years), then the damage of each alternative for that event
(road and structure repair, traffic delay and detour costs, and added upstream flood damage), in the order of
`[Alternatives]`. Start with an event that does no damage.

## Method

- Risk of at least one exceedance in n years: R = 1 − (1 − 1/T)^n, tabulated for T = 2 to 500 years and lives of 10 to
  100 years.
- Expected annual damage: EAD = Σ ½(D1 + D2)(P1 − P2) between events, P = 1/T, plus D·P of the rarest event (damage held
  constant for rarer floods).
- Present worth factor (1 − (1 + i)^−n)/i; total expected cost = capital + PW × (EAD + annual O&M).
- The least total expected cost (LTEC) alternative is marked, and the damage–probability curves are charted.

## Check

The sample gives EAD = $37,050/yr for the 3-cell box (3,600 + 5,700 + 7,750 + 14,800 + 5,200), PW factor 25.730 at
3 % over 50 years, and LTEC for the 4-cell box. R(100 yr, 50 yr) = 39.5 %. The tests reproduce these.

## Decisions

- **D-444** Damage is taken as zero for floods more frequent than the first listed event, and constant beyond the
  rarest; the tool warns when the first event already does damage.
- **D-445** Costs are in today's dollars with a real discount rate; no escalation or nonstationary hydrology. For a
  climate sensitivity, run the damages with the future flows (for example from the climate stress test).

## Messages

| Code | Meaning |
|---|---|
| E-FR-001/002/003 | Service life, discount rate or design T out of range |
| E-FR-004/005/006/007 | Invalid alternative, event or damage rows; repeated return period |
| W-FR-001 | First event already does damage |
| I-FR-001 | No LTEC input |
