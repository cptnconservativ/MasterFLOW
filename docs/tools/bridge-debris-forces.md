# Bridge debris, pier scour with debris and hydraulic forces (HEC-9, HDS-7)

This tool sizes a design debris raft at a pier, shows how much it adds to pier scour, and computes the water forces on
the pier, the raft and an inundated deck for the structural engineer. It is free and works in both unit systems.
Forces are given in kips or kN.

## Input

| Key | Meaning |
|---|---|
| `depth`, `velocity` | Approach flow depth y1 and velocity V1 at the pier. |
| `pier width`, `pier length`, `pier shape`, `skew`, `bed` | As in the bridge scour tool: square, round or sharp nose; the skew angle in degrees; the HEC-18 bed condition. |
| `log length` | Design log length, which becomes the raft width W. |
| `channel width upstream`, `sturdy log length`, `pacific northwest` | Without `log length`: HEC-9 §3.3.2 design log length = the smallest of the upstream channel width, the sturdy-log length (80 ft / 24 m; 150 ft / 45 m in the Pacific Northwest) and 30 ft (9 m) + width/4 (not applied in the Pacific Northwest). |
| `raft thickness`, `raft shape`, `raft drag coefficient` | T (default the full flow depth, HEC-9 Table 3.4), rectangular or triangular, and CD (default HEC-9 Table 4.1 when `contracted area` is given, else AASHTO 1.4). |
| `upstream water surface`, `downstream water surface`, `debris bottom` | HEC-9 §4.6 debris loads: elevations on one datum. The accumulation spans from the debris bottom to the upstream water surface. |
| `contracted area`, `other debris area`, `reference velocity`, `reference depth` | Unobstructed flow area Ac of the contracted section and the debris area on other piers, for the blockage ratio B = Ad/(Ad + Ac); Vr and yr for the Froude number (default the approach velocity and depth). |
| `water surface`, `low chord`, `deck depth`, `deck width`, `span` | Deck geometry, for deck forces. Elevations share one datum. |
| `deck type` | `six-girder` (default), `three-girder` or `streamlined`: the FHWA-HRT-09-028 coefficient envelopes cited by HDS-7 §11.1.4. |
| `deck drag coefficient`, `deck lift coefficient`, `deck moment coefficient` | Optional overrides of the envelope values. |
| `entrapped air`, `girder depth` | Fraction of the space between girders holding air, and the girder depth. Used for buoyancy. |

## Method

1. Effective pier width with debris (HEC-18 2012, from NCHRP 653): a_d = [K1·T·W + (y − K1·T)·a]/y, with K1 = 0.79 for a rectangular raft and 0.21 for a triangular one.
2. CSU pier scour, ys = 2.0·K1·K2·K3·(y1/a)^0.35·Fr1^0.43·a, computed for the pier alone and for a_d with K1 = K2 = 1.
3. Stream pressure, F = ½·ρ·C·V²·A (AASHTO LRFD 3.7.3):
   - Pier longitudinal CD is 0.7 semicircular-nosed, 1.4 square-ended and 0.8 wedged.
   - The raft uses CD 1.4 on W·T, or half of that area for a triangular raft.
   - Lateral CL depends on the skew angle: 0 at 0°, 0.5 at 5°, 0.7 at 10°, 0.9 at 20°, and 1.0 at 30° or more. It acts on L·y.
4. HEC-9 §4.6 debris loads (Parola, NCHRP 445): Fh = γ(hcu·Ahu − hcd·Ahd) with Ahu, Ahd the debris area below the upstream and downstream water surfaces and hcu, hcd half those depths; FD = CD·γ·Ahu·Vr²/2g with CD from HEC-9 Table 4.1 (B < 0.36: 1.8 for Fr < 0.4, 2.6 − 2.0·Fr for 0.4–0.8; 0.36–0.77: 3.1 − 3.6·B; B > 0.77: 1.4 − 1.4·B); the elevation of Fh by moments. Apply the total as a point load.
5. Deck forces (HDS-7 §11.1.4, FHWA-HRT-09-028), h* = (WS − low chord)/deck depth:
   - CD = A·e^(−2h*²) − B·e^(−0.75h*²) + a; CL = b·(e^(−3.5h*²) − e^(−c·h*^1.7)); CM = d·h*^α·e^(−f·h*²) + g (girder decks; the streamlined deck has its own forms), with the report's Table 1 and 2 coefficients. Drag uses the upper envelope, lift the lower (most negative), moment the upper; both envelopes are listed.
   - Drag acts on the inundated depth (WS − low chord, at most the deck depth) × span.
   - Lift acts on deck width × span.
   - Buoyancy counts the slab below the water surface fully, and the girder zone only through its entrapped-air fraction.

## Decisions

| ID | Decision |
|---|---|
| D-570 | The raft width is the design log length entered by the user, with no default. HEC-9 bases it on the debris that reaches the site, which only local knowledge gives. |
| D-571 | Superseded by D-575. |
| D-572 | With debris, K1 = K2 = 1 because the raft, not the pier nose, faces the flow. K3 is kept. |
| D-573 | Superseded by D-576. |
| D-574 | Validation: hand calculations (BridgeDebrisTests) of a_d, CSU with and without debris, the AASHTO coefficients and interpolation, deck drag, lift and buoyancy. |
| D-575 | (2026-11-25) The raft thickness defaults to the full flow depth, the maximum extent of a pier accumulation in HEC-9 Table 3.4; the design log length can be computed from the upstream channel width by HEC-9 §3.3.2. HEC-18 (debris K1, NCHRP 653) was not in the reference set, so K1 = K2 = 1 with debris (D-572) stands. |
| D-576 | (2026-11-25) Deck CD, CL and CM come from the FHWA-HRT-09-028 envelopes, which HDS-7 2nd ed. §11.1.4 names as the source; they reproduce the report's Chapter 5 example (Table 6) to three decimals. |
| D-578 | (2026-11-25) HEC-18 (5th ed.) §7.7.3 Eq. 7.32 and Example Problem 7 confirm D-572: "the shape of the pier itself has no effect once debris has accumulated", K1 = K2 = 1 with a_d. Example 7 (a_d 3.8 / 7.3 ft; ys 8.9, 6.8, 13.6 ft) is reproduced in ManualWorkedExampleTests. |
| D-577 | (2026-11-25) HEC-9 §4.6 hydrostatic and drag debris loads with HEC-9 Table 4.1 drag coefficients; validated against HEC-9 §4.7 Example 2 (ManualWorkedExampleTests). |
