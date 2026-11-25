# Underground detention: chambers or pipe gallery in stone

Tool id `underground-detention` (free). Lays out an underground detention bed of arch chambers or perforated pipes in
washed stone, builds its stage–storage curve, and finds how many units per row a required volume needs. The
stage–storage table pastes into the detention routing tool.

## Input

| Key | Meaning |
|---|---|
| `unit` | `stormtech` (ADS StormTech, with `chamber`), `arch` (semi-ellipse chamber), `pipe`, or `curve` (manufacturer's table under `[Unit curve]`) |
| `chamber` | StormTech model: SC-160LP, SC-310, SC-740, DC-780, SC-800, MC-3500, MC-4500, MC-7200. Fills width, height, installed length, row spacing, stone below and above and the cover limits from the catalog; each can still be entered |
| `width`, `height`, `length` | Chamber base width, height and installed length (`curve`: width and length for the layout) |
| `diameter` | Pipe diameter (`unit = pipe`) |
| `rows`, `per row`, `spacing`, `edge` | Layout: rows, units per row, stone between rows, stone beyond the outer units |
| `stone below`, `stone above`, `porosity` | Stone depths and porosity (default 0.40) |
| `bottom` | Bed bottom elevation |
| `cover`, `min cover`, `max cover` | Fill over the stone to the bottom of flexible pavement, and the limits (StormTech: from the catalog) |
| `required volume` | Optional: finds the units per row |

## Method

- Bed length = per row × length + 2·edge; width = rows × width + (rows − 1)·spacing + 2·edge; depth = stone below + unit height + stone above.
- Unit void below depth y (from the unit bottom): arch = L·(w/2)·h·[asin(t) + t·√(1 − t²)], t = y/h (semi-ellipse);
  pipe = L·r²(θ − sin θ)/2; curve = your table, linear.
- Storage(y) = units' void + porosity × (bed area × y − units' void). Unit walls are taken as thin.
- Units per row for a required volume: the least count whose full-depth storage reaches it.

Hand check (sample): one chamber = 7·(4.25/2)·2.5·π/2 = 58.4 ft³; 72 chambers = 4,206 ft³; bed 86 × 30 × 3.5 ft = 9,030 ft³;
stone 0.40·(9,030 − 4,206) = 1,930 ft³; total 6,136 ft³.

## Decisions

- D-700: Arch chambers default to a semi-ellipse section; enter the manufacturer's curve (`unit = curve`) for final design.
- D-701: Infiltration, the sediment (isolator) row and manifolds are left to routing and the manufacturer's layout.
- D-702 (2026-11-25): ADS StormTech chambers from the StormTech Product Catalog (10818) and the MC-4500 and MC-3500/MC-7200
  design manuals, with the SC-740 from its 2017 sell sheet. Bare chamber stage–storage is the catalog's incremental table
  for the SC-160LP, SC-310, DC-780 and SC-800, and a semi-ellipse scaled to the bare storage for the others. Per-chamber
  installed storage at the catalog layout reproduces every printed "minimum installed storage" within 0.3 %
  (ManualWorkedExampleTests). End caps are not counted.
