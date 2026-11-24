# Pond sediment accumulation and clean-out interval

Estimates how fast sediment fills a pond's forebay and main pool, and how often each needs cleaning. It works for wet ponds,
extended detention basins and dry ponds. The tool is free and works in U.S. units (acres, inches, ft³, ft², lb/ft³).

## Input

| Key | Meaning |
|---|---|
| `drainage area`, `impervious`, `annual precipitation` | ac, %, in/yr. The annual runoff is Rv·P·A/12 ac-ft with Rv = 0.05 + 0.009·I (simple method). |
| `tss` | Event mean concentration, mg/L. The load is EMC × runoff × 2.7194 lb/(mg/L·ac-ft). |
| `[Loads]` | Instead of `tss`: name, area (ac), sediment yield (t/ac/yr) for each land use. |
| `construction area`, `construction yield`, `construction years` | A disturbed area whose load (t/ac/yr reaching the pond, after the site's erosion controls) is added during the first years. The erosion and sediment control tool gives RUSLE loads. |
| `pool volume` or `trap efficiency` | Pool capacity (ac-ft) for the Brune curve, or a trap efficiency (%) from your own settling analysis. |
| `operation` | `submerged` (wet pond), `drawdown`, `considerable drawdown` or `empty` (dry pond): Lara and Pemberton operation types 1 to 4. |
| `sand`, `silt`, `clay` | Mass fractions of the trapped sediment, %, adding to 100. |
| `forebay volume`, `forebay area`, `forebay capture` | Forebay sediment storage (ft³), surface area (ft²) and the share of the trapped sediment that settles there (%, default 50 when a forebay is given). |
| `sediment storage`, `pool area` | Main pool sediment storage (ft³) and surface area (ft²). |
| `wq volume` | Water quality volume (ft³); reports the forebay as a share of it. |
| `cleanout at` | Percent full that triggers a clean-out (default 50). |
| `design life` | Years for the accumulation table and chart (default 25). |

## Method

1. Annual sediment load: TSS × annual runoff, or the sum of area × yield; plus the construction load while it lasts.
2. Trap efficiency: Brune's median curve as fitted by Dendy (1974), TE = 0.97^(0.19^log10(C/I)), with C the pool capacity and I the annual runoff; or the given value.
3. Deposit unit weight: Lara and Pemberton initial weights W1 and consolidation constants K by operation type, weighted by the sand, silt and clay fractions; Miller's average over T years, W_T = W1 + 0.4343·K·[T/(T−1)·ln T − 1].
4. Deposited volume after T years = trapped mass / W_T. The forebay takes its capture share and the main pool the rest.
5. Years to the clean-out trigger are found by bisection: once from today (including construction), and once as the repeating interval for the developed site after a clean-out.

## Output

- Load and trapping: runoff, Rv, load, C/I, trap efficiency, W1, K, average unit weight, first-year volume.
- Clean-out interval for the forebay and the main pool: trigger volume, years from today, developed interval, volume and depth per developed year.
- Forebay size as a share of the WQ volume, when given.
- Accumulation by year without clean-out, and a chart with the trigger lines.

## Decisions

| ID | Decision |
|---|---|
| D-510 | Trap efficiency defaults to the Brune median curve (Dendy fit) because it needs only the pool capacity and the annual inflow. Brune is for reservoirs with a pool, so a dry pond gives W-PSD-002 and should use `trap efficiency` from a settling analysis. |
| D-511 | Unit weights by Lara and Pemberton (1963) in lb/ft³: type 1 clay 26, silt 70, sand 97 (K 16, 5.7, 0); type 2 35, 71, 97 (K 10, 1.8, 0); type 3 40, 72, 97; type 4 60, 73, 97 (no consolidation for types 3 and 4), as tabulated in USBR Design of Small Dams. |
| D-512 | The clean-out trigger defaults to 50 % full, the Rapid City SWQM (2009) rule for sediment traps and basins. Set `cleanout at` for another rule. |
| D-513 | The forebay capture share has no default without a forebay and is 50 % with one; forebay size has no default. Both depend on the governing manual and the sediment's grading, so the tool reports the forebay as a share of the WQ volume to compare with the manual instead of sizing it. |
| D-514 | The construction load is added on top of the developed-site load during the construction years, so enter only the extra load from disturbed ground. |
| D-515 | Validation: hand calculations of the simple method, the TSS conversion, the Dendy fit, Lara–Pemberton/Miller and linear fill times (PondSedimentTests). |
