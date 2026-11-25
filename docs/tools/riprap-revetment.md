# Riprap bank and channel revetment (HEC-11)

Tool id `riprap-revetment` (free). Sizes rock riprap for a channel bank or bed revetment by FHWA HEC-11, *Design of
Riprap Revetment* (1989), one row per reach, and checks the granular filter between the riprap, a filter layer and the
bank soil.

## Input

| Key | Meaning |
|---|---|
| `Ss` | Specific gravity of the stone (default 2.65) |
| `shape` | `angular` (42°), `subangular` (41°) or `rounded` (40°): sets the angle of repose φ |
| `phi` | Angle of repose in degrees; overrides `shape` |

`[Reaches]`: Name, V avg (average main-channel velocity), d avg (average main-channel depth), Bank slope (H:1V), SF
(stability factor, default 1.2), Placement (`dry` or `underwater`).

`[Soils]` (optional, top to bottom): Layer, D15, D50, D85 in mm. The riprap layer is added on top automatically.

Stability factors from HEC-11 Table 2: 1.0–1.2 for uniform flow in a straight or mildly curving reach with little
wave or debris attack; 1.3–1.6 for gradually varied flow, moderate bends, or moderate waves and debris; 1.6–2.0 for
rapidly varied flow, sharp bends, ice, heavy debris or high turbulence.

## Method

- Bank angle θ = atan(1/Z); K1 = √(1 − sin²θ/sin²φ). A bank as steep as φ is an error.
- D50 = 0.001·C·Va³/(davg^0.5·K1^1.5) in ft (U.S.); 0.00594·C·Va³/(davg^0.5·K1^1.5) in m (SI).
- C = Csg·Csf, Csg = 2.12/(Ss − 1)^1.5, Csf = (SF/1.2)^1.5.
- Class: the smallest standard class (HEC-23 Design Guideline 4: Class I 6 in. to Class X 42 in.) at or above D50.
- Thickness = max(D100, 1.5·D50, 12 in.) with D100 ≈ 2·D50; × 1.5 for underwater placement.
- Filter (Terzaghi, as HEC-11 gives it) at each interface: D15 coarse/D85 fine < 5; 5 < D15 coarse/D15 fine < 40;
  D50 coarse/D50 fine < 40.

## Check

Va = 8 ft/s, davg = 6.5 ft, 1V:2H bank, angular stone, Ss = 2.65, SF = 1.2: K1 = 0.744, C = 1.000,
D50 = 0.001·512/(2.550·0.642) = 0.313 ft = 3.8 in. → Class I (6 in.), layer 1.0 ft. The test suite checks this and the
SI form of the equation (0.00594 = 0.001·0.3048^−1.5).

## Decisions

- **D-400** Angle of repose defaults are read from the flat upper part of HEC-11 Figure 4 (stone 6 in. and larger):
  42° angular, 41° subangular, 40° rounded. Smaller stone has a slightly lower φ; give `phi` for it.
- **D-401** The class table and D100 ≈ 2·D50 follow HEC-23 (2009) Design Guideline 4, which replaced the older HEC-11
  weight-based gradation. The filter check assumes riprap D15 ≈ 0.5·D50 and D85 ≈ 1.5·D50.
- **D-402** HEC-11's equation is for typical channel velocities; above 15 ft/s (4.5 m/s) the tool notes that a steep
  slope method (HEC-15 §6) or a site-specific design should confirm the size.
- **D-403** The worked examples in HEC-11 were not available as text in this environment; the tool is checked by hand
  calculation of each equation in U.S. and SI units.

## Messages

| Code | Meaning |
|---|---|
| E-RR-001/002 | Ss not above 1; unknown stone shape |
| E-RR-003/004/005/006 | Reach values missing; SF below 1; unknown placement; no reaches |
| E-RR-007 | Bank as steep as the angle of repose |
| E-RR-008 | Soil layer D15 ≤ D50 ≤ D85 not met |
| W-RR-001 | φ outside 30–45° |
| W-RR-002 | D50 above Class X |
| W-RR-003 | Bank steeper than 1V:1.5H |
| W-RR-004 | Filter rule not met at an interface |
| I-RR-001 | Velocity above 15 ft/s |
| I-RR-002 | No filter check |
