# Grading solver (★ Premium)

Tool id `grading-solver` (license feature of the same name). Give it existing ground on a grid and the grading
requirements; it solves for the finished grades that stay as close to existing ground as the requirements allow,
balance cut and fill after shrink, and it lists every spot where the requirements cannot all be met.

## Input

| Key | Meaning |
|---|---|
| `spacing` | Distance between grid points (ft or m) |
| `shrink` | % of cut lost when compacted as fill (default 15) |
| `import` | Fill brought in, cy (negative: export). Default 0 |
| `balance` | `yes` (default) or `no` (stay closest to existing only) |
| `max cut`, `max fill` | Limits at every grid point (ft) |
| `section row` | Row drawn as the west–east section |

`[Existing]`: one line per grid row from north to south, elevations from west to east. Grid points are named like a
spreadsheet: columns A, B, … west to east, rows 1, 2, … north to south.

`[Constraints]` (header row first; columns Type, Range, Value, Min, Max, Direction, Note). Ranges are `A1:D4`, several
separated by `;`.

| Type | Meaning | Columns |
|---|---|---|
| `fix` | Tie-in: the grade stays at existing ground or a given elevation (property lines, back of walk, existing pavement) | Value = `existing` or an elevation |
| `pad` | Flat pad (building), optionally at a set elevation or within limits | Value (optional), Min, Max |
| `away` | Ground falls away from the pad on every side, Value % for the distance in Min (default 5 % for 10 ft, IBC 1804.4) | Value, Min |
| `slope` | Grade between neighbours along Direction (the way it falls: north, south, east, west) from Min to Max %; `any` limits the slope both ways in both grid directions to Max | Min, Max, Direction |
| `cross` | Cross slope (across the Direction of travel) at most Max %; the range must be at least two points wide | Max, Direction |

Typical values: ADA accessible route running slope ≤ 5 % and cross slope ≤ 2 % (2010 ADA Standards §403.3), ramps ≤
8.33 % (§405.2), driveways ≤ 10–12 % (local standard), lawns ≤ 25 % (4:1) and ≥ 2 %, swales ≥ 1 %.

## Method

The finished grades z minimize Σ A·(z − existing)² (A: the area each point stands for) subject to every requirement
written as a linear inequality between grid points, and to the earthwork balance Σ A·(z − existing) = import − shrink
× cut. The problem is a convex quadratic program; it is solved by Hildreth's dual coordinate ascent, which works one
constraint at a time and needs no matrix, so grids of several thousand points solve in under a second. The shrink term
is updated and the problem re-solved until the cut stops changing.

When the requirements conflict, each constraint's multiplier is capped by its rank, which turns the problem into an
exact penalty problem: the solver still converges, and the requirements it gives up are the lowest-ranked ones that
must yield. Ranks, highest first: tie-ins (kept exactly); pads; drainage away from buildings, ADA running slopes ≤ 8.33 %
and cross slopes; other slope limits and max cut/fill; the balance.

## Output

- **Requirements**: each requirement, the number of point pairs checked, what was reached (steepest and flattest
  slopes, least fall away from the pad, pad elevation), OK or CANNOT BE MET with the worst place and the shortfall.
- **Earthwork**: cut, fill, the fill the cut makes after shrink, import/export needed, largest cut and fill.
- **Conflicts**: every place a requirement cannot be met, with the shortfall (ft or %).
- **Finished grade**, **Cut (−) and fill (+)** and **Existing ground** grids; a west–east section.

## Decisions

- D-260: a grid and a quadratic objective rather than a TIN and linear programming: the grid matches how lots are
  spot-graded, the least-squares objective spreads changes smoothly instead of piling them on a few points, and the
  row-action solver needs no external library.
- D-261: ranked exact penalties rather than "infeasible, no answer": the engineer gets the best grading with the
  conflicts listed, which is what is needed to decide which requirement to relax (variance, retaining wall, steps).
- D-262: tie-ins are fixed by weight (they never move), not by constraint, so a conflict always shows up in the
  requirement that has to give, never as a moved property line.
- D-263: volumes by the grid trapezoid rule; for quantities on the plans, check the final surface in Civil 3D.
