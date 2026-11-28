# Sanitary wet-weather flows: diurnal DWF, RTK RDII and SWMM export (premium)

Tool id `sanitary-wet-weather` (★ Premium, feature `sanitary-wet-weather`; U.S. units). Builds the dry- and wet-weather
hydrographs of a designed sanitary sewer network, routes them by dynamic wave for surcharge and overflows, and writes the
network as an EPA SWMM 5 `.inp` file for a design-storm surcharge run in SWMM.

The network, sizes, inverts and loads come from the project's **Sanitary sewer network design** (`sanitary-network`,
saved with the project). Paste a sanitary-network input (`[Manholes]`, `[Loads]`, `[Pipes]` and its keys) into this
tool to run it on its own; that input is designed by the sanitary-network method first.

## Input

| Key | Default | Meaning |
|---|---|---|
| `residential pattern` | `residential` | 24 hourly multipliers (midnight–1 AM first), or `residential`, `commercial`, `flat`. Residential and multi-family loads |
| `commercial pattern` | `commercial` | Commercial loads |
| `industrial pattern` | `flat` | Industrial loads |
| `public pattern` | `commercial` | Public loads |
| `point pattern` | `flat` | Point (Flow) loads |
| `rtk short`, `rtk medium`, `rtk long` | 0.010, 1, 1 / 0.015, 4, 2 / 0.020, 10, 3 | R, T (h), K of each RTK triangle, or `none` |
| `return period` | 10 | Rapid City 24-hour depth (2, 10 or 100 yr; IDCM depth–duration table) |
| `storm depth` | blank | in.; replaces the Rapid City depth |
| `storm duration`, `distribution` | 24 h, Type II | NRCS Type II (24 h only) or `uniform` |
| `storm start`, `duration` | 24 h, 72 h | From midnight of day 1; the first day settles the dry-weather flow |
| `time step` | 15 min | Hydrograph and rain step |
| `routing` | dynamic wave | or `none` (summed hydrographs only) |
| `[Sewersheds]` | load areas | Manhole, Area (ac): RDII area at each manhole |

## Method

**Dry-weather flow.** Each load's average flow (IDCM 3.12.2, from the sanitary design) times the hourly multiplier of
its land use, as a step by clock hour (a SWMM HOURLY pattern). A manhole with several land uses gets the flow-weighted
blend Σ Qᵢ·pᵢ(h) / Σ Qᵢ, which is exact for step patterns. Patterns that do not average 1.000 are rescaled (I-WW-004)
so a day's volume equals the average daily flow.

**Groundwater infiltration.** The design's `infiltration` allowance (gpd per inch-diameter-mile) of each pipe, constant,
entered at its upstream manhole. The design's `inflow` allowance (gpd/ac) is not added: the RTK hydrographs replace it
(I-WW-002).

**RDII (EPA SSOAP; SWMM 5 Hydrology Reference Manual Ch. 10).** Three triangular unit hydrographs. Triangle j has peak
2/(Tⱼ(1 + Kⱼ)) per unit rain at Tⱼ hours and recedes to zero at Tⱼ(1 + Kⱼ). For rain blocks of intensity i and length Δ
starting at t₀, RDII(t) = A · Σⱼ Σ_blocks Rⱼ · i · [Uⱼ(t − t₀) − Uⱼ(t − t₀ − Δ)], with Uⱼ the cumulative triangle (exact
convolution of the block hyetograph). Volume = ΣR × rain depth × A. 1 ac-in/h = 1.00833 cfs. No initial abstraction
(SWMM's Dmax, Drecov, Dinit are written as 0).

**Accumulation.** Each pipe's "summed" hydrograph adds the DWF, infiltration and RDII of every manhole upstream at the
same moment, with no storage or travel time.

**Routing.** MasterFlow's dynamic-wave engine (the SWMM 5 link-node method; see dynamic-wave.md) with the manhole
diameter as node area, no junction losses, overflow at the rims and a free outfall, from an empty network. Δt from the
Courant condition, 0.25 to 5 s.

**Checks.** W-WW-001 a pipe flows full (dynamic wave) or the summed peak exceeds its open-channel capacity; W-WW-002 a
manhole overflows (sanitary sewer overflow); W-WW-003 the wet-weather peak exceeds the IDCM design peak flow the pipe
was sized for; W-WW-004 peak HGL closer to the rim than the minimum cover (service backups).

**SWMM file.** Junctions with MaxDepth to the rim, the outlet as FREE outfall, conduits with the designed sizes and
inverts, `[DWF]` with one HOURLY pattern per land-use mix, infiltration as `[INFLOWS]` baselines, `[HYDROGRAPHS]` RTK1 on
rain gage RG1 with the three triangles, `[RDII]` sewershed areas, and the storm as an intensity time series.

## Check by hand

Rain 1 in/h for 2 h on 10 ac with R = 0.01/0.02/0.03: RDII volume = 0.06 × 2 × 10 = 1.2 ac-in = 4,356 ft³; the tool's
hydrograph integrates to 4,356 ft³. At 0.5 h only the rising limbs respond: Σ Rⱼ·i·peakⱼ·t²/(2Tⱼ) × A × 1.00833 cfs
(test `Rdii_VolumeIsRTimesRainTimesArea`). A dry day integrates to the average daily flow and peaks at 9 AM at
1.50 × Qave for residential loads.

## Decisions

- **D-760** The tool builds on the sanitary-network design (saved input, or pasted tables) rather than its own network
  input, so the sizes, inverts and average flows are the ones designed and submitted.
- **D-761** Default hourly patterns are MasterFlow's own typical curves (residential: low 0.40 at 4 AM, peaks 1.50 at
  9 AM and 1.30 at 8 PM; commercial and public: 0.40 at night, 1.55 to 1.78 from 8 AM to 4 PM; industrial flat). The
  IDCM gives no diurnal pattern. Replace them with metered data.
- **D-762** RTK defaults (R = 0.010/0.015/0.020, T = 1/4/10 h, K = 1/2/3, ΣR = 4.5 %) are placeholders, not
  values from a published table; the tool warns (W-WW-005) until they are entered. RDII depends on the
  sewershed and must be calibrated to flow monitoring.
- **D-763** The design storm is the Rapid City 24-hour depth from the IDCM depth–duration table used by the rational
  and hydrograph tools, distributed by NRCS Type II. IDCM §3.12 has no wet-weather design storm, so the return period is
  the engineer's (or the utility's) choice; 10 years is only the starting value.
- **D-764** The steady I/I `inflow` allowance of the design is not added to the RTK RDII (it would count rainfall
  inflow twice); the `infiltration` allowance is carried as constant groundwater infiltration.

## Messages

| Code | Meaning |
|---|---|
| E-WW-001 | SI project |
| E-WW-002 | No sanitary design saved and no tables given |
| E-WW-003 | The sanitary design has errors |
| E-WW-004 | Unreadable pattern (I-WW-004: rescaled to a mean of 1) |
| E-WW-005 | Unreadable RTK triangle, or ΣR > 1 |
| E-WW-006 | Storm input (duration, Type II not 24 h, return period not in the table, negative depth) |
| E-WW-007 | `[Sewersheds]` row invalid |
| E-WW-008 | Unknown routing |
| W-WW-001 … 004 | Surcharge, overflow, above the design peak, HGL near the rim |
| W-WW-005 | Placeholder RTK parameters |
| W-WW-006 | Storm runs past the end of the run |
| W-WW-007 | No sewershed area |
| W-WW-008 | Dynamic-wave continuity error over 5 % |
| I-WW-001 … 003 | Design source, inflow allowance replaced, SWMM file contents |
