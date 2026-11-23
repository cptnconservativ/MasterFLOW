# Natural channel water surface profiles (★ Premium)

Tool id `river-profile` (license feature of the same name). Steady water surface profiles through surveyed natural
cross-sections, by the method of HEC-RAS (USACE, Hydraulic Reference Manual, Ch. 2). Use it for the floodplain at a
stream crossing, a drainageway master plan reach, or a check of a FEMA effective model's sections. Prismatic ditches
and swales stay in the free **Channel water surface profile** tool.

## Input

- `[Points]`: RS, Station, Elevation (and optional n, which starts at that point and carries to the right), left to
  right looking downstream. A Civil 3D sample-line section report pastes in directly.
- `[Sections]`: RS, Left bank, Right bank, LOB length, Ch length, ROB length (to the next section **downstream**, as in
  HEC-RAS), n LOB, n Ch, n ROB, and optional Contraction, Expansion (defaults 0.1 / 0.3 or the `contraction` /
  `expansion` keys), Ineff left sta / el, Ineff right sta / el.
- `[Flows]`: RS, then one column per profile. A flow applies at its RS and downstream until the next change. Or `Q =`.
- `regime` = subcritical (default), supercritical or mixed. `downstream` = normal (`downstream slope`), critical or known
  (`downstream ws` = one elevation per profile, comma-separated). `upstream` = critical or known (`upstream ws`).

## Method

| Item | Method |
|---|---|
| Energy equation | WS2 + α2V2²/2g = WS1 + α1V1²/2g + he; he = L·Sf + C·\|α2V2²/2g − α1V1²/2g\| (HRM Eq. 2-1, 2-2) |
| Reach length | Discharge weighted: L = (Llob·Qlob + Lch·Qch + Lrob·Qrob)/(Qlob + Qch + Qrob), flows averaged over the two sections |
| Friction slope | Average conveyance, Sf = ((Q1 + Q2)/(K1 + K2))², the HEC-RAS default |
| Contraction / expansion | C = contraction where the velocity head increases downstream, expansion where it decreases |
| Conveyance | Each run of segments with the same n inside LOB, channel or ROB is one conveyance segment, K = k/n·A·R^⅔; summed per subsection (D-341) |
| α | Σ(Ki³/Ai²)/(Kt³/At²) over LOB, channel and ROB |
| Ineffective flow areas | Segments left of the left station (right of the right station) carry no area or conveyance while WS is below the set elevation |
| Ends | Water above an end point is held by a vertical wall whose height counts as wetted perimeter (as HEC-RAS); warned (W-RP-006) |
| Critical W.S. | Minimum specific energy: 400-level scan from the invert to twice the relief, golden-section refinement, lowest minimum (several are reported) |
| Solution | Bisection on WS between critical and above, on the regime's branch. No balance on the branch → critical depth, warned (HEC-RAS default) |
| Mixed regime | Sub- and supercritical runs; at each section the one with the greater specific force Q²/(gA) + A·ȳ governs (D-342) |
| Checks | Conveyance ratio between sections outside 0.7–1.4 (HEC-RAS guidance for adding sections); bank overtopping and freeboard per profile |

Output: the HEC-RAS standard table (Min Ch El, W.S., Crit W.S., E.G., E.G. slope, channel velocity, area, top width,
channel Froude number, flow split, α), bank checks, the longitudinal profile chart (bed, banks, WS and EG per profile)
and a plot of each cross-section with the largest profile's water surface.

## Decisions

- **D-340** HEC-RAS method rather than a new one, so results can be checked against a HEC-RAS model of the same data.
- **D-341** Conveyance is subdivided at every n change, in the channel too. HEC-RAS by default computes one composite
  n for a main channel with several n values (when its side slopes are steeper than 5H:1V); with a single channel n,
  as in most models, the two are identical.
- **D-342** Mixed regime by specific force at each section (the HEC-RAS rule), without HEC-RAS's re-computation of the
  subcritical profile downstream of a jump that moves; the jump location is reported between sections.
- **D-343** Design-tool numbers of 1000 or more now show two decimals (was one), so elevations read to 0.01 ft in every
  tool's tables.

## Validation

`RiverProfileTests`: a uniform reach stays at the existing engine's Manning normal depth at every section; rectangle
critical depth against (q²/g)^⅓; compound-section conveyance and α by hand; an M1 backwater curve against an independent
RK4 integration of dy/dx = (S0 − Sf)/(1 − Fr²) (within 0.01 ft over 2000 ft); energy balance closed between every pair
of sections in the sample; ineffective areas; a steep-to-mild mixed-regime reach; SI scaling; input errors. A run
against HEC-RAS itself needs a Windows machine with HEC-RAS (not available here).
