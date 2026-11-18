# Decisions

| ID | Date | Decision | Alternatives | Rationale |
|---|---|---|---|---|
| D-001 | 2026-11-17 | Start a new repository named MasterFlow | Wait for a repository | Workspace empty and no GitHub link; brief says proceed |
| D-002 | 2026-11-17 | **Avalonia 11.3** for the desktop UI | WPF, WinUI 3 | Runs natively on Windows with per-monitor DPI; also runs on Linux so the UI can be built, launched and screenshot-tested in CI and in this cloud environment; XAML/MVVM familiar to WPF developers. WPF would be equally good on Windows but cannot be run in CI on Linux; WinUI 3 packaging is heavier. Avalonia 12 exists but 11.3 is mature; upgrade later |
| D-003 | 2026-11-17 | .NET 10 (LTS) | .NET 8 | Brief baseline; LTS |
| D-004 | 2026-11-17 | Manning k = 1.486 (US), 1.0 (SI); g = 32.174 / 9.80665 | k = 1.48592 exact | Matches U.S. hand calculations and FHWA; difference 0.005 % documented |
| D-005 | 2026-11-17 | Engine computes in the user's unit system with no internal conversion | Internal SI with conversion | "No hidden conversions"; traceable to hand calcs |
| D-006 | 2026-11-17 | Brent bracketed solver with post-solve residual check | Newton-Raphson | Guaranteed convergence; robust for discontinuous geometry (irregular sections later) |
| D-007 | 2026-11-17 | Regular shapes are first-class parametric sections | Points only (Flow Calc approach) | Editable dimensions, exact closed-form validation; irregular conversion available later |
| D-008 | 2026-11-17 | Project file = JSON with format id and schema version; refuse newer schemas; atomic save | Binary, SQLite | Human-readable, diffable, auditable, local-first |
| D-009 | 2026-11-17 | Reports as self-contained HTML (SVG plot) printed to PDF; native PDF later | QuestPDF now | No licensing question for v0.1; QuestPDF's community licence depends on company revenue — to decide in Phase F |
| D-010 | 2026-11-17 | Near-critical band 0.86 ≤ Fr ≤ 1.13 (warning) | Fr = 1 ± 0.1 | USACE EM 1110-2-1601 guidance |
| D-011 | 2026-11-17 | Slope entered in percent in the UI, labelled "%" | ft/ft | Practitioner convention (Flow Calc does the same); conversion is in one place in the view model |
| D-012 | 2026-11-17 | No telemetry, no licence activation | — | Local-first engineering tool |
| D-013 | 2026-11-17 | Pipe diameter entered in inches (US) / mm (SI); depth stays ft/m | All in ft | Practitioner convention; single labelled conversion in the view model |
| D-014 | 2026-11-17 | For two normal depths, report the lower as primary and the upper as "alternate"; error above Qmax | Pick upper; clamp | The lower root is the stable design depth; never silently choose; never return a number for impossible free-surface flow |
| D-015 | 2026-11-17 | Pipe plots are always drawn to true scale | Distorted | A distorted circle misleads |
| D-016 | 2026-11-17 | Standard elliptical pipes use the ASTM C507-22 Fig. 5 four-centred shape: published rise, span and R1; R2 derived from tangency | Use tabulated R2 as printed; fit a true ellipse to the tabulated area | Tabulated R2 is rounded and does not close the shape exactly; the derived R2 reproduces all 24 tabulated areas within 0.6 % (Kyle chose "standard sizes first") |
| D-017 | 2026-11-17 | Custom elliptical pipes are a true ellipse (x²/a² + y²/b² = 1) | Four-centred with user R1 | Users with non-standard sizes rarely know R1; a true ellipse needs only rise and span |
| D-018 | 2026-11-17 | SI elliptical sizes are the US inch sizes converted at 0.0254 m/in, not ASTM C507M sizes | Separate C507M table | One geometry source; C507M sizes are soft conversions of the same pipes |
| D-019 | 2026-11-17 | Irregular sections: when several depths carry Q (Q(y) falls as flat overbanks wet), report the highest as governing and list all with W-I-001 | Lowest (as for conduits); refuse | Conservative for freeboard and capacity; the upper conduit root is different (it seals the pipe). Conveyance subdivision usually removes the ambiguity |
| D-020 | 2026-11-17 | Irregular critical depth = the Fr = 1 root with the lowest specific energy among E-minima; α = 1 in E and Fr, α reported | Use α in E; first root | Definition of critical flow as minimum specific energy; HEC-RAS-like but not claimed identical |
| D-021 | 2026-11-17 | Water above an end point: extend a vertical wall (counted as wetted perimeter) and warn W-I-003; all pools below the water surface convey flow (W-I-002) | Error; ineffective-area input | Matches common practice (HEC-RAS extends walls); keeps results available while flagging the survey limit |
| D-022 | 2026-11-17 | Point n applies to the segment to the next point; blank n uses the calculation n; subareas break only where n changes | Separate n-change stations | Matches Flow Calc per-point n entry and HEC-RAS subdivision at n breaks |
| D-023 | 2026-11-17 | Shear uses γ = 62.4 lb/ft³ / 9810 N/m³ and is computed in the presentation layer from R, y, S | Temperature-dependent γ; engine field | Matches FHWA HEC-15 practice; purely derived values need no solver |
