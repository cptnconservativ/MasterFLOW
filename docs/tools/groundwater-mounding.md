# Groundwater mounding under infiltration basins (Hantush)

Tool id `groundwater-mounding` (free). The rise of the water table under a rectangular infiltration basin, by the
Hantush (1967) analytical solution as programmed in USGS Scientific Investigations Report 2010-5102 (Carleton). Many
reviewers now ask for this check for infiltration basins, bioretention without underdrains and dry wells.

## Input

| Key | Meaning |
|---|---|
| `length`, `width` | Basin footprint (ft or m) |
| `recharge` | Infiltration rate over the footprint while the basin drains (ft/day or m/day; 1 in/hr = 2 ft/day) |
| `duration` | Days of recharge |
| `K` | Horizontal hydraulic conductivity (ft/day or m/day) |
| `Sy` | Specific yield |
| `saturated thickness` | Initial saturated thickness hi (water table to the base of the aquifer) |
| `floor depth to water table` | Basin floor above the seasonal high water table (optional) |
| `required separation` | Separation the state or city requires (optional) |

## Method

h² − hi² = (w·ν·t/2K)·[S*((L/2 + x)·n, (W/2 + y)·n) + S*((L/2 + x)·n, (W/2 − y)·n) + S*((L/2 − x)·n, (W/2 + y)·n) +
S*((L/2 − x)·n, (W/2 − y)·n)], with ν = K·b/Sy, n = 1/√(4νt) and S*(α, β) = ∫₀¹ erf(α/√τ)·erf(β/√τ) dτ. The average
saturated thickness b = hi + Δh/2 is iterated. Recovery after recharge stops is by superposition of an equal negative
recharge from the end of recharge. Output: the peak mound at the center, the separation to the basin floor, profiles
along the length and width, and the mound at the center against time.

## Decisions

- **D-490** S* is integrated by Simpson's rule on τ = u² (400 intervals), and erf by Abramowitz and Stegun 7.1.26.
- **D-491** The solution's limits are stated with the result: homogeneous, isotropic, unconfined aquifer on a flat base,
  uniform recharge over the rectangle. A mound above half the saturated thickness, or a recharge rate above K, is
  warned (use MODFLOW for a final design).
- **D-492** USGS SIR 2010-5102's spreadsheet examples were not available here; the tests check erf, the limits of S*,
  the no-spreading limit Δh = w·t/Sy, and recovery.

## Messages

| Code | Meaning |
|---|---|
| E-GM-001/002/003 | Geometry, recharge or aquifer values missing |
| W-GM-001 | Recharge rate above K |
| W-GM-002 | Mound above half the saturated thickness |
| W-GM-003 | Mound reaches the basin floor |
| W-GM-004 | Separation below the requirement |
