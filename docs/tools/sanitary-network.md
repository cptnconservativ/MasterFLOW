# Sanitary sewer network design (premium)

Tool id `sanitary-network` (★ Premium, feature `sanitary-network`; U.S. units). Designs and checks a gravity sanitary
sewer network by the City of Rapid City *Infrastructure Design Criteria Manual* (January 2012) §3.12, which adopts the
Ten States Standards as supplemented by SDDENR (§3.3.2). `criteria = Ten States` uses the Ten States Standards (2014)
alone. Blank pipe sizes and inverts are designed; entered ones are checked. The output follows the City's *Sanitary
Sewer System Design Computation Form* (IDCM Table 3-3).

## Input

Keys (defaults are the Rapid City values):

| Key | Default | Source |
|---|---|---|
| `criteria` | Rapid City | or `Ten States` |
| `peaking` | Rapid City (Ten States: Harmon) | `Rapid City`, `Harmon` or a fixed number |
| `reduction factors` | yes | IDCM 3.12.2 RF tables |
| `gpcd`, `persons per household` | 100, 2.65 | IDCM 3.12.2 QP and PH |
| `commercial rate`, `industrial rate`, `public rate` | 3, 3, 2 gpm/ac | IDCM 3.12.2 QA |
| `residential PF`, `commercial PF`, `public PF` | 4.2, 1.7, 1.2 | IDCM 3.12.2 PF (commercial PF also used for industrial) |
| `infiltration` (gpd per inch-diameter-mile), `inflow` (gpd/ac) | 0, 0 | none in the IDCM; from the master plan |
| `minimum diameter` | 8 in. | IDCM 3.12.4 item 1 |
| `max d/D` | 0.70 at peak | IDCM 3.12.4 item 2 |
| `min velocity`, `min velocity basis` | 2.0 ft/s at `peak` (Ten States: `full`) | IDCM 3.12.6 item 1; Ten States §33.41 |
| `max velocity` | 10 ft/s at half depth | IDCM 3.12.6 item 3 |
| `minimum cover`, `wall thickness` | 5 ft over the top of pipe, 0 in. | IDCM 3.12.5 (4 ft for interceptors with no services) |
| `manhole diameter`, `pipe length` | 4 ft, `center to center` | IDCM 3.12.6 item 7 (slope inside wall to inside wall); `inside to inside` uses the lengths as entered |
| `drop manhole height` | 2 ft | Ten States §34.2 |
| `max spacing small`, `max spacing large` | 400 ft (≤ 15 in.), 500 ft (≥ 18 in.) | IDCM 3.12.9 item 2 |
| `n` | 0.013 for blank cells | IDCM 3.12.6 item 4 |
| `sizes` | 8, 10, 12, 15, 18, 21, 24, 27, 30, 36, 42, 48 | |
| `h2s`, `h2s formula`, `EBOD residential`, `EBOD commercial` | yes, IDCM, 300, 500 mg/L | IDCM 3.12.14 |
| `redesign` | no | network mode: `sizes`, or `sizes and inverts` |

Tables:

- `[Manholes]` Name, Rim, Deflection (deg). The deflection sets the 0.10 / 0.15 ft minimum drop; blank uses the
  network coordinates, or 0.
- `[Loads]` Manhole, Land use, Area (ac), DU/ac, Population, Flow (gpm), PF. Columns are found by their header
  words, so unused ones can be left out. Land uses: `Residential` and `Multi-family` (Area and DU/ac, or Population),
  `Commercial`, `Industrial`, `Public` (Area, or an average Flow), and `Flow` (an average point flow with its own PF,
  default 1.0). A PF entered on any row replaces the land-use default for that row.
- `[Pipes]` Name, From, To, Length (ft), Diameter (in), Up invert, Down invert, n. Leave Diameter blank to size the
  pipe and both inverts blank to design them.

Without a `[Pipes]` table the selected network is used: its pipes, lengths, inverts, n and rims (for example a Civil 3D
sanitary network imported from LandXML). Entered `[Manholes]` rows replace the network's rims. With `redesign`, the
designed sizes, inverts and peak flows can be written back with **Apply to network** and exported to Civil 3D with
**LandXML…** as for a storm network.

## Method

**Design flows (IDCM 3.12.2).** Residential Qave (gpm) = QP·PH·DU·A/1440; commercial, industrial and public Qave =
QA·A. The loads at every manhole upstream of a pipe (and at its From manhole) are added. Qpeak = Σ Qave·PF·RF, where
the residential RF comes from the residential population served above the pipe (≤ 500 1.0; ≤ 1,000 0.955; ≤ 2,000
0.905; ≤ 5,000 0.86; ≤ 10,000 0.775; above 0.72) and the commercial/industrial RF from the commercial and industrial
acres served (≤ 1,000 1.0; ≤ 2,000 0.95; ≤ 5,000 0.85; above 0.71). Public loads have no RF. This applies the RF
"incrementally from the farthest downstream location" as the IDCM notes require, since each pipe uses the population
or area above it.

With `peaking = Harmon`, PF = 1 + 14/(4 + √P), P in thousands, from the equivalent population Qave·1440/gpcd of all
sanitary loads. A number gives a fixed PF. Point flows always keep their own PF. Infiltration and inflow are added to
both Qave and Qpeak.

**Hydraulics.** Manning (k = 1.486) for a circular pipe at normal depth, solved by bisection up to y = 0.938 D (the
greatest open-channel capacity). A peak flow above that capacity is reported as surcharged (W-SS-014). Half-full
velocity equals full-flow velocity, so the IDCM 10 ft/s at half depth limit is a full-flow velocity limit.

**Checks** (each a warning, with its source): d/D at peak; velocity at peak (or full); Ten States minimum slope
(§33.41: 8 in. 0.40 %, 10 in. 0.28 %, 12 in. 0.22 %, 15 in. 0.15 %, 18 in. 0.12 %, 21 in. 0.10 %, 24 in. 0.08 %,
27 in. 0.067 %, 30 in. 0.058 %, 33 in. 0.052 %, 36 in. 0.046 %, 39 in. 0.041 %, 42 in. 0.037 %; other sizes at
2 ft/s full with n = 0.013); 10 ft/s at half depth; minimum size; larger pipe upstream of a smaller one; n below
0.013; manhole spacing; cover at both ends; slope over 20 % (anchors); the drop across each manhole (the greater of
the average of the slopes in and out times the manhole diameter, and 0.10 ft, or 0.15 ft for a deflection over
45°); outlet crown above the lowest inlet crown; and a drop manhole where an inlet enters 2 ft or more above the
outlet invert.

**Design** (blank cells), upstream first. Sizes are tried from the smallest listed size that is not below the
minimum or any upstream pipe. For each size:

1. Minimum slope = the Ten States slope (with `min velocity basis = full`, also the slope for that velocity full). The
   velocity at peak flow is checked afterwards, not designed for (D-706).
2. Upstream invert = the lowest of: rim − cover − D (− 2 walls); each inlet's invert − its required drop; the inlet
   crown minus D (crowns matched).
3. Slope = the steeper of the minimum and the slope keeping the minimum cover at the next manhole, capped at the
   10 ft/s slope when the minimum allows.
4. The first size with d/D within the limit is kept (W-SS-016 when none is).

**Manholes.** Depth = rim − lowest invert. 48 in. round (Rinker precast) below 24 in. mains; precast tee with a 48 in.
riser for 24 in. and larger (IDCM 3.12.9 item 3); eccentric cone at 5.5 ft and deeper, flat H-20 top below (item 4).

**Hydrogen sulfide (IDCM 3.12.14)** for pipes 24 in. and smaller: Z = EBOD/(S^½·Q^⅓)·P/B with the flattest slope
in the system and Q at 1, 5, 25, 75, 95, 100 and 105 % of each pipe's Qpeak; P and B at normal depth for that Q and
slope. EBOD is 300 mg/L for residential, multi-family and public flow and 500 mg/L for commercial and industrial
flow, weighted by average flow. Condition from the largest Z: under 5,000 rare, 5,000 to 10,000 marginal, over 10,000
common.

**Profile figure**: the longest flow path, with rims, invert, crown and the peak-flow water surface.

## Decisions

- **D-704** (Confirmed by Kyle 2026-11-26: use the IDCM form.) IDCM 3.12.14 prints Z = EBOD/(S^½·Q^⅓)·P/B with S in ft/ft. The source it cites (Pomeroy; EPA 1974;
  ASCE MOP 60) is Z = 3·EBOD/(S^½·Q^⅓)·P/B with S in ft/100 ft, which gives values 3.3 times smaller. The IDCM form
  is the default because the IDCM governs; with it almost every small sewer at minimum slope rates "common".
  `h2s formula = Pomeroy` uses the original form.
- **D-705** The IDCM Z table prints "Z < 8,000 rare" and "5,000 ≤ Z ≤ 10,000 marginal", which overlap. MasterFlow
  takes 5,000 as the upper limit of rare (the marginal row and Pomeroy's original limits).
- **D-706** The 2.0 ft/s minimum at the design peak flow (IDCM 3.12.6 item 1) is checked but not designed for (Kyle,
  2026-11-26). Designed slopes follow the Ten States minimum and cover. A sewer below 2.0 ft/s at peak gets W-SS-002
  with the slope it would need, and is listed in the "Design exceptions" table with its average-flow velocity against
  the 1.8 ft/s deviation of item 2, so the engineer can steepen it or request a written design exception.
  `min velocity basis = full` checks the Ten States full-flow velocity instead.
- **D-707** RF tables: "<500" and "501–1,000" are read as ≤ 500 and ≤ 1,000 people (and the same for acres). The
  commercial/industrial RF uses the commercial and industrial acres served above the pipe; public loads have no RF.
- **D-708** Mixed land uses: each load's Qave·PF is multiplied by the RF of its land-use group. EBOD for H2S is the
  flow-weighted mean of 300 and 500 mg/L.
- **D-709** Cover is measured to the top of the pipe as inside crown + `wall thickness` (default 0, thin-wall PVC).
  Enter the wall for concrete or ductile iron. The service-line requirement of IDCM 3.12.5 item 2b (top of main 1 ft
  below the service invert) needs the service inverts and is not checked.
- **D-710** One outlet per manhole (a gravity tree). Flow splits and overflows are not modelled.

## Messages

| Code | Meaning |
|---|---|
| E-SS-001 | SI project (the tool works in gpm, in. and ft) |
| E-SS-002 | No pipes and no network selected |
| E-SS-003 | Pipe row incomplete, duplicate name, or From = To |
| E-SS-004 | Only one invert given |
| E-SS-005 | A manhole with two outlet pipes |
| E-SS-006 | Pipes form a loop |
| E-SS-007 | Load row invalid (manhole, land use, area or flow) |
| E-SS-008 | Inverts to design but a rim is missing |
| E-SS-009 | Unreadable criteria, peaking, size list or parameter |
| E-SS-010 | Entered inverts do not fall downstream |
| E-SS-011 | Length less the manhole diameter leaves no run |
| W-SS-001 | d/D above the limit at peak flow |
| W-SS-002 | Velocity below the minimum |
| W-SS-003 | Slope below the Ten States minimum |
| W-SS-004 | Over 10 ft/s at half depth |
| W-SS-005 | Below the minimum diameter |
| W-SS-006 | Larger pipe upstream of a smaller one |
| W-SS-007 | n below 0.013 |
| W-SS-008 | Manhole spacing too long |
| W-SS-009 | Cover below the minimum |
| W-SS-010 | Drop across a manhole below the required drop |
| W-SS-011 | Outlet crown above the lowest inlet crown |
| W-SS-012 | Drop manhole needed |
| W-SS-013 | Slope over 20 % (anchors) |
| W-SS-014 | Peak flow above the open-channel capacity (surcharged) |
| W-SS-015 | Marginal or common H2S potential |
| W-SS-016 | No listed size meets d/D |
| W-SS-017 | Network pipe not circular (treated as circular) |
| I-SS-002 … 006 | Criteria in use, no I/I, H2S formula, sewers over 24 in., network source |
