# Rational method (Rapid City IDCM 2012 §4.2)

Peak runoff for each sub-basin, for the 2-, 10- and 100-year storms (the storms of IDCM Table 4-2).

| Step | Equation / table | Notes |
|---|---|---|
| Composite C | Eq. 4-2-2, C = ΣCᵢAᵢ / Aₜ | Table 4-1 coefficients for each storm; add or replace land uses under `[Land uses]` |
| Overland time | Eq. 4-2-4, tᵢ = 1.87 (1.1 − C₅) L^0.5 S^−0.33 | L in ft, S in percent (as the IDCM states it) |
| Time of concentration | Eq. 4-2-3, t_c = tᵢ + t_t | t_t (gutter, pipe, channel travel) is entered |
| Urban limit | Eq. 4-2-5, t_c ≤ L/180 + 10 | `urban = yes`; L = flow length (blank: overland L) |
| Minimum t_c | §4.2.6–4.2.7 | 5 min urban, 10 min non-urban |
| Intensity | Table 4-2 depth at a duration of t_c ÷ t_c | log-log interpolation between tabulated durations (5–1440 min) |
| Peak flow | Eq. 4-2-1, Q = C i A | cfs, in/hr, ac |

U.S. units only: the equations are empirical in feet, acres and inches. A basin over 160 ac is flagged
(§4.2.2(3)).

**Apply to network** sets each named structure's inflow to the design-storm Q and its minor inflow to the
minor-storm Q (default 10-yr and 2-yr, matching the network's minimum-velocity check). Basins draining to
the same structure are added. The network then accumulates pipe flows by simple addition (D-047), which is
conservative compared with re-computing t_c at each junction.

Decision (D-053): Table 4-2 gives depths, not intensities; i is depth ÷ duration with log-log interpolation
of depth between tabulated durations, because rainfall depth is close to a power law in duration over
each interval. Checked by hand at 10 min, 10-yr: P = 0.53·(1.04/0.53)^(ln 2/ln 3) = 0.811 in., i = 4.87 in/hr.

## Flow paths, IDF data and network routing (D-059 to D-061)

**Flow paths** (`[Flow paths]`, one row per reach, top of the basin down; slopes in percent). A basin with
reaches gets ti from its overland and sheet reaches and tt from the rest; an entered travel time is added to tt.
When the flow length is blank, the urban limit (Eq. 4-2-5) uses the total path length.

| Type | Equation | Details |
|---|---|---|
| overland | IDCM Eq. 4-2-4 with the basin's C5 | `C5:` to override |
| sheet | NRCS TR-55 Eq. 3-3, Tt = 0.007(nL)^0.8 / (P2^0.5 s^0.4) h | `n:`, `P2:` (default: the 2-yr 24-hr depth of the IDF, 2.00 in. for Table 4-2); over 300 ft warned |
| shallow | TR-55 App. F, V = 16.1345√s unpaved, 20.3282√s paved | `surface: paved` or `unpaved` (or type `shallow paved`) |
| gutter | V = Q/A from IDCM Eq. 4-4-1 = (1.12/n) Sx^0.67 S^0.5 T^0.67 | `n:`, `Sx:` (%), `T:` spread (ft) |
| pipe | Manning flowing full (= half full, IDCM §4.2.6(2)) | `D:` (in.), `n:` |
| channel | Manning bank-full trapezoid (IDCM §4.2.6(3)) | `b:`, `z:`, `d:` (ft), `n:` |
| velocity | entered velocity | `V:` (ft/s) |

**IDF data**: Table 4-2 by default. `[IDF]` takes a table of intensities (in./hr) or depths (in.; `values = depth`)
with a column per storm; `[IDF equation]` takes i = a / (t + b)^c per storm with `min duration` / `max duration`.
Storms must be 2, 5, 10 or 100 years, the storms with Table 4-1 coefficients. Intensities are never
extrapolated beyond the data. Intensity rising with duration, or a rarer storm with a lower intensity, is warned.

**Network routing** (`network flows = rational`, the default). For each pipe from the top of the system down:
ΣCA of every sub-basin upstream; tc = the longest of the sub-basin tc at its upstream structure and (tc +
travel time) of each pipe entering it (IDCM §4.2.7); Q = i(tc)·ΣCA plus any other inflow. Travel time is
L / V with V flowing full (`pipe velocity = full`, the IDCM §4.2.6(2) basis) or at normal depth for the
pipe's Q (`pipe velocity = normal`; Q/A full when surcharged). The design and minor storms are routed
separately; applying writes both Q into every pipe and the sub-basin Q as structure inflows.
`network flows = sum` keeps the earlier behaviour (D-047), and now clears any typed pipe Q so the
inflows actually accumulate.

Decision (D-059): Q in a pipe is never less than the peak of a single sub-basin at its upstream structure or of
any pipe entering it. IDCM §4.2.7 warns that a lower part of a basin can give a larger peak than the whole
basin; taking the larger of the two at every pipe is the usual way to honour that without enumerating every
partial area.

Decision (D-060): inflow typed at a structure that has no sub-basin (offsite or bypass flow) has no C·A or tc, so
it is added to the rational Q unchanged at that structure and every pipe below it.

Decision (D-061): the HTML report draws the IDF curves (log-log) with each sub-basin's tc marked, so the
intensities can be checked by eye against the source curves. The report prints to PDF from the browser.

Validation: `validation/hand_calcs_rational.py` (independent, from the equations) for a three-basin network
with every reach type, and TR-55 Example 3-1 for sheet and shallow concentrated flow. Tests in
`tests/MasterFlow.IntegrationTests/RationalHydrologyTests.cs`.
