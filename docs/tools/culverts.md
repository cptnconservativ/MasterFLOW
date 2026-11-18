# Culvert analysis (FHWA HDS-5, Rapid City IDCM 2012 §4.7)

Rapid City IDCM §4.7.2(4) requires culverts to be designed by the current FHWA HDS-5 (3rd ed., 2012). The tool
computes, for a design flow, an optional check flow and a performance curve:

| Item | Method |
|---|---|
| Inlet control, unsubmerged (Ku·Q/(A·D^0.5) ≤ 3.5) | Form 1: HW/D = Hc/D + K (Ku Q/(A D^0.5))^M + Ks·S, Hc = dc + Vc²/2g. Form 2: HW/D = K (Ku Q/(A D^0.5))^M (HDS-5 eqs. A.1, A.2) |
| Inlet control, submerged (≥ 4.0) | HW/D = c (Ku Q/(A D^0.5))² + Y + Ks·S (eq. A.3). Ks = −0.5, +0.7 for mitered inlets. Ku = 1.0 (U.S.), 1.811 (SI) |
| Transition (3.5–4.0) | Straight line between the unsubmerged value at 3.5 and the submerged value at 4.0 (HDS-5 draws a tangent curve by hand; HY-8 uses a polynomial). Continuous and rising |
| Outlet control (default `outlet method = hds5`) | HWo = ho + H − S·L; H = (1 + Ke + Kf)·V²/2g, V = Q/A full; Kf = 2g n² L/(k² R^4/3) (29.16 n² L/R^4/3 in U.S. units; HDS-5 prints the rounded 29); ho = TW when TW ≥ D, else max(TW, (dc + D)/2) |
| Outlet control (`outlet method = backwater`) | Standard step up the barrel from max(TW, dc) at the outlet (400 steps). Where the profile reaches the crown the rest of the barrel is full (full-flow friction). HW = inlet energy + Ke·V²/2g. Steep barrel with a low tailwater: critical depth at the inlet. Always shown in the hand-check table for comparison |
| Controlling headwater | The larger of inlet and outlet control |
| Outlet velocity | Inlet control on a steep barrel: normal depth (HDS-5's conservative choice). Otherwise the depth at the outlet is max(dc, TW), capped at D |
| Roadway overtopping | Q = Cd·h^1.5 integrated along the [Roadway] crest profile (linear between points). Cd default 3.0 (1.66 SI); take it from HDS-5 Chart 60 for the road surface. Submergence by the tailwater uses the Villemonte factor (1 − (ht/h)^1.5)^0.385 instead of reading Chart 60's kt curve. Flow is split so that Qculvert + Qroad = Q at one headwater (bisection) |
| Tailwater | Constant depth, constant elevation, a [Tailwater rating] table (Q, elevation), or normal depth of a trapezoidal outlet channel (bed at the outlet invert), using the total flow |
| Multiple barrels | Identical barrels share the culvert flow equally |
| Rapid City checks (`criteria = rapid-city`) | 10-yr (design Q) headwater at least 1 ft below the road low point; 100-yr (check Q) depth over the road low point ≤ 18 in. on local streets, 12 in. on collectors and arterials (Table 4-9; the road low point stands in for the gutter flow line); outlet velocity ≤ 20 fps; corrugated metal flagged REVIEW (§4.7.2(3)); reminder that §4.9 inlet/outlet treatment is required |

Outputs: summary, culvert data (every coefficient used and its source), results per flow, a **hand-check table**
(Ku·Q/AD^0.5, regime, dc, Hc, the inlet equation used, V, V²/2g, H, ho, S·L, HWo by both methods, yn, outlet depth
and its basis), criteria with PASS/FAIL, and the performance curve as a table and a chart (inlet control, outlet
control, controlling headwater, road low point, top of barrel) in the HTML report. Excel gets one sheet per table.

## Inlet coefficients

Built in (ids for `inlet =`): circular concrete (square edge / groove end with headwall, groove end projecting),
circular CMP (headwall, mitered, projecting), beveled rings (45°, 33.7°), concrete boxes (charts 8–13: wingwall
flares, top bevels, chamfers, skews, offset flares), concrete ellipses (square edge, groove end). Each row's Ke
default is from HDS-5 Table C.2. Any other inlet: `inlet = custom` with `form`, `K`, `M`, `c`, `Y`, `Ks`, `Ke`.

The coefficient table was transcribed from secondary copies of HDS-5 Table A.1/A.2 (a county stormwater manual
and a software help page that reprints Table A.1) because the FHWA site is not reachable from the build machine.
The two copies differ on a few rows (groove end with headwall K 0.0018 vs 0.0078; box 30°–75° flares c 0.0347 vs
0.0385); MasterFlow uses the Table A.1 reprint (0.0018, 0.0347), which matches the HDS-5 2005 charts. **Check the
rows you use against your copy of HDS-5 before relying on them.**

## Validation

`validation/hand_calcs_culvert.py` computes the reference values independently from the equations above
(critical depth by bisection on Q²/g = A³/T). The tests (`CulvertToolTests`) hold the engine to them to 1e-5 ft:
Form 1 unsubmerged, transition and submerged for a 48 in. RCP; Form 1 and Form 2 and submerged for a 6×4 box;
outlet control with a submerged and a free outlet; the weir and the Villemonte factor; and the same culvert in SI
(Ku = 1.811) against U.S. units. Further checks: continuity and monotonic rise of inlet control through the
transition; the +0.7S mitered correction; backwater reaching normal depth in a long mild barrel; steep barrels
(inlet control, outlet at yn); flow split between barrels; overtopping mass balance; and the input errors (zero
or negative n and D, fractional barrels, inlet/shape mismatch, unknown inlet, adverse slope, road below invert).

Not yet compared against a published HDS-5 worked example or HY-8 run (the HDS-5 PDF could not be downloaded
here). Running the sample in HY-8 is the recommended next check.

Not modelled: improved (side- or slope-tapered) inlets, pipe arches and arch shapes, partly full outlet control
for HDS-5's "ho" refinements beyond the two methods above, approach velocity, buoyancy/structural checks,
and energy dissipators (HEC-14, IDCM §4.9).

Decision (D-059): the HDS-5 outlet-control equation is the default because it is what a reviewer checks by hand
and what the HDS-5 charts use; the backwater value is always shown and is used when `outlet method = backwater`,
and an Info message says when the two differ by more than 5 % of D.
