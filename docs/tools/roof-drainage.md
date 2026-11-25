# Roof drains, leaders and scuppers

Tool id `roof-drainage` (free). Sizes building roof drainage for the site plan: roof drain leaders, the horizontal building
storm drain to the site system, and the secondary scuppers, with the ASCE 7 rain load the structural engineer needs.

## Input

| Key | Meaning |
|---|---|
| `intensity` | 100-year 1-hour rainfall (IPC §1106.1); in./h or mm/h. Rapid City IDCM Table 4-2: 2.95 in./h |
| `weir C` | Scupper weir coefficient (default 3.22, ft-s) |

`[Drains]`: Name, Roof area, Wall area, Leader (drawn size, blank = size it), Horizontal (drawn size), Slope (in./ft, `1/8`
style fractions accepted; % in SI).

`[Scuppers]`: Name, Roof area, Wall area, Width, ds (static head from the drain inlet to the scupper invert).

## Method

- Area = roof + ½ of the vertical wall draining onto it (IPC §1106.4). Q (gpm) = area (ft²) × i (in./h) × 0.0104.
- Leader: smallest size in IPC Table 1106.2 whose capacity is at least Q: 2 in. 30, 3 in. 92, 4 in. 192, 5 in. 360,
  6 in. 563, 8 in. 1,208 gpm.
- Horizontal storm drain: Manning full flow with n = 0.015 at the given slope, sizes 3–15 in. This reproduces the IPC
  horizontal storm drain table (e.g. 8 in. at 1/8 in./ft = 479 gpm).
- Scupper: weir Q = C·b·h^1.5, solved for the head dh. C = 3.22 equals FM Global DS 1-54's Q(gpm) = 2.9·b·h^1.5 (inches).
- Rain load R = 5.2(ds + dh) psf, ds and dh in inches (ASCE 7-16 Eq. 8.3-1); SI R = 0.0098(ds + dh) kN/m² with mm.

## Decisions

- D-640: Horizontal drain capacity is computed (Manning, n = 0.015) rather than read from the IPC table, so every slope works.
- D-641: Leader capacities are the IPC Table 1106.2 values; local amendments may differ.
- D-642: ASCE 7-16 rain load form; ASCE 7-22's ponding head dp can be added to ds.

## Limits

Roof drain body ratings, gutters and downspouts (IPC Table 1106.6) and siphonic systems are not covered.
