# Flow Calc — workflows (observed) and MasterFlow treatment

| Workflow | Flow Calc (observed) | UX strengths | UX weaknesses | MasterFlow treatment |
|---|---|---|---|---|
| Start / open | App opens to an empty tree; Example Project button loads samples | Samples are one click away | — | Sample project on first run; recent files (planned) |
| Create channel | Add Channel → item detail → Channel Analysis opens a separate window | Item metadata separate from hydraulics | Two-step (detail then analysis window) | Single editor pane; metadata at top of the same page |
| Regular channel | Wizard writes station/elevation points into the irregular grid | One engine for all open channels | Regular dimensions are lost once converted; editing b or z means re-running the wizard | First-class parametric shapes; "convert to irregular" as an explicit action (Phase D) |
| Irregular channel | Station/elevation/n grid; paste/import with chosen delimiter | Flexible delimiters; per-point n; descriptions | Station format 0+00.00 only | Grid with paste from Excel, delimiter auto-detect, validation of duplicate/reversed stations (Phase D) |
| Choose unknown | Checkbox next to one of n, S, yn, Q | Compact | Checkbox semantics unusual | Solve-for selector; solved field highlighted with value |
| n selection | Picker from handbook table | Fast, sourced | Cancelling clears n | Picker that never clears on cancel; source shown on report (Phase B+) |
| Pipes | Diameter (in); max discharge, dual normal depth | Surfaces the non-unique Q–depth relation near the crown | — | Same outputs plus explicit warning when Q exceeds full-flow capacity (Phase C) |
| Titles | Expression tokens (%%Q2, %%ND2…) | Titles stay in sync with results | Token syntax must be memorised | Expression builder with insert menu and live preview (Phase F) |
| Rating table | Separate item type with its own analysis window | Reusable | Separate from source channel | Rating tab attached to each calculation, export CSV/clipboard (done) |
| Report / batch print | Print Channel; per-item batch toggle | Batch printing | Not inspected (blocked) | HTML report → browser PDF (done); batch report (done); native PDF (Phase F) |
| Validation | Error list, results hidden while errors exist | Never shows stale results | — | Same principle (done) plus structured codes and warnings with references |
