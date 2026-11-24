# Emergency spillways and dam breach (TR-60, Froehlich)

Tool id `spillway-breach` (free, U.S. units). For detention and stock-pond embankments that fall under dam safety rules:
NRCS TR-60 (*Earth Dams and Reservoirs*, 2005) design rainfall for the auxiliary spillway and freeboard hydrographs,
an auxiliary spillway rating, freeboard checks, and a dam breach screen by the regression equations most states accept.

## Input

| Key | Meaning |
|---|---|
| `hazard` | `low`, `significant` or `high` (TR-60 class a, b, c) |
| `P100`, `PMP` | 100-yr and PMP rainfall for the same duration, in. |
| `spillway` | `weir` (`C`, `L`) or `trapezoid` (`bottom width`, `side slope`) |
| `crest`, `top of dam` | Auxiliary spillway crest and top of dam, ft |
| `ASH stage`, `FBH stage` | Routed peak stages, ft (optional) |
| `breach storage`, `breach depth`, `breach height`, `failure mode` | ac-ft, ft, ft, `overtopping` or `piping` |

## Method

- TR-60 rainfall: low hazard ASH = P100, FBH = P100 + 0.12·(PMP − P100); significant ASH = P100 + 0.12·(PMP − P100),
  FBH = P100 + 0.40·(PMP − P100); high ASH = P100 + 0.26·(PMP − P100), FBH = PMP.
- Rating: weir Q = C·L·H^1.5, or critical flow at a level control section of a trapezoidal earth spillway with specific
  energy equal to the head (Q = A·√(g·A/T)); chart with the crest and top of dam.
- Freeboard: the FBH peak stage must not exceed the top of dam (W-SB-001).
- Breach (SI equations, results in U.S. units): Froehlich (1995) Qp = 0.607·Vw^0.295·hw^1.24; MacDonald and
  Langridge-Monopolis (1984) Qp = 1.154·(Vw·hw)^0.412; USBR (1988) Qp = 19.1·hw^1.85; Froehlich (2008) average width
  B = 0.27·Ko·Vw^0.32·hb^0.04 (Ko 1.3 overtopping, 1.0 piping), side slopes 1.0H:1V (overtopping) or 0.7H:1V, failure
  time tf = 63.2·√(Vw/(g·hb²)); a triangular hydrograph with the largest peak and the reservoir volume.

## Decisions

- **D-470** The ASH and FBH hydrographs are not built or routed here: the depths go to the TR-55 or hydrograph routing
  tool, and the rating to the detention routing tool, which already route ponds. The routed stages come back as inputs.
- **D-471** No spillway erosion (SITES/WinDAM) analysis; the tool says where it is needed.
- **D-472** Breach regressions only (no breach model). The largest peak drives the screen hydrograph.
- **D-473** State dam safety rules (South Dakota DANR, for example) may set other storms; the TR-60 values are the
  federal minimum. Use the state rule where it is stricter.

## Messages

| Code | Meaning |
|---|---|
| E-SB-001 … 008 | SI project; unknown hazard, spillway or failure mode; invalid rainfall, spillway or breach values; nothing to compute |
| W-SB-001 | FBH stage above the top of dam |
