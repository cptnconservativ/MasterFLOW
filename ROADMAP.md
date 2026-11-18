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
- F4 Batch selection, logo picker, footer and watermark settings — 1.5 d — ✅ v0.7.0 logo picker and persisted report settings; v0.9.1 per-calculation batch checkbox
- F5 Title expression builder with live preview — 2 d — ✅ v0.8.0 ({Q:F1}-style fields, live preview, field list tooltip)
- F6 Original templates and user templates — 2 d — ✅ v0.8.0 (10 built-in generic templates; per-user "My templates")

## Phase G — Professional release
- G1 MSIX package / installer — 2 d — 🟡 per-user Setup.exe (NSIS, no admin rights, Start menu, .mfproj association, uninstall keeps user data) built by scripts/publish-win.sh; Kyle chose Setup.exe (D-035); Windows install test pending
- G2 Code signing (needs a certificate from Kyle's firm) — 1 d — ⬜
- G3 Error recovery and local-only logging — 1 d — ✅ v0.10.0 (daily log in %APPDATA%\MasterFlow\logs, 14 days; unexpected errors logged, recovery copy saved, app keeps running)
- G4 Performance (5000-row table under 1 s) — 0.5 d — ✅ v0.10.0 (Release: 5000-row tables ≈ 0.1 s, 5000-point noisy irregular section ≈ 0.26 s; PDF with 5000 rows ≈ 2 s)
- G5 Accessibility (keyboard paths, automation names, contrast) — 2 d — ✅ every input has a screen-reader name (test-enforced); all commands reachable by Tab/Enter and shortcuts; WCAG contrast: solved value 18:1 light / 11.8:1 dark, accent 6.8:1, report muted text 5.7:1 (AA ≥ 4.5:1). A screen-reader walk-through on Windows (Narrator) is still to do
- G6 User guide — 2 d — ✅ v0.10.0 docs/USER_GUIDE.md (checked against the code)
- G7 Release checklist — 0.5 d — ✅ docs/RELEASE_CHECKLIST.md + scripts/publish-win.sh (tests, Release performance, independent read-back, publish, zip, SHA-256); version now set only in Directory.Build.props

## Phase H — Beyond parity, ranked by gain for effort (2026-11-18)

Kyle asked (2026-11-18) for the highest gain from the least effort, each feature on its own branch with a
pull request to `main`, and for Civil 3D work to use LandXML for alignments, profiles, corridors and pipe
networks so inlet locations and data come across. Gain is 1–5 (how much engineering time it saves or
risk it removes for a drainage/roadway engineer); effort is engineering days; the order is by gain ÷
effort, adjusted where one item feeds another.

| Rank | Item | Gain | Effort (d) | Gain/effort | Branch | Status |
|---|---|---|---|---|---|---|
| 1 | H1 Lining shear and freeboard check (HEC-15 permissible shear, required freeboard, pass/fail in results and report) | 4 | 1.5 | 2.7 | `feature/lining-freeboard-check` | ✅ merged (PR #1) |
| 2 | H2 Sensitivity table (yn, V, Fr for n ± 10/20 %, slope range) | 3 | 1 | 3.0 | `feature/sensitivity-table` | ⬜ |
| 3 | ~~H3 Checker sign-off~~ dropped (Kyle, 2026-11-18: not needed) | | | | | ❌ |
| 4 | H4 **LandXML pipe networks** → one pipe calculation per pipe (shape, size, n, slope from inverts, length) and a structure list with inlet locations (northing/easting, rim, sump, inverts) | 5 | 2.5 | 2.0 | `feature/landxml-pipe-networks` | ✅ PR open |
| 5 | H5 **LandXML profiles** → slope for imported cross-sections and pipes from the profile grade at their station | 4 | 2 | 2.0 | `feature/landxml-profiles` | ⬜ |
| 6 | H6 **LandXML alignments** (lines, arcs, spirals) → station and offset of each inlet/structure along the road | 3 | 2 | 1.5 | `feature/landxml-alignments` | ⬜ |
| 7 | H7 Gutter spread and inlet capacity (FHWA HEC-22: curb-opening, grate, combination, on grade and sag; bypass carried to the next inlet) | 5 | 4 | 1.3 | `feature/hec22-gutter-inlets` | ⬜ |
| 8 | H8 **LandXML corridors**: Civil 3D has no corridor element in LandXML; it exports corridor *surfaces* (TIN) and feature lines. Read the gutter flowline feature line for longitudinal slope and cut the corridor surface at each inlet for cross slope, feeding H7 | 4 | 4 | 1.0 | `feature/landxml-corridor-surfaces` | ⬜ |
| 9 | H9 Batch from spreadsheet (CSV/xlsx of pipes or ditches → calculations) | 3 | 2 | 1.5 | `feature/batch-import` | ⬜ |
| 10 | H10 Culvert inlet/outlet control (FHWA HDS-5) | 5 | 6 | 0.8 | `feature/hds5-culverts` | ⬜ |
| 11 | H11 **Storm sewer HGL/EGL** through a typed or imported pipe network (Rapid City IDCM structure losses, tailwater, criteria; tables and profiles in app, PDF, HTML, Excel). Kyle moved this to the top (2026-11-18) | 5 | 10 | 0.5 | `feature/storm-sewer-hgl` | 🟡 PR |

Why this order: H1–H3 use values MasterFlow already computes, so they are almost free. H4–H6 and H8 are
the Civil 3D chain Kyle asked for; H4 comes first because pipe capacity works with what MasterFlow has
today, and H5/H6 make imported data complete (slopes, stations). H7 (inlets) is the payoff of the chain
and needs H6/H8 for best results but can start with typed-in slopes. H10/H11 are big but high value.

**What is inference:** that Civil 3D's LandXML export carries pipe networks as `PipeNetworks/Structs/Pipes`
and corridors only as surfaces and feature lines comes from the LandXML 1.2 schema and common
practice, not from a test export from Kyle's Civil 3D. A sample export from Kyle's version is the first
acceptance test for H4–H8.

Every item's acceptance includes: clean build, tests passing, equations documented, failure modes
tested, independent verification, UI inspected, save/load and report checked, this file and the
parity matrix updated.
