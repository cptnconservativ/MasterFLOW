# Feature parity matrix

Classes: **MVP** = required for MVP · **Pro** = required for initial professional release ·
**Plan** = planned enhancement · **Diff** = intentionally different · **Out** = out of scope ·
**Inv** = needs additional investigation. Status: ✅ done · 🟡 partial · ⬜ not started.

| Feature | Flow Calc (source) | Class | MasterFlow status | Notes |
|---|---|---|---|---|
| Percent full, d/D, full-flow Q/V/slope | Yes (obs.) | MVP | ✅ | |
| Rectangular channel | Wizard → points (obs.) | MVP / Diff | ✅ | Parametric section |
| Trapezoidal (asymmetric) | Wizard → points (obs.) | MVP / Diff | ✅ | |
| Triangular (asymmetric) | Wizard → points (obs.) | MVP / Diff | ✅ | |
| Parabolic | Not seen | Plan | ⬜ | Justify with demand |
| Irregular station/elevation | Yes (obs., site) | MVP | ✅ | v0.4.0: text-grid editor with Excel paste, stationing notation, vertical walls, pools, end-wall extension with warning |
| Per-point n / composite n | Yes; Constant or Flow Conveyance methods (obs.) | Pro | ✅ | v0.4.0: per-segment n; conveyance subdivision (HEC-RAS) or equal velocity (Horton–Einstein); subarea flow table and α in results and report |
| Circular pipe partial flow | Yes (obs., site) | MVP | ✅ | Diameter in in/mm; true-scale plot |
| Dual normal depth / max discharge in pipes | Yes (obs.) | MVP | ✅ | Plus error when Q > Qmax and near-crown warning |
| Elliptical pipes (horizontal/vertical) | Yes (span/rise) | Pro | ✅ | v0.3.0: 24 ASTM C507 HE/VE standard sizes plus a true ellipse for custom rise/span; capacity, both normal depths and full-flow values as for circular pipes |
| Solve for Q / yn / S / n | Yes (obs.) | MVP | ✅ | |
| Critical depth, velocity, slope | Depth and slope (obs.) | MVP | ✅ | Also Vc, Ec, Z |
| Froude, regime | Fr (obs.) | MVP | ✅ | Regime + near-critical warning |
| Specific energy, velocity head | Yes (obs.) | MVP | ✅ | |
| Conveyance, section factor | Not seen | MVP | ✅ | |
| Velocity-depth product | Yes (obs.) | Pro | ✅ | v0.5.0: V·y, plus mean boundary shear γRS and maximum shear γyS (improvement) |
| Water elevation / HGL | Yes (obs.) | Pro | ✅ | v0.5.0: optional invert elevation gives WSE (HGL), EGL and critical WSE; irregular sections use point elevations |
| Imperial / Metric | Yes (obs.) | MVP | ✅ | |
| Units precision settings | Not seen | Pro | 🟡 | Fixed precision per value; user settings later |
| Input validation / errors | Yes (obs.) | MVP | ✅ | Structured codes |
| n reference picker | Handbook table (obs.) | Pro | ✅ | v0.5.0: 49 entries, min/normal/max from Chow Table 5-6, original descriptions; picking copies the normal value (undoable) |
| Rating table depth → Q | Yes | MVP | ✅ | |
| Rating table Q → depth | Inv | MVP | ✅ | |
| Rating chart | Inv | Pro | ✅ | v0.5.0: in the rating tab and in reports with a rating table |
| CSV / clipboard export | "Export calculations" | MVP | ✅ | Rating table and results |
| Excel (.xlsx) export | Inv | Plan | ✅ | v0.6.0: comparison sheet, one sheet per calculation (inputs, results, points, subareas, messages), rating table; full-precision numbers |
| Quick calculator | Not found | Plan / Diff | ⬜ | Deferred; calculations already solve live |
| Alternative comparison | Not found | Pro / Diff | ✅ | v0.6.0: Compare tab, landscape comparison report, Excel sheet; no unit conversion between systems |
| Project tree, folders | Yes (obs.) | Pro | 🟡 | v0.7.0: folder path per calculation ("Ditches/Phase 1"), shown in the list and used by Sort; a collapsible tree view is UI work, deferred |
| Duplicate / delete / recycle bin | Yes (obs.) | MVP | ✅ | v0.7.0: delete moves to a recycle bin saved with the project; restore any item; empty with confirmation |
| Sort names / re-sort | Yes (obs.) | Plan | ✅ | v0.7.0: by folder then title, natural number order |
| Title expressions | Yes (obs.) | Pro | ✅ v0.8.0 | {field:format} syntax, live preview, unknown fields shown as {?name} |
| Templates (factory street sections) | Yes (obs.) | Pro | ✅ v0.8.0 | 10 original generic templates (streets, ditches, flume, pipes, natural channel); user templates per user |
| Save / open / versioned file | Yes | MVP | ✅ | `.mfproj` schema 1 |
| Recent files | Yes (obs.) | Pro | ✅ | v0.7.0: last 10, per user, missing files dropped |
| Backup settings | Yes (obs.) | Plan | 🟡 | Covered by autosave recovery copy; configurable backup folder not planned unless requested |
| Autosave / recovery | Not seen | Pro | ✅ | v0.7.0: recovery copy every 60 s while unsaved; offered at next start; cleared on save/close |
| Undo / redo | Not seen | MVP | ✅ | Per calculation |
| Report | Yes | MVP | ✅ | Original HTML layout |
| PDF | Yes | MVP | 🟡 | Via browser print; native Phase F |
| Watermark | Yes | Pro | ✅ | |
| Logo, footer | Inv | Pro | ✅ | v0.7.0: logo picker (PNG/JPEG/GIF/SVG ≤ 1 MB embedded), watermark, footer and trace option saved with the project |
| Batch printing | Yes | Pro | 🟡 | Batch HTML report done; selection toggle later |
| Calculation trace on report | No | Diff | ✅ | Auditability |
| Civil 3D integration | Video title only | Inv | ⬜ | Investigate data exchange format |
| Licence activation | Yes | Out | — | Not needed |
| Telemetry | Licence checks only | Out | — | None |
| Light / dark theme | Dark-only (obs.) | Plan | ✅ | Follows Windows setting |
| Keyboard shortcuts | Inv | Pro | 🟡 | Main shortcuts done |
| Gradually varied / unsteady flow | No | Out | — | Clearly stated |
