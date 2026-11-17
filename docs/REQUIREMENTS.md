# Requirements

## Functional
| ID | Requirement | Status |
|---|---|---|
| F-01 | Rectangular, trapezoidal (independent side slopes), triangular sections | Done |
| F-02 | Solve for discharge, normal depth, slope or Manning's n | Done |
| F-03 | Report A, P, R, T, D, V, V²/2g, E, Fr, K, AR^(2/3), yc, Vc, Ac, Tc, Ec, Sc, Z, regime | Done |
| F-04 | U.S. customary and SI | Done |
| F-05 | Structured validation errors/warnings with stable codes | Done |
| F-06 | Rating tables in both directions, CSV and clipboard | Done |
| F-07 | Project with multiple calculations, project info; save/open versioned file | Done |
| F-08 | Report with project info, inputs, plot, results, warnings, method, trace, rating table, watermark, footer, disclaimer; PDF via print | Done |
| F-09 | Batch report of all calculations | Done |
| F-10 | Undo/redo of inputs | Done |
| F-11 | Circular and elliptical pipes, partial and full flow | Phase C |
| F-12 | Irregular sections with composite n | Phase D |
| F-13 | Comparison, quick calculator, charts | Phase E |
| F-14 | Native PDF, logo picker, title expressions, templates, autosave | Phase F |

## Non-functional
| ID | Requirement |
|---|---|
| N-01 | Engine independent of UI; deterministic; tested |
| N-02 | No placeholder calculations in production paths |
| N-03 | Never show a number after non-convergence or invalid input |
| N-04 | Local-first; no telemetry; no network needed |
| N-05 | Windows 10/11, per-monitor DPI aware, light/dark |
| N-06 | Keyboard operable; main commands have shortcuts |
| N-07 | Original UI, wording, icons and report layout |
| N-08 | Recalculation instantaneous (< 50 ms per calculation) |
