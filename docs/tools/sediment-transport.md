# Sediment transport capacity and reach budget (HDS-6)

Estimates how much sand or gravel a reach can carry at a given flow using three of the HDS-6 transport equations. It then
runs a sediment continuity budget through a series of reaches, for example the approach, the bridge opening and the reach
downstream, to show where the bed aggrades or degrades during a flood. The tool is free and works in both unit systems.
Grain sizes are in millimetres. Temperature is in °C (SI) or °F (U.S.).

## Input

| Key | Meaning |
|---|---|
| `Q`, `width`, `slope`, `n` | Flow and the main reach's wide-channel geometry. |
| `D50`, `D90` | Bed sizes, mm. D90 defaults to 2.5·D50. |
| `specific gravity`, `temperature` | Ss (default 2.65) and water temperature, used for viscosity. |
| `formula` | `yang` (default), `engelund-hansen` or `mpm` for the reach budget. |
| `duration`, `porosity` | Hours at this flow, and the porosity of the deposited bed (default 0.4). |
| `[Reaches]` | Name, width, slope, n, length, from upstream to downstream. |

## Method

- Hydraulics come from the wide channel: y = (q·n/(K·√S))^(3/5), U* = √(g·y·S), θ = y·S/((Ss − 1)·D50).
- Viscosity combines Vogel's dynamic viscosity with the Thiesen density. Fall velocity is Rubey's.
- **Meyer-Peter and Müller** (bed load, gravel): θ' = (n'/n)^1.5·θ with n' = D90^(1/6)/26 (D90 in m), and Φ = 8(θ' − 0.047)^1.5.
- **Engelund–Hansen** (total load, sand): Φ = 0.1·θ^2.5/f, with f = 2gRS/V².
- **Yang (1973)** (total sand load): log Ct = 5.435 − 0.286 log(ωd/ν) − 0.457 log(U*/ω) + (1.799 − 0.409 log(ωd/ν) − 0.314 log(U*/ω))·log(VS/ω − VcrS/ω), with Ct in ppm by weight.
- The capacities are reported as ppm, volume rate of solids and short tons per day.
- Budget: the first reach is in equilibrium. Each reach receives the capacity of the reach above it, and its bed changes by ΔV = (Qs,in − Qs,out)·T/(1 − p), spread over its width × length.

## Decisions

| ID | Decision |
|---|---|
| D-550 | Three equations, chosen to cover the common bed types: Yang and Engelund–Hansen for sand, MPM for gravel. Each row states the size range it applies to. Toffaleti, Laursen–Copeland and Ackers–White are not included. |
| D-551 | Wide-channel hydraulics (R = y). For complex sections, take depth and velocity from the river profile tool and enter an equivalent width. |
| D-552 | The budget is a single steady flow held for the duration, with no armoring limit. This answers the screening question (where does the bed go, and how much?). A full hydrograph or mobile-bed model (HEC-RAS sediment) is needed for design depths. |
| D-553 | MPM uses grain roughness n' = D90^(1/6)/26, never more than the total n. |
| D-554 | Validation is by hand calculation (SedimentTransportTests): Rubey fall velocity, Engelund–Hansen against its classical form qs = 0.05·V²·√(d/(g(Ss − 1)))·θ^1.5, MPM by hand, Yang's threshold, and conservation of sediment in the budget. HDS-6 itself was not reachable from the build environment. |
