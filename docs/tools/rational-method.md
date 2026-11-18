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
