# Hydrograph network routing (★ Premium)

Runoff hydrographs for every subbasin, combined at nodes as they arrive, routed through pipes, channels and ponds.
U.S. units only (an SI project gets E-HR-001). Tool id `hydrograph-routing` (license feature of the same name).

| Step | Method |
|---|---|
| Design storm, `rainfall = rapid-city` (default) | IDCM 2012 §4.3.2 frequency storm: Table 4-2 depths for the 2-, 10- and 100-yr storms, log–log interpolated for durations 5, 10, 15 … min; 5-min incremental blocks arranged with the largest at the first quartile (`peak position = 25`) and the rest alternating after and before it. `storm duration` 60–1440 min (default 120, the IDCM design storm). The wettest 15 and 60 min of the storm equal Table 4-2 exactly |
| `rainfall = type-ii` | NRCS 24-h Type II distribution (TR-55 ordinates, linear between them); `P<year> =` 24-h depth, default the Table 4-2 24-h value |
| `rainfall = table` | `[Rainfall]` Time (min), then one cumulative depth column (in) per storm, e.g. `100-yr (in)` |
| Losses, curve number | Q = (P − 0.2S)² / (P + 0.8S), S = 1000/CN − 10 (TR-55 Eq. 2-1 to 2-4, AMC II), applied to cumulative rainfall |
| Losses, Green–Ampt (IDCM §4.3.3–4.3.6) | `Soil` (IDCM Table 4-4 class: Ks and suction filled in) or `Ks (in/h)` and `Suction (in)`; `Deficit` (default 0.20, §4.3.6), `Ia (in)` (default 0.35, Table 4-3 lawn), `Impervious (%)` or `MIA (%)` → EIA = 0.1·MIA^1.5 (Eq. 4-3-1). Pervious area fills Ia, then infiltrates at Green–Ampt capacity (exact cumulative equation within each step); impervious area has no loss, as in HEC-HMS |
| Time of concentration | `Tc (min)`, `Lag (min)`, or `[Tc flow paths]` by TR-55 Chapter 3: sheet Tt = 0.007(nL)^0.8/(P2^0.5 s^0.4) (≤ 300 ft; warned over 100 ft per NEH 630 Ch. 15), shallow V = 16.1345√s unpaved / 20.3282√s paved, channel V = 1.49 r^⅔ s^½/n |
| Transform | NRCS dimensionless unit hydrograph (NEH 630 Ch. 16 Table 16-1), PRF 484, lag = 0.6 tc, Tp = Δt/2 + lag; ordinates scaled so the unit hydrograph holds exactly 1 in of runoff; discrete convolution. Warned when Δt > 0.29 lag (D ≤ 0.133 tc) |
| Reaches | `[Reaches]` Name, From, To, Length, Slope, Shape (circle / trapezoid), Size (diameter or bottom width), Side slope, n. With a storm network selected and no `[Reaches]`, the network's pipes are the reaches (circle of the pipe rise; slope from the inverts) |
| Reach routing | Variable-parameter Muskingum–Cunge (IDCM §4.3.8): Δx = ½(cΔt + Q/(TSc)) at half the peak inflow (Ponce and Theurer 1982), K = Δx/c and X = ½(1 − Q/(TScΔx)) recomputed every step from the three-point average flow, c = dQ/dA from Manning. A pipe whose peak inflow exceeds its full-flow capacity is routed at the 0.938 D rating and flagged OVER CAPACITY (surcharge storage is not modelled; check the HGL in the storm network) |
| Ponds | `[Ponds]` Name, Node, Top of berm; `[Pond stages]` Pond, Elevation, Area; `[Pond outlets]` Pond then the detention tool's outlet columns. A pond at a node routes everything arriving there (storage indication, the detention tool's engine, §4.3.9) and its outflow leaves by the node's reach |
| Combining | Hydrographs add at nodes time step by time step, so peaks are not assumed to coincide (unlike the rational method). One outgoing reach per node; loops and splits are errors |
| Apply to network | Each pipe's design Q becomes the routed `network storm` (default 10-yr) peak flow entering it, and its minor-storm Q the `network minor storm` (default 2-yr) peak |

Output: design storms, subbasin runoff (rain, runoff depth, volume, peak, time), the TR-55 worksheet, node peaks,
reach peaks (attenuation, lag, depth/D, subreaches, volume error), pond peaks (WSE, storage, spillway flow, freeboard)
and an outfall hydrograph table per storm. Charts: outfall hydrographs, pond inflow/outflow, rainfall intensity.

The IDCM requires HEC-HMS for basins over 160 ac without a Drainage Basin Plan (§4.2.1) and names Green–Ampt and the
Snyder unit hydrograph (§4.3.3, §4.3.7). This tool uses the NRCS unit hydrograph; every result says so (I-HR-001).

## Decisions

- D-070: NRCS dimensionless unit hydrograph (with TR-55 tc) rather than Snyder: it needs only tc, which MasterFlow
  already computes, and its ordinates are published. HEC-HMS's Snyder transform is Clark-based internally and cannot
  be reproduced from Ct and Cp alone; Snyder is left for a later step.
- D-071: Muskingum–Cunge subreach length by Ponce and Theurer (1982) and X allowed down to −1. Choosing Δx for a
  Courant number of 1 and clipping X at 0 (tried first) attenuated the validation case 20 % too little and lost 10 %
  of the volume; the Ponce length matches the diffusion wave within 2.3 % with 0.7 % volume error.
- D-072: the frequency storm's blocks alternate after/before the peak block starting after it. HEC-HMS may order the
  smaller blocks differently; the wettest 5, 15 and 60 min still equal Table 4-2.

## Validation

`validation/hand_calcs_hydrograph.py` (output `hand_calcs_hydrograph_output.txt`), independent code:

- TR-55 Example 3-1 tc: 0.2959 + 0.2410 + 0.9906 = 1.528 h (TR-55: 0.30 + 0.24 + 0.99 = 1.53 h). Matched to 1e-4 h.
- CN 80, P = 5 in: Q = 2.8929 in. Exact.
- 100 ac, CN 80, tc 30 min, IDCM 100-yr 2-h storm, 1-min step: runoff 1.2952 in, 10.793 ac-ft, peak 195.07 cfs at
  57 min. MasterFlow matches to 0.01 cfs.
- That hydrograph through 10,000 ft of 20 ft rectangular channel, n 0.035, S 0.002, solved as a conservative
  finite-volume diffusion wave (50 ft cells, 0.5 s): 142.5 cfs at 102 min. MasterFlow's Muskingum–Cunge: 145.7 cfs
  at 102 min (+2.3 %), volume error 0.7 %.

Tests also check Table 4-2 nesting, Green–Ampt mass and step independence, unit hydrograph volume and PRF, a
zero-length reach, network apply (non-coincident peaks), Type II and entered rainfall, and rejection of bad input
(CN over 100, negative area, a storm not in Table 4-2, SI projects, loops, a 30-min Rapid City storm).

## Other unit hydrographs: Clark, Snyder, CUHP

Add a `UH` column to `[Subbasins]` (blank or `scs` keeps the NRCS curve) with the parameters each method needs:

| UH | Columns | Method |
|---|---|---|
| `clark` | `Clark R (min)`, plus Tc as usual | HEC-HMS synthetic time–area curve, A/At = 1.414·(t/tc)^1.5 to t/tc = 0.5 and 1 − 1.414·(1 − t/tc)^1.5 after, routed through a linear reservoir: O = CA·I + CB·O₋₁, CA = Δt/(R + Δt/2); the instantaneous UH is averaged over the step. |
| `snyder` | `Cp`, and `Ct`, `L (mi)`, `Lca (mi)` (or a Tc/lag: then tp = lag) | tp = Ct·(L·Lca)^0.3; tr = tp/5.5; tpR = tp + (Δt − tr)/4; qp = 640·Cp·A/tpR (A in mi²); peak at tpR + Δt/2. The shape is a gamma curve through that peak with exactly 1 in of volume. |
| `cuhp` | `L (mi)`, `Lca (mi)`, `Slope` (ft/ft), `Imp (%)`; optional `Ct`, `Cp` | CUHP 2.0 (MHFD 2016): tp = CT·(L·Lca/√S)^0.48 with CT and Cp from imperviousness (Figures B-7, B-8, Eq. B-28) unless entered; qp = 640·Cp·A/tp at tp + Δt/2, gamma shape. |

Every unit hydrograph is scaled to exactly 1 in of runoff, so methods differ only in timing and peak. The method and
its parameters are listed in the subbasin table's last column.

Decisions:

- **D-480** Snyder (and CUHP) give a peak and time to peak, not a full shape. HEC-HMS fits a Clark UH to them; here a
  gamma curve through the Snyder peak with the right volume is used, which keeps the peak and its timing exactly and is
  simple to check. The Corps W50/W75 widths are available from `UnitHydrographs.SnyderWidths` for comparison.
- **D-481** (revised 2026-11-25) CUHP's CT and Cp default from imperviousness by the CUHP 2.0 User Manual (MHFD,
  2016): CT from Figure B-7 (three quadratics in I), the peaking parameter P from Figure B-8 (2016 re-calibration) and
  Cp = 1.3·P·CT·A^0.45 (A ≤ 120 ac) or P·CT·A^0.30 (Eq. B-28). Entered Ct and Cp still override. The `Imp` column is
  used as the imperviousness (CUHP uses its effective, volume-weighted value). The peak is qp = 640·Cp·A/tp at
  Tp = tp + Δt/2 (Eqs. B-30, B-32, B-33), without the Snyder standard-duration adjustment; the shape stays the gamma
  curve of D-480 (CUHP draws its curve through W50 and W75). Table B-4 is reproduced in `ManualWorkedExampleTests`. Losses stay with the CN or Green–Ampt choice (CUHP's own Horton losses are not used).
- **D-482** The Clark tc comes from the subbasin's Tc (entered or TR-55 flow paths); R must be given.

Tests (`UnitHydrographTests`): the time–area curve; Clark volume, attenuation with R and the linear-reservoir
recession ratio; Snyder lag and peak equations and volume; CUHP lag; a four-subbasin run with each method giving the
same runoff volume and Clark attenuating the NRCS peak.
