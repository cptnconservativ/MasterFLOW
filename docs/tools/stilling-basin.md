# Hydraulic jump and stilling basins

Tool id `stilling-basin` (free). The energy dissipators a drainage engineer sizes below a culvert, chute or storm
sewer outfall, by **FHWA HEC-14, 3rd edition (2006)**, the reference the Rapid City IDCM §4.9 is based on:

- the hydraulic jump in a rectangular, trapezoidal or circular channel (HEC-14 Ch. 6);
- the free-jump, USBR Type III, USBR Type IV and SAF stilling basins, with the floor depression, transition and exit
  slopes of HEC-14 §8.1 (Ch. 8);
- the USBR Type VI impact basin and the CSU rigid boundary basin (Ch. 9). The Type VI basin is also sized by the
  Rapid City IDCM (§4.9.2(1), Figure 4-8, Table 4-15) as a local check.

## Input

| Key | Meaning |
|---|---|
| `Q` | Design flow |
| `outlet width` | Wo, the rectangular outlet or chute (default: the basin width) |
| `side slope` | z for a trapezoidal outlet section (jump analysis) |
| `diameter` | D for a circular outlet (jump in a pipe, HEC-14 §6.2.2) |
| `y1`, `V1`, `energy above floor`, or `approach slope` (%) + `approach n` | The flow leaving the outlet (section o): a depth, a velocity, the total energy, or normal depth (HEC-14 Step 1 for supercritical flow) |
| `basin width` | WB, the rectangular basin; turns on the Ch. 8 basins |
| `ground slope` (%), `transition slope`, `exit slope` | So of the streambed below the outlet; ST and SS (V:H), default 0.5 |
| `floor drop` | zo − z1, basin floor below the outlet invert: `auto` (least that holds the jump, per basin) or a depth |
| `saf flare` | z for a flared SAF basin (0 to 0.5) |
| `tailwater`, `[Tailwater rating]` (Q, depth), or `tw bottom width`, `tw side slope`, `tw slope` (%), `tw n` | Tailwater depth above the channel bed at the basin exit |
| `[Check flows]` | Flows to check with the chosen basin as built |
| `outlet shape`, `outlet span`, `outlet rise`, `outlet depth` or `outlet slope` + `outlet n` | Pipe or box outlet for the impact and CSU basins |
| `impact Q`, `impact basins`, `impact Ho/WB`, `impact HL/Ho` | Type VI flow, number of basins side by side, and values read from HEC-14 Figures 9.14 and 9.15 |
| `csu Q`, `exit velocity`, `expansion ratio` (2-4, 5, 6, 7, 8, auto), `basin slope` (%), `csu yA`, `Cp`, `anchor lever` | CSU basin; `csu yA` and `Cp` are values read from HEC-14 Figures 4.3/4.4 and 9.4 |

## Method

| Item | Method (HEC-14 unless noted) |
|---|---|
| Sequent depth | Equal specific force M = Q²/(gA) + A·ȳ (Eq. 6.1) for any section; Eq. 6.3/8.4 for a rectangle; a circle is exact (Table 6.1), and once y2 ≥ D the pressure head acts on the full area (Eq. 6.5) |
| Jump | Fr1 = V1/√(g·A/T); form by Fr1 (Figure 6.2); ΔE = E1 − E2. Length: rectangle from the Figure 8.2 free-jump curve; circle 7(y2 − y1) when y2 ≥ D, else Figure 6.8 (not shown); trapezoid 6.9(y2 − y1) as an estimate only (HEC-14 asks for model tests) |
| Need for a basin | Step 3: TW against y2 at the outlet with C = 1 |
| Entering flow | E1 = (zo − z1) + yo + Vo²/2g with no loss (Eq. 8.2), y1 the supercritical depth in WB; Fr1 = V1/√(g·y1) (Eq. 8.1) |
| Geometry | LT = (zo − z1)/ST (Eq. 8.5); LS = [LT(ST − So) − LB·So]/(SS + So) (Eq. 8.6, not below 0); z3 = z1 + LS·SS (Eq. 8.7). The jump holds when z1 + y2 ≤ z3 + TW |
| Tailwater ratio C | Free jump and Type III 1.0; Type IV 1.1; SAF Eq. 8.18. y2 = C·y1/2·(√(1 + 8Fr1²) − 1) (Eq. 8.4) |
| Basin length LB | Free jump and Type IV: Figure 8.2 free-jump curve × y2; Type III: Figure 8.2 Type III curve × y2; SAF: 4.5·y2/(C·Fr1^0.76) (Eq. 8.19). Total = LT + LB + LS |
| Ranges | Type III Fr1 4.5–17, V1 ≤ 60 ft/s, q ≤ 200 cfs/ft; Type IV 2.5–4.5; SAF 1.7–17 (Table 8.1) |
| Type III elements | Chute blocks h1 = y1 (≥ 0.66 ft), Nc = WB/(2y1), W1 = W2 = WB/(2Nc); baffles h3 = y1(0.168Fr1 + 0.58), NB = WB/(1.5h3), W3 = W4 = WB/(2NB), 0.8y2 downstream; end sill h4 = y1(0.0536Fr1 + 1.04) (Eqs. 8.9–8.14) |
| Type IV elements | Chute blocks 2y1, Nc = WB/(2.625y1), W1 = WB/(3.5Nc), W2 = 2.5W1; end sill as Type III (Eqs. 8.15–8.16) |
| SAF elements | Chute blocks h1 = y1, Nc = WB/(1.5y1), W1 = W2 = WB/(2Nc); baffles h3 = y1, NB = WB2/(1.5y1), at LB/3, 40–55 % blocked, ≥ 3y1/8 from a wall; sill 0.07y2/C; walls y2(1 + 1/(3C)); circular-outlet width Eq. 8.17; flare Eqs. 8.22, 8.26 |
| Width limit | WB ≤ Wo + 2LT√(ST² + 1)/(3Fro) (Eq. 8.3) |
| Curvature | r = y/(e^(1.5/Fr²) − 1) at both slope changes (Eq. 8.8) |
| Recommended | The applicable basin with the shortest total length that holds the jump at the design flow |
| Flow range | Each [Check flows] flow with the chosen basin as built (floor and exit fixed) |
| Type VI impact basin | ye = (A/2)^0.5 for a pipe, the depth for a rectangle; Fr = Vo/√(g·ye); Ho = ye + Vo²/2g; WB = Ho/(Ho/WB) from Figure 9.14, rounded to the nearest Table 9.2 entry; dimensions from Table 9.2; exit velocity from Q/(WB·VB) + VB²/2g = Ho(1 − HL/Ho), HL/Ho from Figure 9.15, supercritical root unless the tailwater reaches the subcritical one. Limits 400 cfs and 50 ft/s per basin; maximum tailwater h3 + h2/2 |
| Type VI, IDCM check | D = √A, W = larger of 2.88·Fr^0.566·D (Figure 4-8) and Table 4-15 (log–log), and the Table 4-15 row |
| CSU basin | VA by Eq. 4.1/4.2; yA from Figures 4.3/4.4 for WB/Wo 4–8 and Eq. 9.3 for WB/Wo < 4 or slopes above 10 %; every Table 9.1 layout: h = (h/yA)·yA, L = (L/h)·h, W1 = (W1/Wo)·Wo, CB·AF·N against Eq. 9.1 (Eq. 9.2 with the water weight above 10 %). Option 1 holds the exit at the tailwater depth; otherwise the given exit velocity. Option 2 VB (the basin's own drag) is listed too. LB = 2Wo + L·Nr, W1/h 2–8, flare Eq. 9.5, freeboard Eq. 9.4; anchor force IDCM Eq. 4-9-8 |

## Decisions

- **D-320** One tool for all of HEC-14's jump basins and Ch. 9 basins, separate from the outlet protection tool,
  because the inputs (entering flow, tailwater, floor) differ from a riprap apron's.
- **D-321** HEC-14 Table 9.1 and IDCM Figure 4-17 both print the circular-conduit column for WB/Wo 2–4 with 6 rows as
  0.48, 0.40, 0.34, 0.25, 0.18. Read bottom to top, each value is exactly 0.02 below the 5-row value in its row, the
  same step the rectangular columns show; read top to bottom it runs against every other column. The column is used
  reversed (0.18, 0.25, 0.34, 0.40, 0.48). Circular with WB/Wo = 8 is blank in both.
- **D-322** Kyle (2026-11-23): HEC-14 is the reference for this tool, ahead of the IDCM, which is based on it. The
  USBR Type II basin is not in HEC-14 and was removed (pending Kyle's answer; see the PR). The Type III baffle and
  sill and the Type IV blocks use the HEC-14 equations, which replace the earlier EM-25 curve fits.
- **D-323** Eq. 9.1 (IDCM Eq. 4-9-3, garbled in the IDCM text) has the pressure term Cp·γ·yo²·Wo/2; HEC-14 prints it.
- **D-324** For WB/Wo < 4, HEC-14 Step 3 takes yA from Eq. 9.3 (energy from the outlet to two widths downstream),
  not Q/(VA·WB) as the IDCM text words it.
- **D-325** Type VI is designed by HEC-14 (Figure 9.14, Table 9.2). The IDCM's Figure 4-8 and Table 4-15 give a
  different width for the same flow (5.47 m against HEC-14's 5.0 m in the HEC-14 SI example), so the IDCM answer is
  shown as a check, with a note when it is wider.
- **D-326** HEC-14 rounds the Type VI width to the nearest Table 9.2 entry ("interpolation is not necessary"); the tool
  does the same, so the width can round down by up to half a step. Outside the table the dimensions are scaled in
  proportion from the end row, with a warning.
- **D-327** Provisional figure values. HEC-14 prints these curves only as images, which were not available as text:
  Figure 8.2 (basin length: free-jump and Type III curves, digitized from the USBR curves and matching every Ch. 8
  example to 1 %), Figure 9.14 and 9.15 (Type VI: lines through the two Ch. 9 example points, Fr 4.7–5.1, warned
  outside 4–6), Figure 9.4 (Cp: 0.7 as in the example), Figures 4.3/4.4 (yA: the IDCM Eq. 4-9-6/4-9-7 lines, 0.35
  against HEC-14's 0.33 at Fr 2.1), Figure 6.5 (jump length; Figure 8.2 is used) and Figure 6.8 (jump length in a
  pipe with y2 < D; not given). Each has an input to type the value read from the figure. To be digitized from the
  HEC-14 PDF.
- **D-328** Eq. 9.3 is scrambled in the HEC-14 text. Provisionally read as yA + (Q/(WA·yA))²/2g =
  2Wo·So + yo + 0.75·Vo²/2g (a quarter of the outlet velocity head lost) with WA = Wo(4/(3Fr) + 1). To be confirmed
  against the printed equation.

## Validation

`StillingBasinTests` reproduces the HEC-14 worked examples: §6.2.1 rectangular jump, §6.2.2 circular jump, the
§8.2 free-jump basin, §8.3 Type III, §8.4 Type IV and §8.5 SAF (y1, Fr1, y2, LB, LS, z3, blocks, baffles and sills
within the examples' rounding), Eq. 8.8 radii, §9.4 Type VI in SI and CU (WB 5.0 m and 16 ft, VB 6.9 m/s and
22.7 ft/s), and §9.1 CSU in SI (required CB·AF·N 4.63, 4 rows short, 5 rows pass, LB 17.75 m). Also Bélanger's closed
form, the trapezoid and circle momentum balance, Table 6.1's K, the Table 9.1 column order, SI scaling and input
errors.
