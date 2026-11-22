# Outlet protection: riprap apron, riprap basin, plunge pool

Tool id `outlet-protection` (free). Sizes the protection at culvert and storm sewer outlets, one row per outlet.

## Input

`[Outlets]`: Name, Shape (`circular` or `box`), Span (diameter), Rise, Q, Slope (%), n, Outlet depth, Tailwater,
Channel V. Lengths are in ft (m in SI projects). Blank cells are optional:

- **Outlet depth** blank: a steep barrel (yn < yc) flows at normal depth. A mild barrel flows at critical depth, or
  at the tailwater up to the crown.
- **Tailwater** blank: treated as unknown. The apron then uses 0.4·D, as HEC-14 directs.
- **Channel V**: the downstream channel velocity, used for the IDCM Vp ≤ 3.33·Vc check.

`[Basin stone]` (Name, D50) fixes the basin stone of an outlet. `[Plunge pool]` (Name, Head HT, unit discharge q) adds
IDCM plunge pools.

## Method

**Outlet conditions.** The equivalent depth is ye = yo for a box and (A/2)^0.5 for a circular outlet. The outlet
Froude number is Fr = Vo/√(g·ye) (HEC-14).

**Riprap apron (HEC-14 §10.1)**:
- D50 = 0.2·D·(Q/(√g·D^2.5))^(4/3)·(D/TW), with 0.4·D ≤ TW ≤ D.
- The class, apron length (in culvert rises) and apron depth (in D50) come from Table 10.1:

| Class | D50 | Length | Depth |
|---|---|---|---|
| 1 | 5 in. (125 mm) | 4D | 3.5 D50 |
| 2 | 6 in. (150 mm) | 4D | 3.3 D50 |
| 3 | 10 in. (250 mm) | 5D | 2.4 D50 |
| 4 | 14 in. (350 mm) | 6D | 2.2 D50 |
| 5 | 20 in. (500 mm) | 7D | 2.0 D50 |
| 6 | 22 in. (550 mm) | 8D | 2.0 D50 |

- A culvert with a rise above 60 in. (1500 mm), or one that needs more than class 6, is sent to the basin.

**Riprap basin (HEC-14 §10.2)**:
- Pool depth: hs/ye = 0.86·(D50/ye)^−0.55·Fr − C0.
- C0 depends on TW/ye: 1.4 below 0.75, 4·TW/ye − 1.6 from 0.75 to 1.0, and 2.4 at 1.0 and above.
- The test range is 2 ≤ hs/D50 ≤ 4 and 0.1 ≤ D50/ye ≤ 0.7.
- Lengths: pool 10·hs (at least 3·Wo), apron 5·hs (at least Wo), basin = pool + apron.
- Riprap thickness: 3·D50 on the approach and 2·D50 elsewhere.
- Fr > 3 is flagged, because HEC-14 Table 1.1 limits the basin to Fr < 3.
- TW/ye > 0.75 is flagged. Riprap is then needed downstream of the basin, and that length is not computed.

**Rapid City IDCM §4.9.5**:
- Basin layout: 3·D wide at the outlet, a 3:1 flare each side, riprap side slopes no steeper than 2H:1V, and rock at
  least 1.5 × the largest stone thick.
- Checks:
  - No riprap basin where Vp > 3.33·Vc.
  - The outlet pipe is no steeper than 5 % for 5·D.
  - The tailwater is at least D/4.

**Plunge pool (IDCM Eq. 4-9-1)**: ds = 1.32·HT^0.225·q^0.54, in U.S. units. It is allowed only with a continuous low flow.

## Decisions

- **D-120** Basin stone selection. Among stones in 0.1 ft (0.01 m) steps, the tool picks the largest D50 with
  hs/D50 ≥ 2, which gives the shallowest pool. This reproduces both HEC-14 worked examples. A different stone
  (e.g. an available gradation) can be fixed under `[Basin stone]`.
- **D-121** Widths follow the IDCM §4.9.5 rule (3·D at the outlet, 3:1 flare), for the apron as well as the basin. That
  is the local criterion, and it governs over HEC-14 per the project's precedence.
- **D-122** Not sized here: the USBR Type VI and CSU impact basins and the downstream jet-decay riprap length for
  TW/ye > 0.75. Each depends on tables or figures that could not be obtained as text: IDCM Table 4-15 / Figure 4-17,
  HEC-14 Table 9.2 and Figure 10.x. The tool flags where each is needed. For a box apron, D is taken as (A/2)^0.5 of
  the full barrel. This should be checked against the HEC-14 Chapter 10 text when it is available.

## Validation

- **HEC-14 §1.2 example, CU.** 5 × 5 ft RCB, 200 cfs, yo = 2.15 ft, TW = 0.
  - HEC-14: D50 1.2 ft, hs 2.7 ft, pool 27 ft, apron 13.5 ft, basin 40.5 ft, riprap 3.6/2.4 ft.
  - MasterFlow: D50 1.2, hs 2.69, pool 26.9, apron 13.5, basin 40.4, riprap 3.6/2.4.
- **The same example in SI.** 1.524 m RCB, 5.66 m³/s, yo = 0.655 m. Both HEC-14 and MasterFlow give D50 0.38 m,
  hs 0.78 m, pool 7.8 m, apron 3.9 m, basin 11.7 m.
- **Apron hand calc.** D = 3 ft, 60 cfs, TW unknown: D50 = 10.7 in., class 4, 18 ft long, 2.57 ft deep.
- **Plunge pool hand calc.** HT = 8 ft, q = 10 cfs/ft: ds = 7.31 ft.
