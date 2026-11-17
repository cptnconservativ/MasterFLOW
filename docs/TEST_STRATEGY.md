# Test strategy

| Level | Project | What | When |
|---|---|---|---|
| Engine reference | MasterFlow.Hydraulics.Tests/ReferenceCases | Closed forms, independent hand calcs, published examples | every change |
| Engine robustness | MasterFlow.Hydraulics.Tests/ValidationAndSolverTests | Invalid input per geometry, solver failures, warnings, trace | every change |
| Application | MasterFlow.Application.Tests | Rating tables, result summary | every change |
| Integration | MasterFlow.IntegrationTests | Save/load round trip, schema refusal, damaged files, report content, sample report artifact | every change |
| View model | MasterFlow.Desktop.Tests | Recalc on edit, invalid text, undo/redo, solve-for UI state, save/load through VM | every change |
| Manual UI | — | Launch app, screenshot review (Linux/Xvfb now; Windows each release) | each milestone |
| Comparison | docs/FLOWCALC_TEST_CASES.md, future HEC-RAS set | Cross-tool checks with explained differences | per phase |

Rules: references are produced independently of the engine (`validation/hand_calcs.py`); tolerances
set before comparison (VALIDATION_PLAN.md); a failing reference is investigated, never loosened to
pass; each geometry has basic, typical, boundary, invalid and convergence cases.
Run everything: `dotnet test MasterFlow.slnx`.
