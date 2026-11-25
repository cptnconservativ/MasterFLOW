# Stormwater pump stations (HEC-24)

Tool id `pump-station` (free, U.S. units). Checks a stormwater pump station (an underpass or depressed roadway) by FHWA
HEC-24, *Highway Stormwater Pump Station Design* (2001): pump operating points on the system curve, routing of the design
inflow hydrograph through the wet well and storage with the pumps' start and stop levels, peak stage, and pump cycling.

## Input

Keys: `discharge elevation`, `force main length`, `force main diameter`, `hazen C` (120), `minor K` (sum of loss
coefficients on the force main velocity), `min cycle time` (minutes, default 10 = 6 starts an hour), `time step`
(minutes, 0.5).

Tables: `[Pump curve]` head (ft) and Q (cfs) for one pump; `[Pumps]` name, start and stop elevation in start order
(identical pumps, common force main); `[Storage]` elevation and volume (ft³) of the wet well and any storage unit;
`[Inflow]` time (min) and Q (cfs).

## Method

- System curve: H = discharge elevation − wet-well W.S. + Hazen-Williams 4.73·L·Q^1.852/(C^1.852·D^4.87) + K·V²/2g.
- n identical pumps in parallel: each pump's head at Q/n equals the system head at Q (bisection).
- Mass-curve routing: storage change = (inflow − pumped)·Δt; a pump starts when the W.S. reaches its start level and
  stops at its stop level; the pumped flow follows the W.S. through the system curve.
- Cycling: starts, the shortest time between starts, and the volume between each pump's start and stop against
  V = 15·T·Q (ft³, T minutes, Q cfs), HEC-24's minimum for the worst case of inflow equal to half the pump rate. Q is the
  flow the pump adds when it starts.
- Force main velocity: information below about 2 ft/s (settling), warning above 8 ft/s.
- Charts: inflow and pumped flow, wet-well W.S. with the start and stop levels, pump curves (1 to n pumps) with the
  system curve.

## Decisions

- **D-460** Identical pumps on one common force main. Different pumps or separate force mains need a run per
  combination.
- **D-461** The cycle-volume rule is applied to the flow each pump adds at its start level, which is the flow that
  must be stored while the pump is off.
- **D-462** No electrical, sump geometry (HI/ANSI 9.8 submergence) or surge analysis: those are left to the pump
  supplier and the structural design.
- **D-463** HEC-24's worked examples were not available as text in this environment; the tests check Hazen-Williams,
  the operating point (pump head = system head), volume conservation in the routing and the cycling rule.

## Messages

| Code | Meaning |
|---|---|
| E-PS-001 … 009 | SI project; missing force main values; bad time step; pump, curve, storage or inflow tables invalid |
| W-PS-001 | Force main velocity above 8 ft/s |
| W-PS-002 | Storage table full |
| W-PS-003 | Start–stop volume below 15·T·Q |
| W-PS-004 | Shortest cycle below the minimum |
| I-PS-001 | Force main velocity below 2 ft/s |
