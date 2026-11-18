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
| Project tree, folders | Yes (obs.) | Pro | 🟡 | Flat list now; folders Phase F |
| Duplicate / delete / recycle bin | Yes (obs.) | MVP | 🟡 | Duplicate + delete; no recycle bin (undo instead, Phase F) |
| Sort names / re-sort | Yes (obs.) | Plan | ⬜ | |
| Title expressions | Yes (obs.) | Pro | ⬜ | Builder with preview |
| Templates (factory street sections) | Yes (obs.) | Pro | ⬜ | Original templates; user templates |
| Save / open / versioned file | Yes | MVP | ✅ | `.mfproj` schema 1 |
| Recent files | Yes (obs.) | Pro | ⬜ | |
| Backup settings | Yes (obs.) | Plan | ⬜ | |
| Autosave / recovery | Not seen | Pro | ⬜ | Phase F |
| Undo / redo | Not seen | MVP | ✅ | Per calculation |
| Report | Yes | MVP | ✅ | Original HTML layout |
| PDF | Yes | MVP | 🟡 | Via browser print; native Phase F |
| Watermark | Yes | Pro | ✅ | |
| Logo, footer | Inv | Pro | 🟡 | Footer done; logo supported in engine, no picker |
| Batch printing | Yes | Pro | 🟡 | Batch HTML report done; selection toggle later |
| Calculation trace on report | No | Diff | ✅ | Auditability |
| Civil 3D integration | Video title only | Inv | ⬜ | Investigate data exchange format |
| Licence activation | Yes | Out | — | Not needed |
| Telemetry | Licence checks only | Out | — | None |
| Light / dark theme | Dark-only (obs.) | Plan | ✅ | Follows Windows setting |
| Keyboard shortcuts | Inv | Pro | 🟡 | Main shortcuts done |
| Gradually varied / unsteady flow | No | Out | — | Clearly stated |
