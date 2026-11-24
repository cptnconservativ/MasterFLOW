# Underdrains: pavement edge drains and bioretention

Two free tools in U.S. units:

- `pavement-edge-drain` designs pavement subsurface drainage: how much water reaches the permeable base, whether the base carries it, how fast it drains, and how far apart the edge drain outlets can be.
- `bioretention-underdrain` checks a bioretention cell's underdrain: flow through the media, the pipe's capacity, an optional restricting orifice, and drain times.

## Pavement edge drains

| Key | Meaning |
|---|---|
| `lanes`, `width` | Lanes draining to this edge (Nc = lanes + 1 longitudinal cracks) and the width W from crown to edge drain, ft. |
| `cross slope`, `grade` | Sx and S, ft/ft. The grade is also the edge drain slope. |
| `crack infiltration`, `transverse crack spacing`, `pavement permeability` | Ic (default 2.4 ft³/day/ft), Cs (default 40 ft) and kp (default 0 ft/day). |
| `base thickness`, `base permeability`, `effective porosity` | H (in), k (ft/day) and ne (default 0.25) of the permeable base. |
| `pipe diameter`, `pipe n`, `outlet spacing` | Edge drain size (in), Manning's n, and the outlet spacing to check (ft, optional). |

Method:

1. qi = Ic·(Nc/W + Wc/(W·Cs)) + kp, with Wc = W (Cedergren, FHWA-NHI-99-028).
2. The resultant slope is SR = √(S² + Sx²) and the resultant length is LR = W·√(1 + (S/Sx)²).
3. The base's steady capacity k·H·SR is compared with qi·LR.
4. Time to drain 50 % comes from the Boussinesq equation for a sloping layer, ne·∂h/∂t = ∂/∂x[k·h·(∂h/∂x + S)], solved by finite volumes. It starts with the base saturated, has a free drain at the edge and no flow at the crown.
5. The time to drain is rated by the AASHTO (1993) quality of drainage: excellent ≤ 2 h, good ≤ 1 day, fair ≤ 1 week, poor ≤ 1 month.
6. Edge drain full-flow capacity is by Manning. The maximum outlet spacing is that capacity divided by qi·W.

## Bioretention underdrain

| Key | Meaning |
|---|---|
| `area`, `ponding depth`, `media depth`, `media permeability` | Filter area (ft²), h and d (ft), k (in/hr). |
| `media drainable porosity`, `gravel depth`, `gravel porosity` | Water the media releases by gravity (default 0.20), gravel layer (default 1 ft, 0.40). |
| `native infiltration` | In/hr below the gravel (default 0, a lined cell). |
| `pipe diameter`, `pipes`, `pipe slope`, `pipe n` | Perforated underdrain(s). |
| `orifice diameter`, `orifice coefficient` | Optional restricting orifice at the outlet (in, default Cd 0.6). |
| `surface drain limit`, `subsurface drain limit` | Hours, default 48 and 72. |

Method:

1. Media flow follows Darcy's law, Q = k·A·(h + d)/d. The underdrain's Manning full-flow capacity must carry Q at full ponding.
2. Surface drain time: the falling head is stepped every 30 s. Outflow is the media flow, limited by the orifice when one is given, plus native infiltration.
3. Below the surface, the media releases its drainable water no faster than k·A (unit gradient). The gravel drains through the orifice down to its centre, or at once without an orifice.

## Decisions

| ID | Decision |
|---|---|
| D-530 | Time to drain is solved numerically instead of read from the Casagrande–Shannon / Barber–Sawyer charts. The numerical model rests on the same Boussinesq sloping-layer theory and needs no chart digitizing. For the sample, the model gives 3.6 h at m = 1.91 days, which is in line with the FHWA chart (T50 ≈ 0.08). |
| D-531 | Steady base capacity uses the gradient SR, which ignores the extra head of a thick saturated layer and so is conservative. |
| D-532 | Bioretention drain limits default to 48 h for surface water and 72 h for water in the soil and gravel, per Kyle (2026-11-20). They are inputs because manuals differ. |
| D-533 | Water below the orifice centre is treated as staying in the gravel (sump storage). An upturned-elbow internal water storage zone is not modeled. |
| D-534 | Validation uses hand calculations and closed forms: Cedergren qi, the resultant path, Manning capacity, the kinematic limit of the sloping-layer model (t50 = 0.5·L·ne/(k·S) within 5 %), the falling-head drain time (d/k)·ln((h + d)/d), and the media release d·nm/k (UnderdrainTests). FHWA-NHI-99-028 was not reachable from the build environment. |
