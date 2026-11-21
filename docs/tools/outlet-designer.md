# Multi-stage outlet designer (premium)

Tool id `outlet-designer`. Given a pond's stage–area table, its inflow and the allowable releases, it designs the
outlet structure from the bottom up and checks it against the Rapid City criteria. The detention tool analyses an
outlet you have drawn; this tool sizes one.

## Criteria (Rapid City)

| Item | Requirement | Source |
|---|---|---|
| Release | 2-, 10- and 100-yr peaks limited to pre-development (unless a downstream analysis shows capacity) | IDCM §4.10.3(2), §4.10.4 |
| WQCV | Storage 120 % of the WQCV above the lowest perforation; WQCV released in 40 h, no more than 50 % in 12 h | SWQM 2009 §2.2.2, §2.3.2 |
| Freeboard | 1 ft above the 100-yr level; 1 ft above the spillway passing the 100-yr, whichever is greater | IDCM §4.10.3(2), §4.10.4(4) |
| Outlet pipe | Reinforced concrete, 18 in. minimum | IDCM §4.10.5 |
| Drain time | The facility drains completely within 72 h after the 100-yr storm | IDCM §4.10.5 |

## Method

1. **Inflow.** Modified rational from `area`, `tc` and `C2`/`C10`/`C100` with IDCM Table 4-2 intensities, as in the
   detention tool. For each opening size, the storm duration (tc, then 5-min steps to 180 min) that gives the
   *highest peak outflow* is used. Or enter `[Inflow hydrograph]` columns named like `10-yr (cfs)`.
2. **Allowable.** `allowable Q10 = cfs`, or `pre C10 =` with `pre tc` for a pre-development rational peak.
3. **WQCV plate.** WQCV from `impervious` (SWQM equation, a = 1.0 at 40 h) or `wqcv = ac-ft`. The plate top
   is the level that stores WQCV × (1 + `sediment allowance`). Rows sit at `row spacing` from the bottom, and the
   hole area is set exactly so that the WQCV drains in `wq drain time` from that level. The same plate code as the
   water quality tool is used. The 12 h half-drain check is reported.
4. **Storm stages, bottom up.** Each `[Stages]` row is an orifice (diameter sized), a slot (rectangular, height
   fixed, width sized) or a weir (crest length sized). A blank invert means the previous storm's routed peak level
   (the plate top for the first stage). The size of stage *k* is the **smallest** of the sizes that make each storm
   *j ≥ k* peak exactly at its allowable with stages 1…*k* in place (bisection with storage-indication routing).
   So no larger storm is pushed over its limit by a lower opening, and each opening releases as much as it may,
   which minimises storage. When a lower stage already uses a storm's whole allowable, that storm's stage is
   left out (I-OD-005 or W-OD-007).
5. **Outlet pipe.** With `Diameter` blank, the smallest standard size from 18 in. is chosen whose capacity does
   not throttle the stages up to the 100-yr level. Capacity follows HDS-5 inlet and outlet control with a free
   outfall, the same as the detention tool. With a diameter given, the size is checked.
6. **Final routing** of every storm with the plate, the stages, the pipe and the spillway. This gives the checks,
   a stage–storage–discharge table, a rating chart, hydrographs, the WQCV drawdown and a schematic front view of
   the structure.

## Limits

* The outlet pipe follows the detention tool's HDS-5 rating. Its outlet-control term (ho = (dc + D)/2) gives no
  capacity at low stages for very large pipes, so a pipe much larger than the openings can read as throttling
  at the bottom of the pond. This is conservative, and it does not arise for usual sizes.
* The 72 h drain time is counted from the end of the 100-yr inflow until storage falls to 0.1 % of its peak.
  A 40 h plate with a 20 % sediment zone usually needs longer than 72 h by that measure, so the check is shown
  as a warning for the engineer to judge. The SWQM does not say whether its 40 h counts toward the IDCM's 72 h.

## Validation

* WQCV: I = 0.55, 40 h gives 0.2553 in. by hand, and 0.638 ac-ft over 30 ac, which the tool reproduces.
* Independent re-route: a single weir designed for a 15 cfs limit (prismatic pond, triangular inflow) is routed
  again outside the tool and peaks at 15 cfs. The peak sits where outflow crosses the falling inflow, as the
  storage equation requires.
* Sample: every storm's routed release is at or below its allowable, and the 10-yr and 100-yr limits are met
  within 0.5 % (they govern the two openings).

Decisions:

* D-105: premium. It automates design, like the optimizer.
* D-106: each stage is sized to the most restrictive of its own and every larger storm's limit, not to its own
  storm alone. Sizing to its own storm alone over-released the 10-yr in testing.
* D-107: the modified rational duration is the one with the highest *outflow* for the trial opening, not the
  highest WSE. The release is what is being limited.
