# MasterFlow roadmap

Status as of 2026-11-17. Effort is engineering days (one developer). Risk: L/M/H.

## Phase A — Foundation  ✅ complete
| Item | Scope | Deps | Tech risk | Eng. risk | Acceptance | Tests | Effort | Status |
|---|---|---|---|---|---|---|---|---|
| A1 Repository & build | Solution, warnings-as-errors, .gitignore | — | L | — | `dotnet build` clean | build | 0.5 | ✅ |
| A2 Architecture | Layered projects, dependency direction | A1 | L | — | Engine has no UI refs | review | 0.5 | ✅ |
| A3 Domain & units | Unit systems, sections, inputs, project | A2 | L | M (units) | No hidden conversion | unit tests | 0.5 | ✅ |
| A4 Solver framework | Brent + expansion + failure reporting | A2 | M | M | Failure never yields a number | solver tests | 0.5 | ✅ |
| A5 CI | GitHub Actions build+test on Windows and Linux | repo hosting | L | — | Green pipeline | CI | 0.5 | ⬜ (no remote repo yet) |
| A6 Core docs | Methods, validation, decisions, architecture | — | L | — | Reviewed by engineer | — | 1 | ✅ (awaiting review) |

## Phase B — Core regular channels  ✅ complete (pending engineer review)
| Item | Scope | Deps | Tech | Eng | Acceptance | Tests | Effort | Status |
|---|---|---|---|---|---|---|---|---|
| B1 Rect/trap/tri geometry | A, P, T, R, D | A3 | L | L | Closed forms match | 1e-10 | 0.5 | ✅ |
| B2 Normal-depth solver | Brent on Q(y) | A4 | L | L | Chow Ex 6-2 | refs | 0.5 | ✅ |
| B3 Q, S, n solvers | Direct/closed form | B1 | L | L | Round trips | refs | 0.5 | ✅ |
| B4 Critical flow & regime | yc, Vc, Sc, Fr, warnings | B2 | L | M | Fr = 1 at S = Sc | refs | 0.5 | ✅ |
| B5 Desktop slice | Inputs, results, plot, undo, save/load, report, rating table | B1–B4 | M | L | Manual inspection | VM tests | 3 | ✅ (Linux); Windows check ⬜ |
| B6 n picker | Original n table with sources | B5 | L | M | Cancel never clears | UI | 1 | ⬜ |

## Phase C — Pipes and additional shapes  (in progress)
| Item | Scope | Deps | Tech | Eng | Acceptance | Tests | Effort | Status |
|---|---|---|---|---|---|---|---|---|
| C1 Circular partial flow | θ-based geometry, both normal-depth roots, Qmax at 0.938D, full-flow Q and slope, % full, d/D | B | M | M (non-unique roots) | Matches closed forms and FHWA charts | 5 categories | 3 | ✅ |
| C2 Capacity logic | Q > Qmax → "surcharged / pressurised – not modelled" error; Q between Qfull and Qmax → both depths | C1 | M | H | No silent pick of one root | tests | 1 | ✅ |
| C3 Elliptical HE/VE | ASTM C507 standard sizes (four-centred, R2 from tangency) + true ellipse for custom rise/span | C1 | M | M | All 24 C507 areas within 0.6 %; HE36 Q and yn match independent script to 1e-8 | tests | 3 | ✅ |
| C4 Pipe UI & report | Diameter in inches with explicit unit | C1 | L | L | Manual | VM | 2 | ✅ |
| C5 Froude near crown | Flag D = A/T unreliable above ~0.9D | C1 | L | M | Warning present | test | 0.5 | ✅ |

## Phase D — Irregular sections
Dependencies: B, C1 (shared non-monotonic solver handling). Tech risk M, engineering risk H.
- D1 Station/elevation editor with paste from Excel and delimiter detection — 3 d — ✅ v0.4.0 (text grid: tab/semicolon/comma/space, header skip, 1+25.50 stationing, per-line errors; a spreadsheet-style grid is UI polish, deferred)
- D2 Geometry engine: wetted sub-areas, disconnected pools, vertical walls, duplicate/reversed stations, wall-extension rule — 4 d — ✅ v0.4.0
- D3 Composite n: equal-velocity (Horton–Einstein) and conveyance subdivision (HEC-RAS style), both documented — 3 d — ✅ v0.4.0 (plus α, subarea flow split, multi-root normal and critical depth)
- D4 Cross-section graphics with stationing — 2 d — ✅ v0.4.0
- D5 HEC-RAS comparison set — 2 d — ◐ independent script implementing the documented HEC-RAS subdivision method agrees to 1e-6; runs in HEC-RAS itself need a Windows machine with HEC-RAS installed (not available here)
Acceptance: HEC-RAS normal-depth agreement within ±0.01 ft for equivalent subdivision; invalid sections rejected with clear messages.

## Phase E — Tables and comparison
Dependencies: B (C, D for pipe/irregular rows). Risk L/L.
- E1 Rating chart (Q vs y) — 1.5 d — ✅ v0.5.0
- E2 Alternative comparison, side-by-side inputs and outputs — 3 d — ✅ v0.6.0 (Compare tab, comparison report, comparison sheet)
- E3 Quick calculator panel (no project item needed) — 1.5 d — ⬜ deferred: every calculation already solves live; revisit with the UI pass
- E4 .xlsx export — 1 d — ✅ v0.6.0 (no third-party library; verified with openpyxl)

## Phase F — Projects and reports
Dependencies: B. Risk M (PDF library licensing).
- F1 Folders, sort names, recycle bin, recent files — 2 d — ✅ v0.7.0 (tree view deferred to UI pass)
- F2 Autosave and crash recovery — 1.5 d — ✅ v0.7.0
- F3 Native PDF (decide QuestPDF licence vs. PDFsharp/MigraDoc) — 3 d — ✅ v0.9.0 SkiaSharp PDF backend (MIT, already shipped with Avalonia; D-031): single, batch and comparison reports
- F4 Batch selection, logo picker, footer and watermark settings — 1.5 d — 🟡 v0.7.0 logo picker and persisted report settings; batch selection toggle remains
- F5 Title expression builder with live preview — 2 d — ✅ v0.8.0 ({Q:F1}-style fields, live preview, field list tooltip)
- F6 Original templates and user templates — 2 d — ✅ v0.8.0 (10 built-in generic templates; per-user "My templates")

## Phase G — Professional release
- G1 MSIX package / installer — 2 d — ⬜
- G2 Code signing (needs a certificate from Kyle's firm) — 1 d — ⬜
- G3 Error recovery and local-only logging — 1 d — ⬜
- G4 Performance (5000-row table under 1 s) — 0.5 d — ⬜
- G5 Accessibility (keyboard paths, automation names, contrast) — 2 d — ⬜
- G6 User guide — 2 d — ⬜
- G7 Release checklist — 0.5 d — ⬜

Every item's acceptance includes: clean build, tests passing, equations documented, failure modes
tested, independent verification, UI inspected, save/load and report checked, this file and the
parity matrix updated.
