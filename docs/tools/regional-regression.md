# Regional regression peak flows, gage weighting and transfer (HDS-2)

This tool computes peak discharges at an ungaged site from the USGS regional regression equations for its region, the
method HDS-2 recommends for most highway drainage on natural streams. It also weights the result with a nearby gage on
the same stream when one exists. The tool is free.

The equations are not built in, because they differ by state and region and are updated. Copy them from the USGS
report or a StreamStats report into the `[Equations]` table. The sample's coefficients are made up.

## Input

| Key | Meaning |
|---|---|
| `[Variables]` | Name, site value, and the minimum and maximum of each basin characteristic in the report's data. Values must be above zero. When a report uses X + 1 (for example storage + 1), enter that value. |
| `[Equations]` | T (yr), C, then one exponent column per variable (the header names the variable), and optionally `SEP (%)` or `Slog` (standard error of prediction in log10 units) and `EYR` (equivalent years of record). |
| `area variable` | The variable that is the drainage area, for the gage transfer. |
| `gage area`, `gage years`, `[Gage]` | A gage on the same stream: drainage area, years of record N, and the at-site estimates (T, Q) from Bulletin 17C, for example from the flood frequency tool. |

## Method

1. Q = C·X1^b1·X2^b2·… at the site.
2. The 90 % prediction interval is Q/10^(1.645·S) to Q·10^(1.645·S). S is in log10 units; when it is given as a percent, S = √(ln(1 + SEP²))/ln 10.
3. A characteristic outside the report's range is flagged (W-RR-001).
4. At the gage, the at-site and regression estimates are weighted by years: log Qw = (N·log Qg + E·log Qr)/(N + E).
5. Transfer to the site (Sauer 1974, used by NSS and StreamStats): Qu = Qr,u·[R − (2ΔA/Ag)(R − 1)], with R = Qw/Qr at the gage. It applies when |Ag − Au|/Ag ≤ 0.5; beyond that the regression is used.

## Decisions

| ID | Decision |
|---|---|
| D-560 | Equations are typed in, not stored. No current South Dakota or other regional report could be downloaded from the build environment, and stored coefficients would go out of date. |
| D-561 | Only power-form equations are supported, which covers most USGS reports. Equations with additive terms (for example 10^(a + b·X)) need a transformed variable entered by the user. |
| D-562 | The prediction interval uses the average standard error of prediction. It does not include the site-specific leverage term (which needs the report's covariance matrix), so it is somewhat narrow for sites far from the center of the data. |
| D-563 | Weighting uses equivalent years of record, the NSS/StreamStats method. Bulletin 17C variance weighting needs the variances of both estimates and is not done here. |
| D-564 | Validation: hand calculations (RegionalRegressionTests) of the power equation, the log standard error, the weighting, and the Sauer transfer at its limits (ΔA = 0 gives R·Qr; ΔA = Ag/2 gives Qr). |
- D-563 (2026-11-25): The results table adds the ±1 standard error range (HDS-2 Eq. 6.5 to 6.7), the form HDS-2
  Example 6.2 reports. HDS-2 Examples 6.1 and 6.2 (Colorado Mountain Region, Capesius and Stephens 2009) are reproduced
  in `ManualWorkedExampleTests`: Q25 = 900 cfs, 600 to 1,300 cfs within 1 SE. HDS-2 prints no national coefficients;
  regression coefficients stay inputs from the USGS report for the region (for western South Dakota, the USGS South
  Dakota peak-flow report).
- D-565 (2026-11-25): `equations = sd-A` … `sd-G` loads the South Dakota rural equations of the SDDOT Drainage Manual
  (October 2011) Figure 7.9-C (USGS SIR 2008-5104) for that hydrologic subregion (Figure 7.9-A), with their standard
  errors of prediction, equivalent years of record and the Figure 7.9-D ranges. Site values are `CA` (mi²), `PII` (in.,
  Subregions A and B, Figure 7.9-B) and `S` (ft/mi, Subregion F). The §7.9.2.2 cautions (Subregion D above 15 mi², karst
  losses in the Black Hills Subregions F and G) and the §7.9.2.5 check for large errors are shown as messages. This
  supersedes D-560 for South Dakota; other regions still type their equations in. Rapid City projects are generally
  in Subregion F (Kyle, 2026-11-25), so the sample's South Dakota comment suggests `sd-F` with `S` in ft/mi.
- D-566 (2026-11-25): `BDF = 0…12` adds the USGS nationwide three-parameter urban equations, UQ = RC·A^b1·(13 −
  BDF)^b2·RQ^b3 (SDDOT Figure 7.11-A, the form HDS-2 recommends), applied to the rural estimate, for 0.2–100 mi².
  SDDOT Examples 7.10 (Q25 3,501, Q100 8,206 cfs) and 7.12 (UQ25 4,515, UQ100 8,714 cfs) are reproduced in
  `ManualWorkedExampleTests`; the urban values differ by 0.2 % because the manual rounds A^0.15 to 1.8.

