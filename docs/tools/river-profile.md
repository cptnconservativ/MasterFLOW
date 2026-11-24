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

## Bridges

A bridge sits between two adjacent cross-sections. Its **Upstream RS** is HEC-RAS section 3, at the upstream face.
The next section downstream is section 2, at the downstream face. Place sections 4 and 1 further away for the
contraction and expansion reaches, as HEC-RAS recommends. The sample shows a bridge with ineffective areas at
sections 2 and 3 outside the abutments, up to the deck.

`[Bridges]` columns:
- Name and Upstream RS.
- Low chord, High chord (top of deck or road) and Deck width (length along the flow).
- Left abutment and Right abutment stations.
- Pier shape: semicircular, twin cylinder (with diaphragm), triangular or square.
- Optional: Weir C (2.6 U.S. or 1.44 SI), Contraction (0.3) and Expansion (0.5), Sluice Cd (0.5) and Orifice C
  (0.8).

`[Bridge piers]` takes Bridge, Station and Width. `[Road profile]` (Bridge, Station, Elevation) gives an irregular
weir crest; otherwise the crest is the high chord, or the ground where that is higher. `bridge low flow` is `energy`
(default), `yarnell` or `highest`.

**Low flow.**
- *Energy method.* Two opening sections, BD and BU, are built from sections 2 and 3:
  - Ground outside the abutments is blocked by vertical walls.
  - Piers are vertical blocks whose faces add wetted perimeter.
  - The deck soffit is a lid at the low chord. Above it, the area stays at its low-chord value, the top width is
    zero and the soffit is wetted.
  - The standard step then runs 2 → BD (no length) → BU (deck width) → 3 (the rest of section 3's reach length),
    using the bridge's contraction and expansion coefficients through the opening.
- *Yarnell (class A, piers only).* ΔH = 2K(K + 10ω − 0.6)(α + 15α⁴)·V₂²/2g, with K from HEC-RAS Table 5-1:
  semicircular 0.90, twin cylinder with diaphragm 0.95, twin cylinder or triangular 1.05, square 1.25.

**High flow.** This applies when the upstream energy grade reaches the low chord or the road crest.
- *Sluice gate:* Q = Cd·A·√(2g(Y₃ − Z/2 + V₃²/2g)).
- *Orifice*, when the downstream W.S. is above the low chord: Q = C·A·√(2g(EG₃ − WS₂)).
- *Weir over the road:* Q = C·∫(EG₃ − crest)^1.5 dx, reduced for submergence by Villemonte's
  (1 − (h_d/h_u)^1.5)^0.385.
- The upstream W.S. is found where the opening flow plus the weir flow equals Q.
- Without overtopping, the pressure answer is compared with the energy-method answer and the higher energy is kept,
  as HEC-RAS does.

The bridge table gives, for each bridge and profile:
- The flow type.
- Upstream and downstream W.S. and E.G., and the energy loss.
- The opening and weir flows, and the opening velocity.
- Both low-flow answers.
- Low chord clearance and overtopping head.

The profile chart shows the deck, and the cross-section plot of section 3 shows the deck, low chord and piers.

### Bridge decisions

- **D-370** Bridges are solved in the subcritical pass only. In a mixed-regime run, the subcritical bridge answer is
  used upstream of each bridge; in a supercritical run, bridges are ignored with a warning. HEC-RAS also checks
  class C (supercritical through the opening); here class B (critical in the opening) is flagged only.
- **D-371** The momentum method and the WSPRO method are not included. The default is the energy method, and
  `highest` takes the higher of energy and Yarnell.
- **D-372** The sluice Cd and orifice C are single user values (HEC-RAS defaults 0.5 and 0.8). The Cd-versus-Y₃/Z
  chart in HEC-RAS (Figure 5-6) could not be obtained as text.
- **D-373** Weir submergence uses Villemonte's equation, not the HEC-RAS submergence curve (Figure 5-8), for the
  same reason. Villemonte's equation reduces flow more gradually at low submergence.
- **D-374** The low chord is level across the opening (one value per bridge).
- **D-375** A downstream boundary at a bridge for a weir run holds the downstream W.S. fixed while the flow divides
  between the opening and the weir. The split is not iterated back to section 2.
- **D-376** Change to all sections: dry ground or an island inside a subsection no longer splits a conveyance
  segment. Segments break at n changes only, as in HEC-RAS. Before this change, a dry pier top split the opening's
  conveyance in two. Results for sections without dry high points inside a subsection are unchanged.

## Validation

`RiverProfileTests`: a uniform reach stays at the existing engine's Manning normal depth at every section; rectangle
critical depth against (q²/g)^⅓; compound-section conveyance and α by hand; an M1 backwater curve against an independent
RK4 integration of dy/dx = (S0 − Sf)/(1 − Fr²) (within 0.01 ft over 2000 ft); energy balance closed between every pair
of sections in the sample; ineffective areas; a steep-to-mild mixed-regime reach; SI scaling; input errors. A run
against HEC-RAS itself needs a Windows machine with HEC-RAS (not available here).
- Bridges (BridgeTests):
  - Opening area, top width and conveyance with abutments, piers and the deck lid match hand calculations.
  - Yarnell matches the equation.
  - Weir flow matches C·L·H^1.5 free, and with Villemonte submergence.
  - Overtopping splits the flow so that opening flow plus weir flow equals Q.
  - A drowned opening runs as an orifice or a lidded energy solution.
