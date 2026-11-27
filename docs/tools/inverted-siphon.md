# Inverted siphon

Tool id `inverted-siphon` (free, U.S. units). Designs or checks a sanitary inverted siphon (depressed sewer) under a
channel, culvert or utility by ASCE MOP 60 (*Gravity Sanitary Sewer Design and Construction*) and the Ten States
Standards. The IDCM has no siphon criteria.

## Input

Keys: `minimum flow`, `average flow`, `peak flow`, `wet weather flow` (gpm; minimum defaults to average, wet weather to
peak); `available head` (ft), or `upstream water surface` (the highest allowed inlet chamber water surface at the
design flow) and `downstream water surface` (outlet chamber); `barrel length` (ft); `manning n` (0.013); `entrance K`
(0.5), `exit K` (1.0), `bend K` (0, sum per barrel); `min velocity` (3 ft/s); `min barrel diameter` (8 in.);
`weir length` (3 ft) and `weir coefficient` (3.0) of the inlet chamber side weirs; `incoming sewer diameter`,
`air jumper diameter`, `air jumper length` (default barrel length), `air ratio` (1.0), `max air loss` (0.5 in. of
water). Optional `[Barrels]` table, one diameter (in.) per row, to check given barrels instead of designing them.

## Method

- Each barrel runs full: hL = [(Ke + Kx + Kb)/2g + n²L/(1.486²·R^4/3)]·V², so Q = A·√(h/coef).
- Inlet chamber: barrel 1 is fed at the invert; each later barrel over a side weir, Q = Cw·Lw·(h − crest)^1.5, limited
  by the barrel's own full flow. h is the inlet water surface above the outlet water surface. The head for any total
  flow is found by bisection.
- Design (no `[Barrels]`): barrel 1 is the largest size, at least the minimum, that runs 3 ft/s at the average flow.
  If it can carry the peak alone at the available head, barrel 2 is the same size and joins above the peak.
  Otherwise barrel 2 joins at the average flow (its crest is the water surface at which barrel 1 carries the average)
  and is the smallest size that brings the capacity at the available head up to the peak. A wet-weather barrel joins
  at the peak when the wet weather flow is larger. Given barrels join at the average, peak and wet weather flows in
  order.
- Checks: two barrels at least; minimum size; 3 ft/s in barrel 1 at the average flow (Ten States); 3 ft/s in each daily
  barrel at the peak (MOP 60: each barrel should reach the cleansing velocity once a day); head at the design flow
  against the available head; n ≥ 0.013 (IDCM §3.12.6). Notes: velocity at the minimum flow (flushing), barrels that
  run only in wet weather, and the capacity with the largest barrel out.
- Air jumper: air flow = design water flow × air ratio; Darcy loss with Swamee-Jain f (smooth pipe), plus 1.5 velocity
  heads for entrance and exit, in inches of water (air 0.0752 lb/ft³). Default size half the incoming sewer, 6 in.
  minimum, rounded up.
- Output: barrels with capacity, joining flow and weir crest (height and elevation); flow split, velocities, head and
  inlet water surface from the minimum to the design flow; the air jumper; head and velocity charts.

Worked check (sample, 260 ft, K 1.9): 10 in. coef = 1.9/64.35 + 0.013² × 260/(1.486² × 0.2083^1.333) = 0.1906; at the
820 gpm average (1.827 cfs, 3.35 ft/s) h = 0.1906 × 3.35² = 2.14 ft, the barrel 2 crest. 12 in. coef 0.1559. At the
2,000 gpm peak barrel 2 runs 3.1 ft/s; the 8 in. wet-weather barrel joins above it. 3,000 gpm needs 2.88 ft of the
3.00 ft available.

## Decisions

- **D-745** Velocity: 3 ft/s at the average flow in barrel 1 (Ten States Standards, inverted siphons) and at the peak in
  each daily barrel (ASCE MOP 60). The minimum flow is reported but not required to reach 3 ft/s. Ten States clauses
  are cited by topic, as in D-713, because the 2014 text could not be checked in this environment.
- **D-746** Minimum barrel 8 in., the IDCM §3.12.4 minimum sewer size (Ten States allows 6 in.); change with `min
  barrel diameter`.
- **D-747** Staging by side weirs in the inlet chamber with a sharp/broad-crested weir coefficient of 3.0 and 3 ft
  weirs by default; outlet chambers are not modeled (stop logs or gates on every barrel are listed).
- **D-748** Air jumper: neither the IDCM nor the Ten States sizes it. The air demand defaults to the design water flow
  (the air the water displaces, a lower bound; headspace air dragged by the incoming sewer can be several times that),
  the size to half the incoming sewer, and the loss limit to 0.5 in. of water. All three are inputs, to be set from an
  odor or ventilation study.
- **D-749** Capacity with the largest barrel out is the remaining barrels at the full available head with weirs
  bypassed (a stop-log arrangement), as an information note, not a requirement.

## Messages

| Code | Meaning |
|---|---|
| E-IS-001 | SI project |
| E-IS-002 | A required value is missing |
| E-IS-003 | A value is out of range or flows out of order |
| E-IS-004 | `[Barrels]` row invalid |
| W-IS-001 | Fewer than two barrels |
| W-IS-002 | Barrel below the minimum diameter |
| W-IS-003 | Barrel 1 below 3 ft/s at the average flow |
| W-IS-004 | A daily barrel below 3 ft/s at the peak |
| W-IS-005 | Not enough head for the design flow |
| W-IS-006 | Air jumper loss above the limit |
| W-IS-007 | n below 0.013 |
| I-IS-001 … 005 | Minimum-flow velocity; wet-weather barrels; largest barrel out; air jumper; chamber layout |
