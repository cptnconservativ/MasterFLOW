# Validation results

Run 2026-11-17, .NET 10.0.115, Linux x64. Command: `dotnet test MasterFlow.slnx`.

| Test project | Passed | Failed |
|---|---|---|
| MasterFlow.Hydraulics.Tests | 107 | 0 |
| MasterFlow.Application.Tests | 24 | 0 |
| MasterFlow.IntegrationTests | 11 | 0 |
| MasterFlow.Desktop.Tests | 23 | 0 |
| **Total** | **165** | **0** |

(Updated for v0.5.0 outputs; v0.4.0 was 159/159, v0.3.0 129/129, v0.2.0 91/91, the first slice 72/72.)
Shear check by hand: rectangle b = 10 ft, y = 2 ft, S = 0.01: τ = 62.4 × (20/14) × 0.01 = 0.89143 lb/ft²,
τmax = 1.248 lb/ft²; SI rectangle b = 3 m, y = 1 m, S = 0.001: τ = 9810 × 0.6 × 0.001 = 5.886 N/m². Pass.

## Reference cases (engine vs independent calculation)
US customary unless noted. Values from `validation/hand_calcs_output.txt`.

| Case | Inputs | Reference | MasterFlow | Status |
|---|---|---|---|---|
| R1 | Rect b = 10, n = 0.013, S = 0.001, y = 2 | Q = 91.7008636875 | same to 1e-10 | Pass |
| R2 | Rect b = 10, Q = 50 | yn = 1.3355577378; yc closed form 0.9193445737 | same | Pass |
| S1 | Rect b = 3 m, n = 0.015, S = 0.001, y = 1 m (SI) | Q = 4.4991536946 m³/s | same | Pass |
| T1 | Trap b = 10, z = 2, n = 0.013, S = 0.01, y = 1.5 | Q = 247.0851422290 | same | Pass |
| T2 | Chow Ex. 6-2: b = 20, z = 2, n = 0.025, S = 0.0016, Q = 400 | Book ≈ 3.36 ft; hand calc 3.3609678403 | 3.36096784 | Pass |
| T3 | Trap b = 4, zL = 3, zR = 1.5, n = 0.03, S = 0.005, Q = 120 | yn = 2.6173254227 | same | Pass |
| S2 | Trap b = 2 m, z = 1.5, n = 0.022, S = 0.0005, Q = 8 m³/s | yn = 1.7414138067 m | same | Pass |
| V1 | Tri z = 3, Q = 20, n = 0.015, S = 0.002 | closed form yn = 1.4045170237, yc = 1.2253735000 | same to 1e-10 | Pass |
| V2 | Tri zL = 4, zR = 1, Q = 5, n = 0.016, S = 0.02 | yn = 0.6023112175, Fr = 1.771 | same | Pass |
| B1 | Rect b = 10, Q = 1e-4 | yn = 0.0004625629 (7 s.f.); yc closed form | same | Pass |
| B2 | Trap b = 50, z = 3, Q = 1e5, S = 0.0002 | yn = 64.6222522605 | same | Pass |
| B3 | Rect b = 6, Q = 300, S = 0.20 | yn = 1.1203475472 + steep warning | same | Pass |

## Circular conduit cases (validation/hand_calcs_circular.py)

| Case | Inputs | Reference | Status |
|---|---|---|---|
| P1 half full | D = 2 ft, n = 0.013, S = 0.005, y = 1 | Q = Qfull/2 exactly (closed form); 7.9982260189 cfs | Pass (1e-13) |
| P1b constants | same | Qmax/Qfull = 1.0757061, y(Qmax)/D = 0.9381812; Sfull = S/4 | Pass |
| P2/P3 Flow Calc Ex. #3 | D = 36 in, n = 0.013, S = 1 %, Q = 71.37 | lower 2.7295067672 ft, upper 2.8886196484 ft | Pass; Flow Calc 32.76 / 34.66 in vs MasterFlow 32.754 / 34.663 in |
| P4 typical | D = 1.5 ft, S = 0.004, Q = 4 | yn = 0.8392399829, yc = 0.7655222981 | Pass |
| P5 nearly empty | Q = 1e-4 cfs | yn = 0.0051717821 | Pass (1e-7, reference printed to 8 s.f.) |
| P6 steep | D = 0.6 ft, S = 5 %, Q = 1 | yn = 0.2830441087, supercritical | Pass |
| Invalid | D ≤ 0, y > D, Q > Qmax | errors E-G-001, E-P-002, E-P-001 | Pass |
| Round trips | y = 0.6, 1.2, 1.85 ft (last one above full-flow Q) | S, n, y recovered | Pass |
| T = dA/dy | numerical derivative | 1e-6 | Pass |

## Elliptical conduit cases (validation/astm_c507_check.py, hand_calcs_elliptical.py)

The scripts build each shape independently (polyline and midpoint integration with 200k strips), not
with the engine's arc formulas.

| Case | Inputs | Reference | Status |
|---|---|---|---|
| E1 all C507 sizes | 24 HE sizes, full | Tabulated waterway area | Pass, all within 0.6 % (largest 0.54 %) |
| E2 HE 36 geometry | 28.75 × 45.5 in, R1 = 12.25 in | Derived R2 = 39.253676 in; A = 7.36010652 ft²; P = 10.00888436 ft | Pass (R2 1e-7; A, P 1e-8) |
| E3 HE 36 discharge | y = 1.2 ft, n = 0.013, S = 0.005 | Q = 24.30662119 cfs | Pass (1e-8) |
| E4 HE 36 normal depth | Q = 20 cfs, same n, S | yn = 1.07482756 ft | Pass (1e-7; reference printed to 9 s.f.) |
| E5 true ellipse 2 × 3 ft | full | A = πab = 4.71238898; P = 7.93271979 (Ramanujan II) | Pass (A 1e-14, P 1e-10) |
| E6 true ellipse discharge | y = 0.8 ft, n = 0.013, S = 0.005 | Q = 8.89571643 cfs | Pass (1e-8) |
| E7 VE | VE 48 vs HE 48 | Same full area and perimeter (1e-12); rise 60 in, springline width 38.25 in | Pass |
| E8 dual depth and capacity | Qfull < Q ≤ Qmax; Q > Qmax | Two roots with W-P-001; E-P-001 | Pass |
| E9 invalid | rise or span ≤ 0; R1 out of range; y > rise | Errors, no numbers | Pass |
| E10 T = dA/dy | numerical derivative | 1e-5 | Pass |

## Irregular section cases (validation/hand_calcs_irregular.py)

The script integrates depth over station on a 200k-step grid and measures wetted ground length on a
resampled polyline; it shares no code or formulas with the engine. Tolerances set before the first run:
exact (1e-12) for shapes that are also parametric sections, 1e-6 for script values.

| Case | Inputs | Reference | Status |
|---|---|---|---|
| I1 Flow Calc Example #1 as points | (0,5) (10,0) (20,0) (30,5), n 0.013, S 1 %, y 1.5 | Equals parametric trapezoid; Q = 247.08514222 | Pass (1e-12; 1e-8) |
| I1b vertical walls | (0,4) (0,0) (8,0) (8,4) | Equals rectangle b = 8 (yn, P, yc) | Pass (1e-10) |
| I2 compound, subdivision | overbanks n 0.06, channel n 0.035, S 0.001, y 6 | A 128.3333, P 70.88369712, Q 311.83231363 | Pass |
| I2b same, Q = 1500 | water above both ends | yn 9.67513472, W-I-003 | Pass |
| I3 equal velocity | same, y 6 | Q 183.66530498 | Pass |
| I4 single n compound | n 0.035, Q 180 | roots 4.54249328 and 5.41924191; step at 5.0 ignored; highest governs, W-I-001 | Pass |
| I5 two pools | ridge at 2 ft, y 1.5 | A 7.35416667, P 12.6705197, Q 11.33544864, 2 regions, W-I-002 | Pass |
| I6 wall extension | left end 2 ft, y 2.5 | A 20.16666667, P 13.13880262, Q 100.87769199 | Pass |
| Round trips | y 0.8, 3, 5.6, 9 | S, n, y recovered | Pass |
| Critical depth | single n compound | Z(yc) = Q/√g; E(y) ≥ Ec on a 0.05 ft scan to 10 ft | Pass |
| Invalid | < 3 points, decreasing station, flat, NaN, n ≤ 0, solve n with point n | errors, no numbers | Pass |
| Text parsing | tab, comma, semicolon, spaces, header, 0+10 stationing, CRLF, bad lines | points or line-numbered errors | Pass |

Not yet done: comparison with HEC-RAS runs (needs HEC-RAS on Windows). The script implements the
documented HEC-RAS subdivision rule, so agreement is expected wherever the main channel has one n.

## Discrepancies explained
1. **B1 first run failed** at 1e-7 relative on yc: the reference was printed with 10 decimals, i.e. 7
   significant figures at 1.46e-4 ft. Not an engine error. The test now uses the exact closed form
   yc = (q²/g)^(1/3), which passes at 1e-10.
2. **Flow Calc Example #1: 247.07 vs 247.085 cfs (0.006 %).** Reproduced exactly by k = 1.48592
   instead of 1.486; the full-flow value in Example #3 (66.69 vs 66.70) supports the same explanation.
   MasterFlow intentionally keeps k = 1.486 (D-004). This is an inference about Flow Calc, not a proof.

3. **P3 first run** asserted Flow Calc's 32.76 in exactly; MasterFlow gives 32.754 in. Q = 71.37 is
   Flow Calc's own rounded output and dQ/dy is small near the crown, so ±0.005 cfs of rounding plus the
   k difference moves the depth by about 0.01 in. The comparison now uses ±0.015 in, with this
   explanation in the test; the independent hand-calc comparison remains at 1e-9.

## Manual UI inspection (2026-11-17)
App run under Xvfb on Linux; screenshots reviewed: sample project loads, editing recalculates,
solved field highlighted with value, cross-section with yn and dashed yc, results grouped, messages,
rating table. Report rendered in Chromium and printed to PDF (`docs/samples/sample-report.pdf`).
**Not yet inspected on Windows** (high-DPI, multi-monitor, native file dialogs).

### v0.3.0 elliptical UI check (Xvfb screenshot)
HE 36 standard size, n = 0.013, S = 0.5 %, Q = 20 cfs: yn shows 1.075 ft (independent 1.07482756),
Qfull 48.467 cfs, plot drawn to true scale. Fixed in this pass: the "Vertical (VE)" checkbox was clipped
(moved to its own "Orientation" row), the size list text was shortened, "Depth / diameter" now reads
"Depth / rise" for ellipses, and "Max. free-surface Q" became "Peak part-full Q" so it no longer truncates.
4. **Irregular I2 effective n** first asserted 0.035 ≤ neff ≤ 0.06; the engine gave 0.0287. The test was
   wrong: with conveyance subdivision the effective n of a compound section can be below every subarea n.
   The test now checks neff against its definition k A R^(2/3)/K.
5. **Irregular I5 wetted regions** first returned 1 instead of 2: two partly wet segments meeting at a dry
   ridge crest were counted as touching. Fixed in the engine (a region continues only if wet pieces
   actually touch); the reference value was unchanged.
