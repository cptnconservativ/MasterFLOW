# Bridge debris, pier scour with debris and hydraulic forces (HEC-9, HDS-7)

This tool sizes a design debris raft at a pier, shows how much it adds to pier scour, and computes the water forces on
the pier, the raft and an inundated deck for the structural engineer. It is free and works in both unit systems.
Forces are given in kips or kN.

## Input

| Key | Meaning |
|---|---|
| `depth`, `velocity` | Approach flow depth y1 and velocity V1 at the pier. |
| `pier width`, `pier length`, `pier shape`, `skew`, `bed` | As in the bridge scour tool: square, round or sharp nose; the skew angle in degrees; the HEC-18 bed condition. |
| `log length` | Design log length, which becomes the raft width W. HEC-9 takes the longest sturdy log likely to reach the site, limited by the upstream channel width. |
| `raft thickness`, `raft shape`, `raft drag coefficient` | T (default half the flow depth), rectangular or triangular, and CD (default 1.4). |
| `water surface`, `low chord`, `deck depth`, `deck width`, `span` | Deck geometry, for deck forces. Elevations share one datum. |
| `deck drag coefficient`, `deck lift coefficient` | Read from HDS-7 Chapter 7 for the inundation ratio. Required when the water reaches the deck. |
| `entrapped air`, `girder depth` | Fraction of the space between girders holding air, and the girder depth. Used for buoyancy. |

## Method

1. Effective pier width with debris (HEC-18 2012, from NCHRP 653): a_d = [K1·T·W + (y − K1·T)·a]/y, with K1 = 0.79 for a rectangular raft and 0.21 for a triangular one.
2. CSU pier scour, ys = 2.0·K1·K2·K3·(y1/a)^0.35·Fr1^0.43·a, computed for the pier alone and for a_d with K1 = K2 = 1.
3. Stream pressure, F = ½·ρ·C·V²·A (AASHTO LRFD 3.7.3):
   - Pier longitudinal CD is 0.7 semicircular-nosed, 1.4 square-ended and 0.8 wedged.
   - The raft uses CD 1.4 on W·T, or half of that area for a triangular raft.
   - Lateral CL depends on the skew angle: 0 at 0°, 0.5 at 5°, 0.7 at 10°, 0.9 at 20°, and 1.0 at 30° or more. It acts on L·y.
4. Deck forces (HDS-7):
   - Drag acts on the inundated depth (WS − low chord, at most the deck depth) × span.
   - Lift acts on deck width × span.
   - Buoyancy counts the slab below the water surface fully, and the girder zone only through its entrapped-air fraction.

## Decisions

| ID | Decision |
|---|---|
| D-570 | The raft width is the design log length entered by the user, with no default. HEC-9 bases it on the debris that reaches the site, which only local knowledge gives. |
| D-571 | The raft thickness defaults to half the approach depth, as NCHRP 653 recommends. It is an input. HEC-9 was not available to confirm a maximum thickness, so none is applied. |
| D-572 | With debris, K1 = K2 = 1 because the raft, not the pier nose, faces the flow. K3 is kept. |
| D-573 | Deck drag and lift coefficients are inputs. HDS-7 gives them as charts against the inundation ratio and the deck shape, and those charts could not be read from the build environment. |
| D-574 | Validation: hand calculations (BridgeDebrisTests) of a_d, CSU with and without debris, the AASHTO coefficients and interpolation, deck drag, lift and buoyancy. |
