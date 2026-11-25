# Stream stability at highway crossings (HEC-20 Level 2)

This tool runs the HEC-20 Level 2 basic engineering checks for a reach at or near a crossing. It answers:

- Does the bed move at the design flow?
- How far could the bed degrade above a grade control before it reaches the equilibrium slope or armors?
- Does the reach tend toward a braided or meandering planform?
- Which way will a proposed change in flow or sediment push the reach?

The tool is free and works in both unit systems. Grain sizes are in millimetres.

## Input

| Key | Meaning |
|---|---|
| `Q`, `bankfull Q` | Design discharge, and the bankfull discharge for the braiding threshold (defaults to Q). |
| `width`, `slope`, `n` | Active channel top width, bed slope S0 and Manning's n. |
| `D50` | Median bed size, mm. Taken from `[Gradation]` when left out. |
| `[Gradation]` | Size (mm) and percent finer of the bed material, for armoring. |
| `Shields`, `specific gravity`, `armor multiple` | F* (default 0.047), Ss (default 2.65), and ya as a multiple of Dc (default 2). |
| `sinuosity`, `control distance` | Channel length over valley length; distance to a downstream grade control. |
| `[Change]` | Ratios of new to existing `Q`, `Qs`, `D50` and `S` for Lane's relation. |

## Method

1. Wide-channel normal depth y = (q·n/(K·√S0))^(3/5), with q = Q/W. Bed shear is τ = γ·y·S0.
2. Shields critical size Dc = τ/(F*·(Ss − 1)·γ), and the critical shear of D50.
3. Equilibrium slope, where the normal-depth shear equals the critical shear of D50: Seq = [F*(Ss − 1)·D50]^(10/7)/(q·n/K)^(6/7). Degradation at the control distance L is (S0 − Seq)·L.
4. Armoring: Pc is the fraction of the bed coarser than Dc. The armor layer is ya = multiple × Dc, and the degradation until it forms is ys = ya·(1/Pc − 1).
5. Specific stream power is γ·Q·S/W. The Leopold–Wolman threshold S = 0.06·Q^−0.44 (bankfull, cfs) separates braided from meandering channels. Sinuosity is classed as straight below 1.05, sinuous below 1.5, and meandering otherwise.
6. Lane's relation: Qs·D50 against Q·S. When supply exceeds transport the reach aggrades; when transport exceeds supply it degrades. Within 5 % counts as balanced.

## Decisions

| ID | Decision |
|---|---|
| D-540 | Level 2 uses wide-channel hydraulics (R = y), as HEC-20's basic engineering analysis does. Use the river profile tool for section-by-section hydraulics. A width–depth ratio below 10 is flagged. |
| D-541 | Degradation is reported both to the equilibrium slope and to armoring. The smaller value governs. |
| D-542 | The armor layer thickness defaults to 2·Dc. It is an input because published guidance ranges from 1 to 3 times Dc. |
| D-543 | The Level 1 qualitative assessment (site visit, photos, channel classification by eye) is not scored here. It needs field observation. |
| D-544 | Validation: hand calculations (StreamStabilityTests). The equilibrium slope is checked by its defining property, y·Seq = F*(Ss − 1)·D50. HEC-20 was not reachable from the build environment. |
- D-545 (2026-11-25): Optional `D90` (mm) also reports the equilibrium slope for that size, the form HEC-20 §6.5.2
  Example Problem 2 uses (Seq = 0.000108, reproduced in `ManualWorkedExampleTests`). Example Problem 1 uses the
  log-law boundary shear (Eq. 6.15), which this tool does not compute; its armoring step uses Eq. 6.16 as here.

