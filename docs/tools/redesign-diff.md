# ★ What changed? Redesign diff

When the site plan moves, this tool re-runs every calculation for the old and new design and lists **only** what
moved: elements that now fail, failures that got worse, checks that lost margin, things that were fixed or gained
margin, and elements added or removed. Premium feature `redesign-diff`.

## Three ways to give the baseline

| Input | Compares |
|---|---|
| `baseline = C:\…\Job 2026-10-01.mfproj` | An earlier copy of the project against the open project: every storm network (matched by name), every channel/pipe calculation (by id, then title) and every design tool whose input is saved in both |
| `baseline network = Main St (v1)` | Another storm network in this project against the selected one (or `current network = …`) |
| `[What if]` edits | The selected network against a copy with the edits applied, to ask "what if the rims drop 0.75 ft and the flows go up 15 %?" before touching the design |

`[What if]` rows are `Item, Field, Value`. Structure fields are `rim`, `rim change`, `inflow` and `k`. Pipe fields are `rise` (in.), `span` (in.), `length`, `us invert`, `ds invert`, `invert change`, `n`, `q`, `q factor` and `minor q`. The network field is `tailwater`, used with Item `network`. Item `*` applies the edit to every structure or pipe.

## What is checked

Networks are solved from their inputs, never from a stale stored result. Each check has a margin, which is the distance from the value to the limit on the passing side. A negative margin fails. Limits come from each network's own criteria (Rapid City IDCM 2012 §4.6 by default).

| Element | Check |
|---|---|
| Structure | HGL below the rim (§4.6.7), EGL below the rim when set |
| Pipe | Design flow ≤ full-flow capacity; maximum velocity on mains (Table 4-13); minimum velocity at the minor storm (§4.6.2(3)a); minimum size; HGL at or below the crown when surcharge is not allowed; cover at each end |
| Pipe/structure | The IDCM §4.6 plan-review items that are pass/fail only (spacing, larger into smaller, crowns, inverts, n): new and cleared FAILs |
| Channel calculation | The calculation's design criteria (shear, freeboard, velocities) and, for pipes, capacity |
| Design tools | Each tool is re-run with both inputs; numbers that changed by more than `margin change` and result words (FAIL, OK, NO-GO, …) that changed; new and cleared errors |

A change is listed when its margin moves by more than `margin change` (default 10 %) of the limit, or when it crosses
the limit. `show unchanged = yes` lists every check. The **What was changed** table lists the input differences
(rims, inverts, sizes, flows, routes) so the cause sits next to the effect. A chart shows the HGL clearance at each
structure, before and after.

## Decisions

- **D-210** One premium design tool. A baseline file is opened through `DesignToolContext.LoadProject` (the desktop passes the project loader), so the Application layer stays free of file formats.
- **D-211** Both versions are solved fresh with the current solver, so a solver update never shows up as a design change.
- **D-212** Margins are measured on the passing side of the limit and compared relative to the limit's size, so a 0.2 ft loss of HGL clearance against a 1 ft criterion (20 %) is listed while a 0.2 cfs change in a 50 cfs capacity check is not.
- **D-213** Elements are matched by name (networks, pipes, structures), calculations by id then title, and tool result rows by their first cell.
- **D-214** Plan-review items already measured as margins (HGL, velocity, cover, size) are not listed twice.
- **D-215** The drainage report builder and batch scenarios are not re-run inside the diff, because they re-run other tools themselves.
- **D-216** Order: new failures, worse failures, less margin, fixed, more margin, added/removed.
