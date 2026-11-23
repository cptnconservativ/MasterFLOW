# ★ Climate stress test

**Tools → Climate stress test** (premium). Reruns the storm network with the design flows increased for future
rainfall and reports which pieces fail first. It answers the client question "how resilient is this design?"

## Input

| Key / section | Meaning |
|---|---|
| `network` | A network name, or `all` for every network in the project. Blank uses the selected network. |
| `runoff exponent` | Flow factor = (1 + rainfall change)^exponent. Use 1 for the Rational method (Q = CiA, same Tc). Curve-number runoff grows faster than rainfall, so use a larger exponent to bound it, or rerun the hydrology. |
| `search up to` | Highest rainfall increase (%) scanned for the order of failure (default 60 in the tool, 100 in the sample, at most 300). |
| `[Projections]` | Scenario, Rainfall change (%), Tailwater rise, Source. The tailwater rise is added to a fixed tailwater elevation. It is ignored, with a warning, when the tailwater follows the flows. |

**No projection data are bundled.** The rainfall changes in the sample are examples. Enter values from your source,
for example a NOAA Atlas 15 value or a CMIP6 change factor for the design storm, and name it in the Source column so
the memo carries it.

## Output

* **Projections compared with the design.** For each scenario it shows the flow factor, the least rim − HGL, counts below the HGL criterion, flooding and surcharged pipes, and the new failures compared with the design.
* **Order of failure.** The rainfall increase is scanned from 0 at the design tailwater. Each new failure is bisected to 0.1 %, then ranked, and the first projection that reaches it is named. Failures the design already has are listed first as "already in the design".
* **Chart.** Rim − HGL against the rainfall increase for the eight structures closest to failing, with lines for the rim and the HGL criterion.
* **Climate resilience memo.** Save… writes it to Word or PDF. It is a plain-language summary for the client, with the method and both tables.

Failure modes:

* HGL criterion: the HGL is within `HglBelowRim` of the rim.
* EGL criterion: the same check against the EGL.
* Floods: the HGL is above the rim.
* Surcharged: Q is above the full-flow capacity. This is a failure only when the criteria do not allow surcharge, which Rapid City IDCM 2012 does not. Otherwise it is listed as "allowed by the criteria".
* Velocity above maximum: the velocity exceeds the criteria's maximum pipe velocity.

## Decisions

* **D-310** Rainfall projections are user inputs with a named source, never bundled values. Projections change
  faster than software releases and differ by agency.
* **D-311** Flows scale by (1 + ΔP)^k at fixed times of concentration. k = 1 is exact for the Rational method, and the
  memo says so.
* **D-312** Only *new* failures count against a projection. Exceptions that the design already has are shown
  separately.
* **D-313** The failure-order scan uses the design tailwater, so the order reflects rainfall alone. The tailwater rise
  is applied only in the named projections.
