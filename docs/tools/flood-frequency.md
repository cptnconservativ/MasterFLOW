# Flood frequency (Bulletin 17C)

Tool id `flood-frequency` (free). Fits the log-Pearson Type III distribution to a gage's annual peak flows by the
methods of USGS Bulletin 17C, *Guidelines for Determining Flood Flow Frequency* (England et al., 2019). The results
are the flood quantiles (Q2 through Q500) with confidence limits, plotting positions and a frequency chart.

## Input

| Key | Meaning |
|---|---|
| `regional skew`, `regional skew SE` | Regional (generalized) skew and its standard error, from the applicable USGS skew study |
| `skew` | `station`, `weighted` (default when a regional skew is given) or `regional` |
| `low outliers` | `MGBT` (default), `none`, or a low-outlier threshold flow |
| `historic start`, `historic threshold` | First year of the historical period, and the perception threshold for its unrecorded years |
| `confidence` | Two-sided confidence interval, % (default 90) |

`[Peaks]`: Year, Peak, Code. A peak may be a number, `<x` (less than x), `>x` (greater than x) or `a-b` (between a
and b). Code `H` marks a historical peak outside the systematic record. Zero flows are allowed.

## Method

1. **Low outliers.** The Multiple Grubbs–Beck test (Cohn et al., 2013) runs on the log10 systematic peaks above zero.
   For the r-th smallest value, ω = (x(r) − mean of the larger values)/sd of the larger values. Its p-value comes
   from a noncentral t, conditional on x(r), integrated over the order-statistic density.
   - A value is a potentially influential low flood (PILF) when p < 0.005 at any rank up to n/2.
   - It is also a PILF when p < 0.10 in an unbroken run from the smallest value.
   - The low-outlier threshold (LOT) is the smallest peak kept.
2. **Observations.** Each year becomes an exact value or an interval in log space:
   - PILFs and zero flows become (0, LOT).
   - Censored and interval peaks become their stated range.
   - Each unrecorded year in the historical period becomes (0, historic threshold).
3. **EMA.** The Expected Moments Algorithm (Cohn, Lane and Baier, 1997) replaces each interval by its expected moments
   under the current fit, then updates the mean, the standard deviation (× n/(n−1)) and the skew
   (× n²/((n−1)(n−2))). It repeats until the moments stop changing. With exact data only, this gives the ordinary
   Bulletin 17B moments.
4. **Skew.** The station skew's mean square error is computed by Bulletin 17B Eq. 6 with N = systematic years. The
   weighted skew is (MSE_R·G_S + MSE_S·G_R)/(MSE_R + MSE_S). EMA then re-estimates the mean and standard deviation with
   the skew held at the value used.
5. **Quantiles.** Q = 10^(mean + K·sd). K is the exact Pearson III frequency factor from the inverse incomplete gamma
   function; it reproduces the Bulletin 17B Appendix 3 table. Below |G| = 0.01, Wilson–Hilferty is used.
6. **Plotting positions.** Weibull i/(n + 1) over the systematic years. With a historical period, Hirsch–Stedinger
   positions (a = 0) are used about the historical threshold.

## Decisions

- **D-360** The truncated Pearson III moments that EMA needs are found by integration by parts. This avoids the
  cancellation that raw gamma moments suffer at small skews. Below |G| = 0.01 the normal distribution is used.
- **D-361** The station-skew MSE uses the Bulletin 17B formula with N = systematic years, not the EMA-specific MSE in
  Bulletin 17C. Leaving the historical years out of N gives the regional skew somewhat more weight.
- **D-362** Confidence limits use the Bulletin 17B noncentral-t approximation with N = systematic years. PeakFQ
  computes the Bulletin 17C EMA variance (Cohn et al., 2001), so its limits will differ. Check against PeakFQ for
  formal submittals.
- **D-363** The Multiple Grubbs–Beck p-values were checked against a 20,000-sample Monte Carlo simulation (see the
  tests), not against PeakFQ output, which could not be run here. Neither PeakFQ nor USGS NWIS can be reached from
  the build environment.
- **D-364** One perception threshold for the whole historical period, and a perception threshold of zero (or the LOT)
  for systematic years. Peaks with several historical thresholds can be entered as `<x` or `>x` years.

## Messages

| Code | Meaning |
|---|---|
| E-FF-001 | Fewer than three systematic peaks above zero |
| E-FF-002 | Year or peak unreadable |
| E-FF-003 | Year given twice |
| E-FF-004 | Regional skew without its standard error |
| E-FF-005 | Historical period without a threshold, or starting after the systematic record |
| E-FF-006 | Unknown skew option, or weighted/regional skew without a regional skew |
| E-FF-007 | Confidence outside 50–100 % |
| E-FF-008 | Unreadable low-outlier option |
| E-FF-009 | Fewer than three peaks above the low-outlier threshold |
| W-FF-001 | Fewer than 10 systematic peaks |
| W-FF-002 | Historical peak below the historic threshold |
| W-FF-003 | Station skew only |
| I-FF-001 | PILFs found and censored |
| I-FF-002 | Use for gaged sites; ungaged Rapid City basins use IDCM hydrology or regional regression |
