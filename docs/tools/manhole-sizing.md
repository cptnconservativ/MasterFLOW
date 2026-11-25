# Manhole and structure size from pipes

Tool id `manhole-sizing` (free). Finds the smallest round manhole or inlet structure whose barrel wall can take
all of its pipes, and rounds it up to the sizes your precaster makes. Pipe sizes, inverts and plan angles are read
from the selected storm network when the cells are left blank (angles only when the structures have coordinates).

## Input

| Key | Meaning |
|---|---|
| `min diameter` | Smallest structure allowed (default 48 in. / 1200 mm) |
| `sizes` | Standard inside diameters, comma-separated (default 48, 60, 72, 84, 96, 120, 144 in.) |
| `clearance` | Added to each side of the pipe OD for the cored hole or boot (default 2 in. / 50 mm) |
| `leg` | Least wall left between two openings on the inside face (default 8 in. / 200 mm) |

`[Pipes]`: Structure, Pipe, ID, Wall, Angle (deg, clockwise from any reference), Invert. Wall blank = ASTM C76 wall B
(ID/12 + 1 in.; ID/12 + 25 mm in SI).

## Method

- Opening width w = ID + 2·wall + 2·clearance. On the inside face of a structure of diameter D it subtends θ = 2·asin(w/D).
- Two openings interfere when their vertical bands (invert − wall − clearance to invert + ID + wall + clearance) overlap
  or come within one leg of each other. A missing invert counts as overlapping.
- Every interfering pair needs plan angle ≥ (θ1 + θ2)/2 + 2·asin(leg/D). Each condition loosens as D grows, so the least D
  is found by bisection, then raised to `min diameter` and rounded up to the next listed size.
- The pair with the least spare angle at the required D is reported as governing.

Check: two 48 in. pipes straight through (wall 5 in., clearance 2 in., w = 62 in., leg 8 in.) need
asin(62/D) + asin(8/D) = 90°, so D = √(62² + 8²) = 62.5 in. → 72 in. structure.

## Decisions

- D-600: The leg is measured as a chord on the inside face, the conservative side of the outside-face arc that precasters quote.
- D-601: Openings at different elevations are not checked against each other; the base slab, cone and top slab openings are not checked.
- D-602: Elliptical pipes use the span as the opening width.
- D-603: Wall B is the default wall; enter the actual wall (or the precaster's hole size minus the OD) where it differs.

## Limits

Precaster hole-size tables, maximum hole size per barrel size and drop connections are not modelled; confirm with the
supplier's shop drawings.
