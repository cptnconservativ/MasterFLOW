# Preliminary detention storage estimate

Tool id `storage-estimate` (free). A first-cut detention volume for site layout and feasibility, before a pond is drawn.
Size the pond itself by routing (`detention-routing`, `hydrograph-routing`).

## Input

| Key | Meaning |
|---|---|
| `area` | Drainage area (acres; ha in SI) |
| `qi`, `qo` | Post-development peak inflow and allowable release |
| `CN`, `P` or `Q runoff` | Runoff depth for TR-55 (P = 24-hour rainfall; IDCM Table 4-2 100-yr = 4.55 in.) |
| `rainfall type` | I, IA, II or III (South Dakota: II) |
| `C`, `tc`, `frequency` | Modified rational: runoff coefficient, time of concentration (min), IDCM Table 4-2 storm (2, 10, 100) |

`[IDF]` (optional): Duration (min), Intensity; replaces IDCM Table 4-2.

## Methods

- TR-55 Chapter 6 (Figure 6-1, Appendix F): Vs/Vr = C0 + C1·r + C2·r² + C3·r³, r = qo/qi; Type I/IA 0.660, −1.76, 1.96,
  −0.730; Type II/III 0.682, −1.43, 1.64, −0.804. Vr = runoff depth × area, runoff by Q = (P − 0.2S)²/(P + 0.8S).
- Modified rational, critical duration: V(Td) = 60·[C·i(Td)·A·Td − qo·(Td + tc)/2] ft³, maximised over Td from tc to 24 h.
- Ratio methods for comparison: Abt and Grigg (1978) Vs/Vr = (1 − r)²; Baker (1979) Vs/Vr = 1 − r.

Hand check (sample): CN 82, P = 4.55 in.: S = 2.195, Q = (4.11)²/(6.31) = 2.68 in.; Vr = 2.68/12 × 25 = 5.58 ac-ft;
r = 0.375, Vs/Vr = 0.682 − 0.536 + 0.231 − 0.042 = 0.334 → 1.86 ac-ft.

## Decisions

- D-690: TR-55's 0.1–0.8 range of qo/qi is warned, not refused.
- D-691: The modified rational outflow rises linearly to qo at Td + tc (the common trapezoidal form).

## Limits

Planning accuracy only (TR-55 cautions errors of about 25 %); no stage-storage, outlet or multi-stage checks.
