# TR-55 hydrology (NRCS curve number, Tc, peak discharge, unit hydrograph)

Tool id `tr55-hydrology` (free). It fills in the four TR-55 worksheets for one or more basins and draws each
basin's runoff hydrograph. Source: USDA NRCS *Urban Hydrology for Small Watersheds*, Technical Release 55
(2nd ed., June 1986), and NEH Part 630 Ch. 16 for the unit hydrograph.

**Rapid City.** IDCM 2012 §4.2.1 allows the rational method up to 160 ac and otherwise requires HEC-HMS with
Green–Ampt losses (§4.3.3) under the Table 4-2 frequency storm. Curve-number results are therefore for
comparison, for SDDOT or county work, or for a reviewer's check. Basins over 160 ac are flagged (I-TR-005)
with `criteria = rapid city` (the default). For the City's own method use **Hydrograph network routing**
(premium), which runs Green–Ampt with IDCM Table 4-4 soils.

| Step | Method | Notes |
|---|---|---|
| Rainfall | 24-h depth per storm, `P<year> =` | Default: IDCM Table 4-2 24-h depths (2-yr 2.00, 10-yr 3.07, 100-yr 4.55 in). Use NOAA Atlas 14 depths for other sites. |
| Curve number (Worksheet 2) | TR-55 Tables 2-2a–2-2d, area-weighted | `[Land cover]` rows by basin: a cover key and HSG, or a CN. ARC II. |
| Impervious adjustment | Fig. 2-3: CNc = CNp + (Pimp/100)(98 − CNp); Fig. 2-4 (Pimp < 30 %): × (1 − 0.5R) | `Impervious (%)` and `Unconnected (%)` on a pervious cover row. R = unconnected / total impervious. |
| Runoff | Q = (P − Ia)² / (P − Ia + S), S = 1000/CN − 10, Ia = 0.2S | Eqs. 2-1 to 2-4 |
| Tc (Worksheet 3) | Sheet Eq. 3-3; shallow concentrated Fig. 3-1 (V = 16.1345√s unpaved, 20.3282√s paved); channel Eq. 3-4 | Sheet flow over 100 ft warned (NEH 630 Ch. 15, 2010), over 300 ft refused. Or enter Tc under `[Basins]`. |
| Graphical peak (Worksheet 4) | qp = qu Am Q Fp; log qu = C0 + C1 log Tc + C2 (log Tc)² (Appendix F, Table F-1) | Types I, IA, II, III. Ia/P limited to 0.1–0.5 and Tc to 0.1 h (TR-55's instructions); qu linear between tabulated Ia/P. Tc > 10 h: no graphical peak. |
| Pond and swamp factor | Table 4-2: 0, 0.2, 1, 3, 5 % → 1.00, 0.97, 0.87, 0.75, 0.72 | Linear between; over 5 % the method does not apply (W-TR-006). |
| Hydrograph | NRCS dimensionless UH, peak rate factor 484, lag = 0.6 Tc, convolved with CN excess | `hydrograph storm = type-ii` (24-h Type II) or `rapid-city` (IDCM frequency storm, `storm duration`), or `none`. Δt ≤ 0.133 Tc. |

### Cover keys (TR-55 Tables 2-2a–2-2d)

`open space poor|fair|good`, `impervious`, `paved curb`, `paved ditch`, `gravel road`, `dirt road`,
`desert natural`, `desert artificial`, `commercial`, `industrial`, `residential 1/8|1/4|1/3|1/2|1|2`,
`newly graded`, `fallow bare`, `row crops sr|c poor|good`, `small grain sr poor|good`,
`pasture poor|fair|good`, `meadow`, `brush poor|fair|good`, `woods grass poor|fair|good`,
`woods poor|fair|good`, `farmsteads`, `herbaceous`, `oak aspen`, `pinyon juniper`, `sagebrush` (each
`poor|fair|good`, no group A), `desert shrub poor|fair|good`. Any other cover: enter its CN.

### Outputs

Worksheet 2, 3 and 4 tables; a unit hydrograph table with the peak, time to peak, volume and the difference
from the graphical peak; one hydrograph chart per storm with every basin. HTML, PDF and Excel as for every
tool.

### Validation

* TR-55 Example 4-1 (250 ac, CN 75, Tc 1.53 h, Type II, P 6.0 in): Ia 0.667 in, Q 3.28 in, qu 269 csm/in
  (TR-55 reads 270 from Exhibit 4-II), qp 344 cfs (TR-55: 345). Test `Example41_GraphicalPeak_MatchesTr55`.
* TR-55 Example 3-1 Tc: sheet 0.30 h, shallow 0.24 h, channel V 2.05 ft/s and 0.99 h, Tc 1.53 h, all reproduced.
* Example 2-2 composite CN 75.2; Figure 2-3 and 2-4 equations checked by hand.
* The unit hydrograph's volume equals Q × A to 0.005 ac-ft in every sample basin; its peak is within 12 % of
  the graphical peak in the sample (Exhibit 4 is a regression of TR-20 runs, so they are not expected to agree
  exactly).

Decisions (D-100 to D-102):

* D-100: free tool. It is single-basin hydrology like the rational method, which D-098 keeps free; the
  multi-basin routed model stays premium (Hydrograph network routing). Moving it to premium is one line in
  `PremiumFeatures`.
* D-101: Table F-1 coefficients in place of digitizing Exhibit 4; qu is interpolated linearly between Ia/P
  values (the way TR-55 tells users to read between the Exhibit 4 curves).
* D-102: Hydrographs only for the Type II distribution (all of South Dakota is Type II) and the IDCM storm;
  types I, IA and III get the graphical peak only.
