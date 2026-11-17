# Validation results

Run 2026-11-17, .NET 10.0.115, Linux x64. Command: `dotnet test MasterFlow.slnx`.

| Test project | Passed | Failed |
|---|---|---|
| MasterFlow.Hydraulics.Tests | 40 | 0 |
| MasterFlow.Application.Tests | 10 | 0 |
| MasterFlow.IntegrationTests | 10 | 0 |
| MasterFlow.Desktop.Tests | 12 | 0 |
| **Total** | **72** | **0** |

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

## Discrepancies explained
1. **B1 first run failed** at 1e-7 relative on yc: the reference was printed with 10 decimals, i.e. 7
   significant figures at 1.46e-4 ft. Not an engine error. The test now uses the exact closed form
   yc = (q²/g)^(1/3), which passes at 1e-10.
2. **Flow Calc Example #1: 247.07 vs 247.085 cfs (0.006 %).** Reproduced exactly by k = 1.48592
   instead of 1.486; the full-flow value in Example #3 (66.69 vs 66.70) supports the same explanation.
   MasterFlow intentionally keeps k = 1.486 (D-004). This is an inference about Flow Calc, not a proof.

## Manual UI inspection (2026-11-17)
App run under Xvfb on Linux; screenshots reviewed: sample project loads, editing recalculates,
solved field highlighted with value, cross-section with yn and dashed yc, results grouped, messages,
rating table. Report rendered in Chromium and printed to PDF (`docs/samples/sample-report.pdf`).
**Not yet inspected on Windows** (high-DPI, multi-monitor, native file dialogs).
