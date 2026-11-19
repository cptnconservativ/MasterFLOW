# Water quality and LID sizing (premium)

Tool id `water-quality`. It computes the water quality capture volume (WQCV) of each sub-basin and sizes the
BMP each one drains to. It also reports the share of impervious area that is treated, for MS4 reporting.

> **Criteria to confirm.** The repository has no Rapid City water quality criteria. The equations here are the
> Mile High Flood District (MHFD) *Urban Storm Drainage Criteria Manual* Vol. 3 methods, which are common across the
> Front Range and Black Hills. The filter sizing follows Darcy's law (Maryland Stormwater Design Manual 2000).
> Confirm the jurisdiction's required volume, drain times and allowed BMPs before using a result. Every value
> below is an input.

## Input

* `regional depth` (d6, in. or mm). The WQCV is scaled by d6 / 0.43 (MHFD Eq. 3-2). 0.43 in. means no adjustment.
* `[Subbasins]`: Name, Area (ac or ha), Imperviousness (%), BMP. A blank BMP means the sub-basin is untreated.
  Imperviousness is always in percent. A value from 0 to 1 is flagged (W-WQ-001).
* One `[BMP <name>]` section per BMP, with `type =` one of `edb`, `sand filter`, `bioretention` or `infiltration`.

## WQCV

WQCV (watershed inches) = a (0.91 i³ − 1.19 i² + 0.78 i) × d6 / 0.43. Here i is imperviousness as a fraction. a
comes from the BMP's `wqcv drain time` (MHFD Table 3-2): 12 h gives 0.8, 24 h gives 0.9 and 40 h gives 1.0, linear
in between. The default is 40 h for an EDB and 12 h for the other types. Several sub-basins that drain to one
BMP use their area-weighted composite imperviousness.

## BMPs

| Type | Sizing |
|---|---|
| `edb` extended detention basin | Design volume = WQCV × (1 + `sediment allowance`, default 20 %). The stage–area rows give the storage (conic method). The WQCV level is where the storage equals the design volume. The orifice plate has rows every `row spacing` (default 4 in.), starting at the bottom row and continuing up to the WQCV level. Each row discharges Q = Cd·a·√(2g(h − z)) above its centre (Cd 0.6). The hole area per row a is set so that `drained fraction` (default 99 %) of the design volume drains in the drain time. Total outflow is proportional to a, so the drain time is proportional to 1/a, and a = T(1)/T exactly. The output gives the hole diameter with one hole per row, and the number of columns needed to keep holes ≤ `maximum hole` (2 in.). It also gives the 50 % drain time, a stage–storage–discharge table and a drawdown chart. Holes under 0.5 in. are flagged (W-WQ-003). |
| `sand filter`, `bioretention` | Darcy: Af = V·df / (k (hf + df) tf), where hf is half the `ponding depth`, df is the `media depth`, k is the `permeability` (ft/day; defaults are 3.5 for sand and 0.5 for bioretention soil) and tf is the `filter drain time` (40 h for sand, 48 h for bioretention). Storage: A = V / (ponding + `media porosity` × df), with porosity 0 by default. The required area is the larger of the two, and the output names which one governs. The drain time at that area is reported. |
| `infiltration` basin or trench | Design rate = measured `infiltration rate` / `safety factor` (default 2). The deepest storage that drains in the `maximum drain time` (72 h) is f·T / n, where n is the `void ratio` (0.4 for stone, 1.0 for an open basin). The `depth` you give is used if it is not deeper than that; otherwise the drain-time depth is used (W-WQ-004). Bottom area = V / (n·d). The bottom only infiltrates, and the sides are ignored. A measured rate below `minimum infiltration rate` (0.5 in./hr) is flagged (W-WQ-005). |

## Validation

`validation/hand_calcs_water_quality.py` checks the results independently:
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
* Warnings:
  * W-WQ-001: imperviousness looks like a fraction.
  * W-WQ-002: BMP with no sub-basin.
  * W-WQ-003: small holes.
  * W-WQ-004: depth limited by drain time.
  * W-WQ-005: low infiltration rate.
* Information:
  * I-WQ-001: method.
  * I-WQ-002: untreated sub-basins.
  * I-WQ-003: bioretention without an underdrain.

## Decisions

* D-085: MHFD Vol. 3 methods, because Rapid City's own water quality criteria are not in the project sources. Every
  coefficient is an input, so a jurisdiction's values can be entered without a code change.
* D-086: the EDB plate is sized exactly with the 1/a scaling and checked by independent time stepping. Rows start at
  the bottom and a micropool is not modelled. Set the bottom row of the stage table at the lowest orifice.
* D-087: filters take the larger of the Darcy area and the area that stores the WQCV over the media. This is
  conservative when the media porosity is unknown.
* D-088: not yet included: TSS or phosphorus removal estimates, water quality flow rates for flow-through devices,
  permeable pavement run-on limits and the EDB micropool/trickle channel. These are the next additions if Kyle
  wants them.
