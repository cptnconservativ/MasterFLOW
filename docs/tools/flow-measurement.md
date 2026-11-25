# Flow measurement: weirs and Parshall flumes

Tool id `flow-measurement` (free). Discharge from a measured head at a standard measuring weir or Parshall flume, and a
rating table for each device. Useful for pond outfall monitoring, MS4 dry-weather screening, irrigation ditch
crossings and checking a field measurement against a model.

## Input

| Key | Meaning |
|---|---|
| `rating max`, `rating step` | Heads for the rating table |

`[Devices]`: Name, Type, Size, P, Ha, Hb, Ce, kh.

| Type | Size | Notes |
|---|---|---|
| `v-notch` | notch angle, degrees | Ce and kh for angles other than 90° (USBR WMM Fig. 7-5) |
| `suppressed` | crest length | full-width rectangular weir; P = crest height above the approach floor |
| `contracted` | crest length | rectangular with full end contractions |
| `cipolletti` | crest length | 1:4 side slopes |
| `parshall` | throat width (`9 in`, `3 ft`) | Hb = downstream gauge for the submergence check |

## Method (USBR *Water Measurement Manual*, 2001; ft and cfs)

- V-notch (Kindsvater–Shen): Q = 4.28·Ce·tan(θ/2)·(h + kh)^2.5; 90°: Ce = 0.578, kh = 0.003 ft.
- Suppressed rectangular (Kindsvater–Carter, b/B = 1): Q = (3.22 + 0.40·h/P)·(L − 0.003)·(h + 0.003)^1.5.
- Contracted rectangular: Q = 3.33·(L − 0.2·h)·h^1.5.
- Cipolletti: Q = 3.367·L·h^1.5.
- Parshall, free flow Q = C·Ha^n: 1 in. 0.338·Ha^1.55; 2 in. 0.676·Ha^1.55; 3 in. 0.992·Ha^1.547; 6 in. 2.06·Ha^1.58;
  9 in. 3.07·Ha^1.53; 1–8 ft 4·W·Ha^(1.522·W^0.026); 10–50 ft (3.6875·W + 2.5)·Ha^1.6. Free flow holds while Hb/Ha ≤ 0.50
  (1–3 in.), 0.60 (6–9 in.), 0.70 (1–8 ft), 0.80 (10–50 ft).

Hand check: 90° V-notch at 0.48 ft: Q = 4.28·0.578·(0.483)^2.5 = 0.401 cfs.

## Decisions

- D-660: Submerged Parshall flow is reported as submerged with no discharge (the correction charts are not built in).
- D-661: Heads outside each equation's calibrated range are computed but warned.

## Limits

Approach-velocity corrections beyond the Kindsvater forms, broad-crested weirs (see the outlet designer) and flumes
other than Parshall (Cutthroat, H-flume, ramp flumes) are not included.
