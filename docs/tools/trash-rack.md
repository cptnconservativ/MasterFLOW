# Trash rack head loss and clogging

Tool id `trash-rack` (free). Checks a bar rack on a pond outlet, culvert inlet or storm sewer intake: open area, the
velocity through it, and the head loss to add to the outlet rating, clean and partly clogged.

## Input

| Key | Meaning |
|---|---|
| `clogging` | Share of the open area blocked for the design check, % (default 50) |
| `max velocity` | Your limit on the velocity through the clogged net area (optional) |

`[Racks]`: Name, Q, Width, Height (submerged height of the rack), Bar t and Clear b (inches or mm), Angle (from horizontal,
default 90), Shape (`rectangular`, `round nose`, `round`, `round both`).

## Method

- Gross area Ag = width × height / sin(angle); open ratio = b/(b + t); net area An = Ag × ratio; clogged An × (1 − clogging).
- Kirschmer (1926), clean rack: h = Kt·(t/b)^(4/3)·sinθ·Va²/2g, Va = Q/(width × height); Kt = 2.42 sharp rectangular,
  1.83 rectangular with a round upstream face, 1.79 round bars, 1.67 round at both ends.
- Creager and Justin (1950), as used by USBR: h = (1.45 − 0.45·R − R²)·Vn²/2g with R = An/Ag and Vn = Q/An, for the clean
  and the clogged rack.

Hand check: 42 cfs, 6 × 4 ft vertical rack, 3/8 in. bars at 3 in. clear: ratio = 0.889, An = 21.3 ft², Vn = 1.97 ft/s,
h = (1.45 − 0.40 − 0.790)·1.97²/64.35 = 0.016 ft clean; at 50 % clogging R = 0.444, Vn = 3.94 ft/s, h = 0.192 ft.

## Decisions

- D-650: The clogged loss uses the net-area equation with the reduced R; Kirschmer is reported for the clean rack only.
- D-651: Default clogging 50 %; set your agency's value.

## Limits

Flow-induced vibration of bars and debris loads on the rack are not checked.
