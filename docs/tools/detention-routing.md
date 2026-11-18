# Detention pond routing (IDCM 2012 §4.10)

| Item | Method |
|---|---|
| Stage–storage, contours | `[Stage storage]` Elevation, Area. `storage method = conic` (default): frustum, V = h/3 (A₁ + A₂ + √(A₁A₂)); `average end area`: V = h (A₁ + A₂)/2 |
| Stage–storage, volume table | `[Stage storage]` Elevation, Volume (a header containing "vol" or "stor"; ac-ft when it says ac-ft, otherwise ft³), e.g. a Civil 3D stage–storage report. Linear between rows; the first row must be 0 |
| Stage–storage, basin geometry | `[Basin]` bottom elevation, bottom length, bottom width, side slope (H:V), top elevation. Exact prismoidal volume V = LWh + (L+W)zh² + 4/3·z²h³ |
| Orifice | Full: Q = C A √(2g h), h to the centroid. Below the crown: Q = (2/3) C √(2g) B h^1.5, B = A / opening height (circular: πD/4), which meets the full-orifice value within 6 % at the crown |
| Weir | Q = C L h^1.5 (default C 3.0) |
| V-notch weir | Q = C tan(θ/2) h^2.5, Size = θ in degrees (default C 2.5) |
| Riser (drop inlet) | Crest at Invert; circular (Size = D) or box (Size × Height). Least of weir control C·P·h^1.5 (P = perimeter, default C 3.0) and orifice control Co·A·√(2gh) over the opening (column Orifice C, default 0.6). With the outlet pipe below, the three controls of §4.10.5 (weir, orifice, pipe) are all evaluated and the least governs |
| Outlet pipe (barrel) | Limits the sum of the orifices, weirs, v-notches and risers. At and above the barrel's inlet crown: Q = A √(2g H / (1 + Ke + 29 n² L / R^4/3)), H = WSE − (outlet invert + ho), ho = (dc + D)/2, free outfall (HDS-5 outlet control). Below the inlet crown: Qcrown·(h/D)^1.5 (approximation). The rating is continuous and rises with stage, which storage-indication routing needs |
| Emergency spillway | Broad-crested trapezoidal weir, Q = C (L h^1.5 + 0.8 z h^2.5), Size = bottom width L, Height = side slope z (default C 2.6). Discharges beside the principal outlet (not limited by the barrel) |
| Inflow, rational | `inflow = rational` (default). Modified rational (§4.10.7): Q = C i(Td) A, i from Table 4-2 for each duration Td from tc to 180 min; trapezoid rising over tc, flat to Td, falling over tc. The duration giving the highest water level is critical |
| Inflow, hydrograph | `inflow = hydrograph` and `[Inflow hydrograph]`: Time (min), then one flow column (cfs) per storm, e.g. `2-yr (cfs)`, `100-yr (cfs)`. The year in the header links a column to `allowable Q<year>`; the 100-yr column drives the freeboard, drain and trickle checks. Resampled to the routing step by linear interpolation (warned if that changes the volume by more than 1 %); a table starting after t = 0 rises from 0 at t = 0 |
| Routing | Storage indication (modified Puls, level pool): 2S₂/Δt + O₂ = I₁ + I₂ + 2S₁/Δt − O₁, pond empty at t = 0, `time step` 0.1–10 min (default 1). Drain time is from the end of inflow to 0.1 % of the peak storage |

## Criteria checks (table "Criteria checks", one row each, OK / FAILS / NOT CHECKED)

| Check | Rule | IDCM |
|---|---|---|
| Peak outflow, 2-, 10-, 100-yr | ≤ `allowable Q2/Q10/Q100` (pre-development) | §4.10.3(2), §4.10.4 |
| Freeboard | top of berm ≥ 1 ft above the greater of the routed 100-yr WSE and the emergency spillway WSE at its design flow (`spillway design Q`, default the 100-yr peak inflow, i.e. principal outlet blocked) | §4.10.3(2), §4.10.4(4) |
| Drain time | ≤ 72 h after the 100-yr storm, by gravity through the principal outlets | §4.10.5 |
| Outlet pipe, riser | ≥ 18 in.; a riser needs a drawdown orifice (or v-notch) at the pond bottom | §4.10.5 |
| Side slopes | `side slope` (or the [Basin] side slope) 4H:1V or flatter | §4.10.4(2) |
| Bottom slopes | `longitudinal slope` ≥ 0.5 %, `cross slope` ≥ 2 % | §4.10.4(1) |
| Trickle channel | `trickle capacity` ≥ 2 % of the 100-yr peak inflow (slope ≥ 0.2 % noted) | §4.10.4(3) |
| Spillway side slopes | z ≥ 4 for an excavated earth spillway (warning only; ignore for a structural spillway) | §4.10.6 |

Messages also note the inlet-protection flow (100-yr inflow with no storage, §4.10.4(5)) and the ARSD dam check (§4.10.3(1)).

## Output

Tables: routing results (peak inflow and outflow, peak WSE, storage used, time of peak, spillway flow, drain time,
peak reduction), criteria checks, stage–storage–discharge (every outlet's flow and the barrel capacity), outlet works,
and one routed hydrograph per storm (time, inflow, outflow, WSE, storage, spillway flow; about 60 rows, always
including the peak). Charts: inflow/outflow hydrograph per storm with the allowable line, water surface elevation for
all storms with the top of berm and spillway crest, stage–discharge (with the barrel capacity) and stage–storage.
Save as HTML (charts as SVG), PDF (landscape, charts drawn natively) or Excel (one sheet per table).

Errors stop the run: a pond that fills above the top stage is reported as overtopping, never extrapolated. The tool
works in U.S. units only (Table 4-2, weir coefficients and the criteria are U.S. values); an SI project gets an error.

## Decisions

- D-055: the modified rational method was chosen because §4.10.7 names it and it needs only Table 4-2 and the
  rational inputs MasterFlow already has. HEC-HMS remains the tool for basins where the IDCM requires it (§4.2.1).
- D-062: entered hydrographs are the hook for other hydrology (the rational-method tool, HEC-HMS output, SCS):
  a plain Time/flow table rather than a coupling between tools, so any source can be pasted and checked.
- D-063: the freeboard reference is the greater of the routed 100-yr WSE and the spillway-alone WSE at the
  spillway design flow, reading §4.10.4(4) "1 ft above the emergency spillway water surface elevation or 1 ft
  above the 100-yr WSE during spill, whichever is greater". The default spillway design flow (100-yr inflow with
  the principal outlet blocked) is conservative; enter `spillway design Q` to use another.
- D-064: risers take the least of weir and orifice control, then the barrel limits the total; the minimum of
  non-decreasing ratings stays non-decreasing, so routing remains valid.

## Validation

`validation/hand_calcs_detention.py` (output in `hand_calcs_detention_output.txt`) checks a graded basin with an
orifice, a riser and an emergency spillway against a triangular 120 cfs inflow. It integrates storage numerically
and routes two independent ways (textbook storage-indication table at 0.001 ft; Runge-Kutta on dS/dt = I − O at
0.5 s): both give 113.7–113.8 cfs and WSE 105.926–105.927 ft, and MasterFlow matches within 0.2 % and 0.003 ft
(`DetentionRoutingValidationTests`). Other tests check closed-form storage and outlet flows, mass balance, the
time-step and table-spacing independence of the answer, monotonic ratings, every criterion, and rejection of bad
input (negative sizes, unknown types, hours or m³/s in the hydrograph, uphill outlet pipe, SI projects).
