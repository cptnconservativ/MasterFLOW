# Emergency spillways and dam breach (TR-60, Froehlich)

Tool id `spillway-breach` (free, U.S. units). For detention and stock-pond embankments that fall under dam safety rules:
NRCS TR-210-60 (*Earth Dams and Reservoirs*, 2019) design rainfall for the auxiliary spillway and freeboard hydrographs,
an auxiliary spillway rating, freeboard checks, and a dam breach screen by the regression equations most states accept.

## Input

| Key | Meaning |
|---|---|
| `hazard` | `low`, `significant` or `high` (TR-60 class a, b, c) |
| `storage x height` (or `storage` and `effective height`), `upstream dam`, `purpose`, `auxiliary spillway` | Figure 2–2 low hazard rows (ac-ft × ft above or below 30,000; an upstream dam whose failure could endanger this one); `purpose = municipal` or `industrial` uses the significant hazard row; `irrigation` and `earth`/`vegetated` select the Figure 2–1 principal spillway storm |
| `embankment area`, `valley width` | ft² of embankment section at the breach and ft of valley width at the breach depth, for the TR-210-60 minimum breach peak |
| `P100`, `PMP` | 100-yr and PMP rainfall for the same duration, in. |
| `spillway` | `weir` (`C`, `L`) or `trapezoid` (`bottom width`, `side slope`) |
| `crest`, `top of dam` | Auxiliary spillway crest and top of dam, ft |
| `ASH stage`, `FBH stage` | Routed peak stages, ft (optional) |
| `breach storage`, `breach depth`, `breach height`, `failure mode` | ac-ft, ft, ft, `overtopping` or `piping` |

## Method

- TR-210-60 Figure 2–2: low hazard (< 30,000, no upstream dam) ASH = P100, FBH = P100 + 0.12·Δ; low (> 30,000) ASH = P100 + 0.06·Δ,
  FBH = P100 + 0.26·Δ; low with an upstream dam and significant ASH = P100 + 0.12·Δ, FBH = P100 + 0.40·Δ; high ASH = P100 + 0.26·Δ,
  FBH = PMP (Δ = PMP − P100). Figure 2–1 principal spillway (10-day) storm by class, size, upstream dams and spillway type.
- TR-210-60 minimum peak breach discharge: Hw ≥ 103 ft → 65·Hw^1.85; otherwise 1100·Br^1.35 (Br = Vs·Hw/A) but not less than
  3.2·Hw^2.5 nor more than 65·Hw^1.85; when the valley width L < T = 65·Hw^0.35/0.416, 0.416·L·Hw^1.5 replaces 65·Hw^1.85.
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
- **D-474** (2026-11-25) Criteria updated from TR-60 (2005) to TR-210-60 (2019), the copy in the reference set: the
  missing low hazard rows of Figure 2–2, Figure 2–1 and the ch. 1 minimum breach peak are added.
