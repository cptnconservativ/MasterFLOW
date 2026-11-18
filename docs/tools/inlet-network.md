# Inlet capacity and bypass routing (HEC-22)

Every inlet in a storm network, on grade or in a sag, with its bypass carried to the next inlet downstream.
This is the network version of [gutter-inlets.md](gutter-inlets.md): that tool takes one gutter in a fixed
order; this one routes bypass through the network, adds composite (depressed) gutters, built-in grate
splash-over constants, combination inlets with a sweeper curb opening, the HEC-22 sag transition, and an
optional minor-storm run.

## Input

`key = value` street data (street class, pavement width, curb height, cross slope Sx, n, and optionally
`gutter width` W and `gutter slope` Sw for a composite gutter, `longitudinal slope` as a default S), then an
`[Inlets]` table. Columns are found by their header, so order is free and unused columns can be left out:

| Column | Meaning |
|---|---|
| Inlet | Structure name (matches the network to apply the result) |
| Type | `curb`, `grate` or `combination` |
| Location | `grade` or `sag` |
| Q, Q minor | Runoff reaching the gutter at this inlet, design and (optional) minor storm, cfs |
| S | Longitudinal slope on grade, ft/ft (must be > 0; a flat or adverse grade is a sag) |
| L, W | Opening length and width, ft. For a curb opening W is the local depression width |
| a | Local depression, in. |
| Grate | `P-50`, `P-50x100`, `P-30`, `curved vane`, `45 tilt bar`, `30 tilt bar`, `reticuline`, or a splash-over velocity Vo in fps |
| Area | Grate clear opening area, ft² (needed in a sag) |
| h | Curb-opening height, ft (needed in a sag) |
| Lc | Curb-opening length of a combination inlet when it is longer than the grate (sweeper upstream) |
| Clog | Clogging %, blank = IDCM Table 4-11 |
| Sx, n | Row overrides of the street values |
| Bypass to | Blank = next inlet downstream through the pipes; an inlet name; or `out` |

## Bypass routing

- Blank **Bypass to**: follow the outlet pipe of the inlet's structure downstream until a structure that is in
  the table is reached. Surface flow is assumed to follow the sewer; name the inlet where the street grade
  sends the water when it does not. No network or no inlet downstream: the bypass leaves the system and a
  warning gives the amount.
- Inlets are solved in topological order of the bypass graph, so several inlets can bypass to one. A loop
  is an error. Gutter Q at an inlet = its local Q + every bypass arriving.
- Sag inlets take all of their flow (no bypass); the ponded depth and spread are checked.
- Mass balance: local runoff = intercepted + bypass leaving the system, shown under each storm's table.
- **Apply to network** sets each inlet's Inflow to its intercepted design flow, and MinorInflow to its
  intercepted minor-storm flow when Q minor was given.

## Methods (U.S. units)

| Item | Equation |
|---|---|
| Gutter, uniform | IDCM Eq. 4-4-1 Q = (0.56/n) Sx^1.67 S^0.5 T^2.67 (exponents as printed, D-054) |
| Gutter, composite | HEC-22 Eq. 4-4: Q = Qs/(1 − Eo), Eo = 1/{1 + (Sw/Sx)/[(1 + (Sw/Sx)/(T/W − 1))^2.67 − 1]} (shared `GutterSection`) |
| Velocity | V = Q/A |
| Curb opening on grade | Se = Sx + S'w Eo, S'w = a/W with a = gutter depression + local depression (HEC-22 Eq. 4-24); LT = 0.6 Q^0.42 S^0.3 (1/(n Se))^0.6 (Eq. 4-22); E = 1 − (1 − L/LT)^1.8 (Eq. 4-23) |
| Grate on grade | Eo for the grate width; Rf = 1 − 0.09 (V − Vo) ≥ 0 (Eq. 4-18); Rs = 1/(1 + 0.15 V^1.8/(Sx L^2.3)) (Eq. 4-19); E = Rf Eo + Rs (1 − Eo) |
| Splash-over Vo | Vo = α + βL − γL² + ηL³ at the clogged length, constants from MHFD USDCM Vol. 1 Ch. 7 Table 7-6 (same as HEC-22 for the P-bar grates, vane and 45° tilt bar); beyond 4 ft, Vo(4) + 0.2 fps per foot |
| Combination on grade | Equal lengths: the grate alone (HEC-22 §4.4.4.4; IDCM §4.5.2). Lc > L: the curb opening upstream (length Lc − L) intercepts first, the grate takes the rest |
| Grate in sag | Weir 3.0 (L + 2W) d^1.5, orifice 0.67 A √(2gd); the lesser governs, which is exactly the change at d = 1.79 A/P. d = average depth over the grate |
| Curb opening in sag | Weir: depressed 2.3 (L + 1.8W) d^1.5 with d from the normal cross slope (L ≤ 12 ft), otherwise 3.0 L d^1.5. Orifice 0.67 h L √(2g(di − h/2)), di = depth at the lip. Weir while di ≤ h, orifice from 1.4h, linear between |
| Combination in sag | Weir flow (grate): grate alone. Orifice flow: grate orifice + curb orifice (HEC-22 Eq. 4-34) |
| Clogging | IDCM Table 4-11: grate 40 %, combination 40 % on grade (it is a grate there) and 33 % in a sag, curb 0 %. Shortens the grate and its open area (MHFD Eqs. 7-41/7-42); width unchanged; the curb opening of a combination is not clogged |

The second table for each storm, **hand-check values**, prints every intermediate quantity (T, V, Eo, Se, LT,
Vo, Rf, Rs, weir/orifice regime, depths) so each inlet can be checked by hand.

## Decisions

- Bypass default follows the pipes because the coordinates of gutter flow lines are not in the network;
  the explicit Bypass to column overrides it. (Requested: bypass carried to the next downstream inlet.)
- Clogging reduces length and area but not width (MHFD), so the composite-gutter Eo applies when the grate
  is as wide as the gutter. The older gutter-inlet tool also narrows a clogged sag grate; this tool does not.
- A grate narrower or wider than a composite gutter uses the uniform-section Eo formula for its width
  (HEC-22 gives no closed form); stated here as an approximation.
- The splash-over constants are printed in MHFD Table 7-6; the names P-50 = P-1-7/8, P-50x100 = P-1-7/8-4,
  P-30 = P-1-1/8 follow HEC-22. The 30° tilt bar and reticuline constants are MHFD's.

## Validation

`validation/hand_calcs_inlets.py` (output in `hand_calcs_inlets_output.txt`) computes four cases directly
from the equations; `tests/MasterFlow.IntegrationTests/InletNetworkToolTests.cs` checks the tool against them
to 0.001: a depressed curb opening → clogged P-50x100 grate → sag grate chain on a uniform gutter (bypass found
through the pipes), a curb opening on a composite gutter, a sag curb opening in weir, transition and orifice
flow, and a sag combination inlet in weir and orifice flow. The curb-opening case also equals the older
gutter-inlet tool's result. Edge cases: negative Q, flat and adverse grade, zero length, missing grate data,
unknown grate, loops, unknown targets, duplicate names, a gutter flatter than the pavement, SI projects, zero
flow and no network.
