# Erosion and sediment control

Tool id `erosion-sediment` (free). This tool checks a construction erosion and sediment control plan against the
Rapid City Storm Water Quality Manual (2009) Chapter 1. It also estimates the soil loss behind the plan with RUSLE.
The IDCM §4.1.7 adopts that manual, and the project precedence is IDCM, then SWQM, then the Drainage Criteria Manual.
All inputs are in U.S. units.

## Input

| Section | Columns |
|---|---|
| `R`, `R fraction`, `sediment unit weight` | Annual erosivity (AH 703 isoerodent map); share of the annual R during the exposure; deposited sediment pcf (90). |
| `[Areas]` | Name, Area (ac), Slope length λ (ft), Slope (%), K, C, P, Stabilization, Exposure (months), Basin |
| `[Perimeter controls]` | Name, Type, Length (ft), Tributary area (ac), Slope length (ft), Slope (H:V or %) |
| `[Basins]` | Name, Type (basin/trap), Tributary disturbed area (ac), Volume (ft³), Length, Width, Depth (ft), Orifice (in.) |

## Checks

- **RUSLE** (USDA AH 703): A = R·K·LS·C·P, in t/ac/yr.
  - L = (λ/72.6)^m, with m = β/(1+β) and β = (sinθ/0.0896)/(3 sin^0.8 θ + 0.56). This is the moderate rill/interrill ratio.
  - S = 10.8 sinθ + 0.03 below 9 %, and 16.8 sinθ − 0.50 at 9 % or more.
  - For slopes shorter than 15 ft, S = 3.0 sin^0.8 θ + 0.56.
  - The soil loss over the exposure is A × R fraction × area. It is also given as a volume.
- **Table 1.1** maximum exposure by stabilization method:

  | Method | Maximum exposure |
  |---|---|
  | Roughening | 1 month |
  | Mulch | 12 months |
  | Temporary revegetation | 24 months |
  | Stockpile revegetation | 2 months |
  | Early road base | 1 month |

  Slopes of 3:1 or steeper need a blanket or bonded fiber matrix (§1.2.2).
- **Table 1.2** perimeter controls. The table gives the maximum slope length at 2:1, 3:1, 4:1, 5:1 and flatter for each of these:
  - silt fence
  - 9 in. wattle
  - 8, 12, 18 and 24 in. compost socks
  - compost berm

  Further limits:
  - Silt fence and silt ditch: 0.5 ac per 100 ft. The silt ditch also needs 3,600 ft³ per acre.
  - Sod filter strip: slope length 50 ft or less, slope 6:1 or flatter, width at least 20 ft.
- **Sediment traps (§1.3.3.5)**:
  - At least 3,600 ft³ per tributary acre, or ½ in. of runoff, whichever is greater. In practice 3,600 ft³ always governs.
  - At most 5 ac.
- **Sediment basins (§1.3.3.6)**:
  - Required where more than 10 ac drains to one location. The tool warns when more than 10 ac is disturbed and there is no basin.
  - At least 3,600 ft³ per tributary disturbed acre.
  - Length at least 2 × width, when practicable.
  - Drain time of 16 h or more.
  - Clean out before the basin is half full.
  - Months to half full are estimated from the RUSLE loads of the areas that drain to the basin.

## Decisions

- **D-130** The tool uses RUSLE (AH 703) rather than RUSLE2. RUSLE2 is a daily-time-step program with a large
  database, and it cannot be reproduced as closed-form equations. RUSLE is the standard hand method that RUSLE2
  replaced, and it is used here only to judge clean-out and stabilization needs. Rapid City sets no soil loss limit.
- **D-131** Drain time is computed by falling head through one bottom orifice (Cd = 0.6), taking the plan area as
  volume ÷ depth (a prismatic basin). This is a check on the 16 h minimum. For a staged dewatering riser, use the
  outlet designer.
- **D-132** The tool does not pick an R value. The AH 703 isoerodent map gives roughly 30–50 in western South Dakota,
  and the engineer enters the site value. R fraction takes the place of the AH 703 EI-distribution tables for the
  construction period.

## Validation

- **Unit plot.** λ = 72.6 ft at 9 % gives L = 1 and S = 1.006 (hand calc).
- **Hand calc.** λ = 180 ft at 4 %: m = 0.361, L = 1.388, S = 0.462, LS = 0.641. With R = 40 and K = 0.32,
  A = 8.20 t/ac/yr.
- **Basin drain time.** 40,000 ft³ at 4.5 ft deep through a 2 in. orifice drains in 99.8 h (hand calc).
- **Table 1.1 and 1.2 limits.** The sample flags exactly the controls that the manual's tables reject.
