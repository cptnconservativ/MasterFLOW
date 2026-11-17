# Validation results

Run 2026-11-17, .NET 10.0.115, Linux x64. Command: `dotnet test MasterFlow.slnx`.

| Test project | Passed | Failed |
|---|---|---|
| MasterFlow.Hydraulics.Tests | 56 | 0 |
| MasterFlow.Application.Tests | 10 | 0 |
| MasterFlow.IntegrationTests | 10 | 0 |
| MasterFlow.Desktop.Tests | 15 | 0 |
| **Total** | **91** | **0** |

(Updated after Phase C1 circular pipes; first run of the slice was 72/72.)

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
