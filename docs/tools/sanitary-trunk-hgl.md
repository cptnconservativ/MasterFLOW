# Sanitary trunk sewer HGL and surcharge (premium)

Tool id `sanitary-trunk-hgl` (★ Premium, feature `sanitary-trunk-hgl`; U.S. units). Computes the hydraulic and
energy grade lines of a sanitary trunk sewer or interceptor at its design peak flow and checks them against the
manhole rims and the lowest service and basement elevations. Large mains are sized to run near d/D 0.70 at peak
flow, where a small backwater from the next pipe or a manhole loss can push the water surface past the limit or into
surcharge; normal depth alone does not show that.

## Input

The input is a [Sanitary sewer network design](sanitary-network.md) input: the same keys, `[Manholes]`, `[Loads]` and
`[Pipes]` tables, or the selected network. The flows, sizes and inverts are worked out exactly as that tool does
(blank cells are designed first), so the two tools always agree. Added keys and tables:

| Key | Default | Meaning |
|---|---|---|
| `outlet water surface` | normal | `normal` (normal depth of the peak flow in the outlet sewer), `free` (critical depth), `crown`, or an elevation, e.g. the HGL of the interceptor downstream; never below critical depth |
| `losses` | IDCM | `IDCM` (Rapid City Figures 4-6/4-7 cases from the pipe layout and deflection) or `K` |
| `manhole K` | 0.15 | hs = K·V²/2g at every manhole with `losses = K` |
| `flow factor` | 1.0 | multiplies every peak flow (e.g. a wet-weather or future check) |
| `rim freeboard` | 1.0 ft | HGL below the manhole rim |
| `service freeboard` | 1.0 ft | HGL below the lowest service or basement elevation |
| `max d/D` | 0.70 | IDCM 3.12.4 item 2 |

- `[Manholes]` may carry a `K` column, which uses hs = K·V²/2g at that manhole whatever `losses` says.
- `[Services]` Location (manhole or pipe), Station (ft from the pipe's upstream end; blank for a manhole), Name,
  Elevation (lowest service invert, floor drain or basement floor served by gravity).
- `[Flows]` Pipe, Q peak (gpm): replaces the computed peak flow of that pipe (e.g. from the master plan model).

## Method

1. The sanitary sewer design runs on the input (H2S off) and gives each sewer's size, inverts, run (inside wall to
   inside wall), n and peak flow including I/I.
2. The sewers and manholes become a network for the storm network energy solver (`StormNetworkSolver`): standard
   step energy equation upstream through each sewer, with full, part-full and surcharged reaches in one equation, and
   critical depth as the lowest downstream depth. Each sewer's friction length is its run, the length its slope is
   measured over (D-721).
3. Manhole losses (D-720): with `losses = IDCM`, the cases of IDCM Figures 4-6/4-7. The upstream end of a line is
   Case VI (1.25·V²/2g); a manhole with one inflow and a deflection of 11.25° or more is the Case VIII bend at or
   above the deflection (22½°, 45°, 60°, 90°); otherwise Case I (c = 0.5) for one inflow and Case IV (c = 0.25) for
   several. V1 is the largest-flow inflow (D-042). With `losses = K`, hs = K·Vo²/2g.
4. Checks: each sewer's depth along the computed profile against d/D ≤ 0.70 (D-723) and against the crown
   (surcharge); each manhole's rim − HGL against the rim freeboard; each service's elevation − HGL (interpolated
   along the profile for a service on a pipe) against the service freeboard.

A profile of the longest flow path shows rims, invert, crown, HGL (filled) and EGL.

## Decisions

- **D-720** Manhole losses default to the storm-sewer cases of IDCM Figures 4-6/4-7, which the IDCM gives for storm
  sewers only; the IDCM has no sanitary manhole loss method. The deflection entered for the sanitary manhole drop
  rule also picks the bend case. Shaped (benched) sanitary manholes generally lose less than these storm junctions,
  so the default is on the safe side; `losses = K` with a channelled-manhole K (default 0.15, an adjustable
  assumption, not a cited value) or a K per manhole is the alternative.
- **D-721** The friction length is the run the sanitary design measures the slope over (length less the manhole
  diameter, IDCM 3.12.6 item 7), so normal depth in the HGL solve equals the design's normal depth. The outlet water
  surface defaults to normal depth of the peak flow in the outlet sewer, since a trunk usually continues downstream;
  give the downstream interceptor's HGL as an elevation when it is known. A network with several outlets falls back to
  free outfalls (W-TH-007).
- **D-722** The IDCM sets no HGL criterion for sanitary sewers (only d/D ≤ 0.70 at peak, 3.12.4). The 1 ft rim and
  service freeboards mirror the storm sewer clearance of IDCM 4.6.7 and are inputs, not City criteria.
- **D-723** d/D is checked on the HGL depth along the whole computed profile, as well as on normal depth. A sewer
  whose normal depth passes but whose backwater depth exceeds 0.70 gets W-TH-001 "although normal depth gives …".

## Messages

| Code | Meaning |
|---|---|
| E-TH-001 | SI project |
| E-TH-002 | Unreadable or negative key (losses, outlet water surface, flow factor, freeboards, K) |
| E-TH-003 | Service row invalid: location, station or elevation |
| E-TH-004 | `[Flows]` row invalid or names no pipe |
| E-TH-005 | The sanitary sewer design of the input failed (its errors follow) |
| W-TH-001 | Depth above d/D 0.70 at peak flow with the backwater |
| W-TH-002 | Sewer surcharged (HGL above the crown) |
| W-TH-003 | HGL within the rim freeboard |
| W-TH-004 | HGL above the rim (overflow) |
| W-TH-005 | HGL within the service freeboard |
| W-TH-006 | HGL above a service or basement elevation (backup) |
| W-TH-007 | Several outlets: free outfalls used instead of normal depth |
| I-TH-001, I-TH-002 | No IDCM sanitary HGL criterion; storm-sewer loss cases in use |

## Tests

`SanitaryTrunkHglTests`: sample runs with services and a profile; SI refused; normal-depth tailwater gives the
uniform-flow HGL by hand; a high tailwater surcharges the sewer and backs up a service; a tailwater at 0.85 D flags
the backwater d/D while normal depth passes; hs = K·V²/2g by hand; a 90° deflection picks the 90° bend case; `[Flows]`
override with a flow factor; bad services, stations, flows and keys are errors.
