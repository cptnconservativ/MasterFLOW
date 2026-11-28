# Sewer drop structures and vortex drop shafts

Tool id `sewer-drop-structures` (free, U.S. units). It covers three things for sanitary (and combined) sewers:

- Reaches that run faster than 10 ft/s at half depth. Rapid City IDCM §3.12.6 item 3 allows these only with
  written approval, and then with vortex-type energy-dissipating manholes and anchors.
- Classing each manhole drop by its height.
- Sizing tangential vortex drop shafts, with a baffle drop shaft as the alternative.

## Input

Keys: `n` (0.013), `max velocity` (10 ft/s at half depth), `drop pipe height` (2 ft), `vortex drop height` (15 ft),
`max drop per structure` (30 ft), `vortex k` (1.2), `overload` (1.5), `min shaft diameter` (24 in.), and
`baffle thickness` (0.5 ft).

Tables:
- `[Reaches]`: pipe, length (ft), D (in.), up invert, down invert, peak flow (gpm).
- `[Drops]`: manhole, inlet invert, outlet invert, peak flow (gpm), inlet D (in.), inlet slope (%), outlet D (in.),
  outlet slope (%). The slopes are optional, and the outlet D defaults to the inlet D.

Either table can be left out.

## Method

- **Reach velocity.** V at half depth = V full = (1.486/n)(D/4)^(2/3)√S. The peak-flow depth and velocity come from
  Manning (the sanitary sewer tool's functions). A reach over the limit is regraded at the slope that gives the limit,
  but never below the Ten States minimum. The drop to take out, L·(S − S_allow), is split evenly into structures of at
  most `max drop per structure`. Each one is then classed and sized like an entered drop.
- **Anchors** (Ten States §33.45) on slopes of 20 % or more: centers of at most 36 ft up to 35 %, 24 ft up to 50 %,
  and 16 ft at 50 % and over.
- **Drop class.** Under 2 ft, fillet the invert with no drop pipe (Ten States §34.2). From 2 ft, an outside drop
  connection: the IDCM allows drop manholes only with written permission, and never internal drops (§3.12.10). At
  `vortex drop height` or more, or when the inlet runs over the velocity limit, a vortex drop shaft. The inlet depth is
  the normal depth when the inlet slope is given, otherwise critical depth. The table also gives the inlet Froude
  number and the free-fall impact velocity √(Vo² + 2gh).
- **Tangential vortex shaft.**
  - Size: D = k·(Q²/g)^0.2, from Jain (1984) and Yu and Lee (2009), who give k = 1.0 to 1.3. The shaft is made at
    least the inlet pipe and the minimum size, then rounded up to the next standard size.
  - Reported: the capacity Froude number Fc = Q/√(gD⁵), and the flow at which the chosen shaft reaches k = 1.0
    (Q = √g·(D/1.0)^2.5).
  - Overload: that flow should be at least `overload` × the peak. Tests held the air core to about 1.5 times the
    design flow, and it collapsed at 1.7 times.
  - Intake: the critical depth (Q²/(gB²))^⅓ for an intake channel as wide as the shaft, and whether the approach is
    subcritical or supercritical.
  - Energy to dissipate: 1 − Hu/Ho. Ho is the drop plus the inlet depth plus Vo²/2g, measured above the outlet invert.
    Hu is the outlet pipe's depth plus V²/2g. Crispino et al. (2021) measured 95 to 99 % overall in a vortex shaft
    with a dissipation chamber.
- **Baffle drop alternative**, from Odgaard, Lyons and Craig (2013):
  - Size: D = 6.0·Q^0.4/g^0.2 (Eq. 11), rounded up to a standard size, with a centered dividing wall, B = D/2.
  - F = (Q²/(B⁵g))^⅓ (Eq. 5), and the baffle spacing h = B(F/β + hv/B) + t, with β = 0.55 and hv/B = 0.1 (Eq. 8).
  - The baffle count is drop/h, and the plunge criterion 2(h/D)(B/D)F + (B/D)²F²/β must be under 0.1 (Eq. 7).
  - The calibrated range is 0.12 < F < 0.23. With B = D/2 from Eq. 11, F is always (1/243)^⅓ = 0.160, until rounding
    lowers it.

## Checks against published data

- Jain's relation: Q = 19.4 L/s with k = 1.22 gives D = 0.160 m. That is the model shaft of the 2023 *Scientific
  Reports* study (s41598-023-28762-2).
- Odgaard et al. (2013), York prototype: Q = 16.2 m³/s. Eq. 11 gives D = 11.6 m against the 12 m built, and B = 6 m
  gives F = 0.151 against the paper's 0.15. Eq. 8 gives (h − t)/B = 0.391 against the 0.39 used.
- `SewerDropStructureTests` also checks the half-full critical depth, the anchor spacing, and the regrade drop worked
  by hand.

## Decisions

- **D-775** The velocity test is at half depth (= full) as IDCM 3.12.6 item 3 words it. The peak-flow velocity is
  shown as well.
- **D-776** A vortex shaft is chosen at 15 ft of drop by default. The IDCM, Ten States and ASCE MOP 60 give no
  height, so this is a judgment value for Kyle to confirm (`vortex drop height`). An inlet over the velocity limit
  also gets a vortex shaft, as IDCM 3.12.6 names vortex-type structures for those reaches.
- **D-777** The shaft is at least the inlet pipe and 24 in., for access and to keep solids moving; the hydraulic size
  alone is often smaller at sanitary flows.
- **D-778** The intake geometry, the dissipation chamber and the air vent are not sized. The sources used give no
  closed-form air demand or chamber dimensions, and ask for physical or CFD model confirmation. The tool gives the
  numbers that start that design: D, Fc, the approach Froude number, the intake critical depth and the energy to
  dissipate.
- **D-779** Baffle drops use only the Odgaard et al. (2013) relationships, flagged outside their calibrated range.

## Messages

| Code | Meaning |
|---|---|
| E-DS-001 | SI project |
| E-DS-002 | Settings out of range |
| E-DS-003 | Reach row invalid |
| E-DS-004 | Drop row invalid (inlet below outlet, missing values) |
| E-DS-005 | Neither table given |
| W-DS-001 | n below 0.013 (IDCM 3.12.6 item 4) |
| W-DS-002 | vortex k outside 1.0–1.3 |
| W-DS-003 | Reach over the velocity limit at half depth (regrade and drops given) |
| W-DS-004 | Slope 20 % or more: anchors and spacing |
| W-DS-005 | Pipe surcharges at the peak flow |
| W-DS-006 | Outside drop connection needed (IDCM 3.12.10 permission) |
| W-DS-007 | Vortex shaft overload margin too small |
| W-DS-008 | Baffle drop plunge criterion not met |
| I-DS-001 … 005 | Anchors for fast reaches; supercritical intake; shaft set by pipe size; method; items not sized |

## References

- Jain, S.C. (1984), "Tangential vortex-inlet," *J. Hydraulic Engineering* 110(12).
- Yu, D. and Lee, J.H.W. (2009), "Hydraulics of tangential vortex intake for urban drainage," *J. Hydraulic
  Engineering* 135(3).
- Odgaard, A.J., Lyons, T.C. and Craig, A.J. (2013), "Baffle-drop structure design relationships," *J. Hydraulic
  Engineering* 139(9), 995–1002.
- Crispino, G., Contestabile, P., Vicinanza, D. and Gisonni, C. (2021), "Energy head dissipation and flow pressures in
  vortex drop shafts," *Water* 13(2), 165.
- Mulligan, S., Plant, J., Nash, S. and Clifford, E. (2019), "Vortex drop shaft structures: state-of-the-art and future
  trends," IAHR World Congress.
