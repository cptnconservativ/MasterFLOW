# Water quality and LID sizing (premium)

Tool id `water-quality`. It computes the water quality capture volume (WQCV) of each sub-basin and sizes the
BMP each one drains to. It also reports the share of impervious area that is treated, for MS4 reporting.

Two sets of criteria are available with `criteria =`:

* `rapid city` (the default): the City of Rapid City *Storm Water Quality Manual* (2009 edition, "SWQM"). The
  IDCM (2012) §4.1.7 says "For storm water quality requirements refer to the City of Rapid City Storm water Quality
  Manual", and the Drainage Criteria Manual §1.5.13 only says water quality "shall be a consideration". Kyle's
  order of precedence where they differ is IDCM, then the Storm Water Quality Manual, then the Drainage Criteria
  Manual, so the SWQM governs every number below.
* `mhfd`: the Mile High Flood District *Urban Storm Drainage Criteria Manual* Vol. 3 equations, with every
  coefficient an input. Use it outside Rapid City.

## Rapid City criteria (SWQM 2009)

| Item | Requirement | Source |
|---|---|---|
| WQCV | WQCV = 1.16 · a · (0.91 I³ − 1.191 I² + 0.78 I) watershed inches; I = fraction of mapped impervious area (connected and unconnected) | §2.2.2 step 3 |
| Drain-time coefficient a | 6 h 0.7, 12 h 0.8, 24 h 0.9, 40 h 1.0 (linear between in the tool) | §2.2.2 |
| Volume | WQCV / 12 × area; with design credits, × effective area (total area less the area routed through conservation areas, riparian or vegetated buffers, or vegetated swales). The depth in inches does not change. | §2.2.2, §2.4.2 |
| BMPs that provide the WQCV | Porous landscape detention (12 h), extended detention basin (40 h), sand filter extended detention basin (40 h), constructed wetland basin (24 h), bioretention (no drain time in the manual; project criteria 48 h surface, 72 h bed) | §2.2.1, §2.3.1–2.3.4, §2.3.9 |
| Supplemental only (no WQCV credit by themselves) | Constructed wetland channel, grass buffer, grass swale | §2.2.1, §2.3.5–2.3.7 |
| Redevelopment only, Growth Management Director approval | Water quality catch basins and inserts (no WQCV calculation; manufacturer sizing) | §2.2.1, §2.3.8 |
| Other BMPs (infiltration basins and trenches, proprietary devices) | Case by case, Growth Management Director approval | §2.3.10 |

The printed coefficient is 1.191, not MHFD's 1.19. The tool uses 1.191 as printed; the manual's own examples
(0.182 in. at I = 0.318, 40 h; 0.178 in. at I = 0.365, 24 h) are reproduced in the tests. The 1.16 factor is
about 0.50 in. / 0.43 in., so `regional depth` is ignored under these criteria (W-WQ-008).

BMP rules the tool applies under `criteria = rapid city`:

| Type | Rule (SWQM) | What the tool does |
|---|---|---|
| `porous landscape detention` | §2.3.1: 12 h WQCV; average depth (volume / water surface area) 6 to 12 in.; ≥ 18 in. sand-peat media (36 in. in Type D or expansive soils without an underdrain) | Surface area = WQCV / `average depth` (default 12 in.). Depth or media outside the limits: W-WQ-010. |
| `edb` | §2.3.2: storage 120 % of the WQCV (40 h) above the lowest perforation; the outlet releases the WQCV, not the design volume, over 40 h, with no more than 50 % of it in 12 h; 4 in. row spacing on the manual's charts; other outlets allowed if they meet the drain times | Plate rows from the bottom to the 120 % level; hole area set exactly so the WQCV leaves in 40 h starting from that level (the last 20 % is the sediment zone). The time for half the WQCV is reported and flagged under 12 h (W-WQ-009). A different `sediment allowance` or drain time is replaced by the manual's (W-WQ-007). |
| `sand filter` | §2.3.3: 100 % WQCV (40 h) above the sand bed; maximum design volume depth 3 ft; As = design volume / 3 ft; 18 in. sand over 8 in. gravel | Area = WQCV / `ponding depth`, with the depth capped at 3 ft (W-WQ-010 if more). Sand under 18 in.: W-WQ-010. The Darcy drain time is shown as a check only. |
| `constructed wetland` | §2.3.4: WQCV surcharge (24 h) above the permanent pool; pool ≥ 75 % of the WQCV; surcharge ≤ 2 ft; outlet releases the WQCV in no less than 24 h | Needs `permanent pool =` (E-WQ-011). Plate rows from the pool up; 99 % of the WQCV leaves in 24 h. Pool volume and surcharge depth are checked (W-WQ-010). |
| `bioretention` | §2.3.9: meets the WQCV criteria; ponding ≤ 6 in., ≤ 12 in. with collector pipes (now standard); planting soil ≥ 4 ft (1–2 ft on-line under 2 ac); area guideline 5–7 % of drainage area × C. No drain time in §2.3.9; project design criteria (set 2026-11-20): the surface ponding drains in 48 h, and the soil and gravel bed, if modeled, within 72 h so the cell is ready for the next storm. | a = 1.0 (48 h is past the manual's longest category, 40 h). Area = the larger of WQCV / ponding and the Darcy area that drains the ponding in 48 h, V·df / (k (hf + df) 2 days); the output names which governs and gives the surface drain time. Ponding capped at 12 in. (6 in. with `underdrain = no`). Soil under 4 ft: W-WQ-010. With `media porosity` (drainable) or `gravel depth`, bed drainage after the ponding = (porosity × soil depth, plus gravel porosity × depth without an underdrain) / rate, the rate being k (unit gradient), or the subgrade design rate (`subgrade infiltration rate` / `safety factor`) when lower and there is no underdrain. Surface + bed over 72 h: W-WQ-010. With `runoff coefficient`, the 5–7 % guideline is shown (I-WQ-004 if below). |
| `infiltration` | Not a listed WQCV BMP | Sized as under MHFD, flagged W-WQ-006 (approval needed, §2.3.10). |

## Input

* `criteria`: `rapid city` (default) or `mhfd`.
* `regional depth` (d6, in. or mm, MHFD only). The WQCV is scaled by d6 / 0.43 (MHFD Eq. 3-2). 0.43 in. means no adjustment.
* `[Subbasins]`: Name, Area (ac or ha), Imperviousness (%), optional Credit (ac or ha, §2.4.2 design credit area), BMP. A blank BMP means the sub-basin is untreated.
  Imperviousness is always in percent. A value from 0 to 1 is flagged (W-WQ-001).
* One `[BMP <name>]` section per BMP, with `type =` one of `edb`, `sand filter`, `constructed wetland`, `porous landscape detention`, `bioretention` or `infiltration`.

## MHFD criteria (`criteria = mhfd`)

### WQCV

WQCV (watershed inches) = a (0.91 i³ − 1.19 i² + 0.78 i) × d6 / 0.43. Here i is imperviousness as a fraction. a
comes from the BMP's `wqcv drain time` (MHFD Table 3-2): 12 h gives 0.8, 24 h gives 0.9 and 40 h gives 1.0, linear
in between. The default is 40 h for an EDB and 12 h for the other types. Several sub-basins that drain to one
BMP use their area-weighted composite imperviousness.

### BMPs

| Type | Sizing |
|---|---|
| `edb` extended detention basin | Design volume = WQCV × (1 + `sediment allowance`, default 20 %). The stage–area rows give the storage (conic method). The WQCV level is where the storage equals the design volume. The orifice plate has rows every `row spacing` (default 4 in.), starting at the bottom row and continuing up to the WQCV level. Each row discharges Q = Cd·a·√(2g(h − z)) above its centre (Cd 0.6). The hole area per row a is set so that `drained fraction` (default 99 %) of the design volume drains in the drain time. Total outflow is proportional to a, so the drain time is proportional to 1/a, and a = T(1)/T exactly. The output gives the hole diameter with one hole per row, and the number of columns needed to keep holes ≤ `maximum hole` (2 in.). It also gives the 50 % drain time, a stage–storage–discharge table and a drawdown chart. Holes under 0.5 in. are flagged (W-WQ-003). |
| `sand filter`, `bioretention` | Darcy: Af = V·df / (k (hf + df) tf), where hf is half the `ponding depth`, df is the `media depth`, k is the `permeability` (ft/day; defaults are 3.5 for sand and 0.5 for bioretention soil) and tf is the `filter drain time` (40 h for sand, 48 h for bioretention). Storage: A = V / (ponding + `media porosity` × df), with porosity 0 by default. The required area is the larger of the two, and the output names which one governs. The drain time at that area is reported. |
| `infiltration` basin or trench | Design rate = measured `infiltration rate` / `safety factor` (default 2). The deepest storage that drains in the `maximum drain time` (72 h) is f·T / n, where n is the `void ratio` (0.4 for stone, 1.0 for an open basin). The `depth` you give is used if it is not deeper than that; otherwise the drain-time depth is used (W-WQ-004). Bottom area = V / (n·d). The bottom only infiltrates, and the sides are ignored. A measured rate below `minimum infiltration rate` (0.5 in./hr) is flagged (W-WQ-005). |

## Validation

`validation/hand_calcs_water_quality.py` checks the results independently. Rapid City part:
* The SWQM's worked examples (§2.2.2 and §2.4.2).
* The sample EDB plate by explicit time stepping and a secant search: the WQCV leaves in 40 h from the 120 % level, and the time for half of it.
* The sample wetland plate above the permanent pool (99 % in 24 h) and the pool volume.
* The storage-sized bioretention, sand filter and porous landscape detention areas.

MHFD part:
* The WQCV equation.
* The orifice plate: hole area found by explicit time stepping and a secant search, without the 1/a scaling. It gives 1.5739 sq in. against the tool's 1.574, and a 50 % drain time of 7.526 h against 7.53.
* Darcy and storage areas, and the infiltration area and drain time.

Tests also cover:
* Table 3-2 interpolation and the Eq. 3-2 scaling.
* Darcy governing and infiltration limited by drain time.
* SI units.
* Every input error.

## Messages

* Errors:
  * E-WQ-001: bad d6.
  * E-WQ-002: bad sub-basin.
  * E-WQ-003: no sub-basins.
  * E-WQ-004: BMP section missing.
  * E-WQ-005: unknown type.
  * E-WQ-006: bad BMP value.
  * E-WQ-007: bad stage table.
  * E-WQ-008: basin too small.
  * E-WQ-009: no infiltration rate.
  * E-WQ-010: unknown criteria.
  * E-WQ-011: wetland without a valid permanent pool.
* Warnings:
  * W-WQ-001: imperviousness looks like a fraction.
  * W-WQ-002: BMP with no sub-basin.
  * W-WQ-003: small holes.
  * W-WQ-004: depth limited by drain time.
  * W-WQ-005: low infiltration rate.
  * W-WQ-006: infiltration needs approval (Rapid City §2.3.10).
  * W-WQ-007: a drain time or sediment allowance replaced by the manual's value.
  * W-WQ-008: regional depth ignored under Rapid City.
  * W-WQ-009: EDB releases more than half the WQCV in 12 h.
  * W-WQ-010: outside a Storm Water Quality Manual limit (depth, media, pool, surcharge).
* Information:
  * I-WQ-001: method.
  * I-WQ-002: untreated sub-basins.
  * I-WQ-003: bioretention without an underdrain.
  * I-WQ-004: bioretention below the 5–7 % × C area guideline.
  * I-WQ-005: slow media by Darcy (check only).

## Decisions

* D-085: Rapid City SWQM 2009 is the default (IDCM §4.1.7 adopts it; precedence IDCM > SWQM > Drainage Criteria
  Manual, per Kyle 2026-11-20). MHFD Vol. 3 stays available with `criteria = mhfd`, every coefficient an input.
* D-089: Rapid City details the manual leaves open: a is interpolated linearly between the four listed drain times;
  bioretention uses the project's 48 h surface and 72 h bed drainage (none is given in §2.3.9; Kyle, 2026-11-20), with a = 1.0 since 48 h is past the 40 h category, and a unit-gradient bed drainage model; the EDB plate drains the WQCV (not the 120 %
  design volume) in 40 h from the full design level, so the sediment zone stays below; the wetland drains 99 % of the
  WQCV in 24 h; sand filters are sized by storage (Darcy shown as a check), bioretention by the larger of storage and the 48 h Darcy area.
* D-086: the EDB plate is sized exactly with the 1/a scaling and checked by independent time stepping. Rows start at
  the bottom and a micropool is not modelled. Set the bottom row of the stage table at the lowest orifice.
* D-087 (MHFD criteria): filters take the larger of the Darcy area and the area that stores the WQCV over the media. This is
  conservative when the media porosity is unknown.
* D-088: not yet included: TSS or phosphorus removal estimates, water quality flow rates for flow-through devices,
  permeable pavement run-on limits and the EDB micropool/trickle channel. These are the next additions if Kyle
  wants them.
