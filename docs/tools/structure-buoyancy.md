# Structure buoyancy (flotation) check

Tool id `structure-buoyancy` (free). Checks an empty manhole, inlet, vault or outlet structure against flotation at the
design high groundwater, and says how much ballast concrete or base extension would fix it.

## Input

| Key | Meaning |
|---|---|
| `concrete`, `water` | Unit weights (150 and 62.4 pcf; 23.6 and 9.81 kN/m³) |
| `soil moist`, `soil saturated` | Backfill above and below the water table (120 and 125 pcf) |
| `FS required` | Required factor of safety (default 1.25) |
| `other weight` | Castings, cone, riser rings etc. added to every structure |

`[Structures]`: Name, Shape (`round` or `box`), Inside L or D, Inside W, Wall, Base (slab thickness), Ext (base projection
beyond the outside wall), Top slab, Opening (top slab opening), Bottom (bottom of base), Top (rim/ground), GW (design water table).

## Method

- Uplift U = γw × outside volume below the water table (base slab with its extension, then the barrel).
- Concrete = base slab + walls + top slab less its opening, × γc.
- Soil on the extension: the ring between the outside wall and the edge of the base, from the top of the base to the ground,
  moist unit weight above the water table and γsat − γw below.
- FS = (concrete + soil + other)/U. Ballast inside = (FS·U − W)/γc. The extension that reaches FS is found by bisection
  (a wider base adds concrete and soil, and uplift on the slab).

Hand check (sample MH-4): 5 ft ID, 6 in. wall, 8 in. slabs, 2 ft opening, 13 ft tall, 11.5 ft submerged:
U = 62.4·28.27·11.5 = 20,290 lb; concrete = (18.94 + 100.7 + 16.8) ft³ × 150 = 20,480 lb; FS = 1.01 → floats.

## Decisions

- D-630: The structure is checked empty, with no side friction (the usual conservative basis).
- D-631: Default FS 1.25; agencies use 1.1 to 1.5. Enter the governing value.
- D-632: Pipe openings in the walls are not deducted from the concrete; on structures with many large pipes, deduct
  them by hand (lower the wall thickness or raise `FS required`).

## Limits

Anchors, tie-downs and the uplift on pipes are not modelled.
