# Hydraulic jump and stilling basins

Tool id `stilling-basin` (free). One tool for the energy dissipators a drainage engineer sizes below a chute, a culvert
or a storm sewer outfall: the hydraulic jump itself, the USBR and SAF jump basins, the USBR Type VI impact basin that
Rapid City IDCM §4.9.2(1) shows, and the CSU roughness-element basin of IDCM §4.9.6.

## Input

| Key | Meaning |
|---|---|
| `Q` | Design flow |
| `basin width` | Width of the rectangular chute and basin (turns on the jump and USBR/SAF part) |
| `side slope` | z for a trapezoidal section (jump analysis only; default 0) |
| `y1`, `V1`, `energy above floor`, or `approach slope` (%) + `approach n` | The entering supercritical flow: a depth, a velocity, the total energy above the basin floor, or normal depth on the chute |
| `tailwater`, `[Tailwater rating]` (Q, depth), or `tw bottom width`, `tw side slope`, `tw slope` (%), `tw n` | Tailwater depth above the channel bed downstream |
| `floor drop` | `auto` (least depression that holds the jump, per basin) or a depth |
| `[Check flows]` | Flows to check against the tailwater with the chosen basin |
| `outlet shape`, `outlet span`, `outlet rise`, `outlet depth` or `outlet slope` + `outlet n` | Pipe or box outlet for the impact and CSU basins |
| `impact Q`, `impact basins` | Flow for the Type VI basin and the number of basins side by side |
| `csu Q`, `exit velocity`, `expansion ratio` (2-4, 5, 6, 7, 8, auto), `basin slope` (%), `anchor lever` | CSU basin |

## Method

| Item | Method |
|---|---|
| Sequent depth | Equal specific force M = Q²/(gA) + A·ȳ (any trapezoid; Bélanger's equation for a rectangle) |
| Jump | Fr1 = V1/√(g·A/T); type by Fr1 (USBR EM-25); ΔE = E1 − E2; length Lj = 6.9(y2 − y1) (HEC-14 Ch. 6) |
| Jump position | TW < 0.98·y2: swept out; TW > 1.02·y2: submerged; otherwise at the toe |
| Floor drop | The entering energy is carried down the drop (y1 from E1 + drop), so y2 grows; the drop is solved by bisection so that TW + drop = factor·y2 |
| Tailwater factor | Type I and III 1.0, Type II 1.05, Type IV 1.1 (EM-25); SAF (1.10 − Fr²/120) for Fr 1.7–5.5, 0.85 for 5.5–11, (1.00 − Fr²/800) for 11–17 (Blaisdell, HEC-14 Ch. 8) |
| Basin lengths | Type I and IV: Lj; Type II ≈ 4.3·y2; Type III ≈ 2.7·y2 (mid-range values of the EM-25 curves); SAF LB = 4.5·y2/Fr1^0.76 |
| Ranges | Type II Fr1 ≥ 4.5; Type III Fr1 ≥ 4.5, V1 ≤ 60 ft/s, q ≤ 200 cfs/ft; Type IV 2.5–4.5; SAF 1.7–17, at the dropped floor |
| Blocks and sills | Type II chute blocks y1, dentated sill 0.2·y2; Type III baffles h3 = y1(0.168Fr1 + 0.63), sill h4 = y1(0.0556Fr1 + 1.0); Type IV blocks 2·y1; SAF blocks y1 wide 0.75·y1, floor blocks at LB/3, sill 0.07·y2, walls y2/3 above TW |
| Recommended | The shortest applicable basin that holds the jump at the design flow |
| Flow range | Each [Check flows] flow at the chosen floor: TW + drop against the needed depth, as a table and a chart |
| Impact basin (Type VI) | D = √A of the entering flow, Fr = V/√(gD). W = larger of 2.88·Fr^0.566·D (the Figure 4-8 design-width line; HEC-14 Eq. 9-1) and the Table 4-15 width for Q (log–log between rows). H = ¾W, L = 4/3W, a = ½W, b = ⅜W, c = ½W, d = W/6, e = W/12, t = W/12, riprap = W/20 (Figure 4-8). The Table 4-15 row to build (Q and W both covered) is listed in feet-inches with thicknesses |
| CSU basin | VA from Eq. 4-9-4/4-9-5; YA from Eq. 4-9-6/4-9-7 (WB/Wo 5–8) or Q/(VA·WB) (2–4); every Figure 4-17 layout: h = (h/YA)·YA, L = (L/h)·h, W1 = (W1/Wo)·Wo, provided CB·W1·h·N against the Eq. 4-9-3 requirement; W1/h 2–8; Li = 2Wo + L·Nr; anchor force Eq. 4-9-8 |

## Decisions

- **D-320** One tool for all of §4.9's basins, separate from the outlet protection tool, because the inputs (entering
  flow, tailwater, floor) differ from a riprap apron's.
- **D-321** IDCM Figure 4-17 was read from the page image. Its circular-conduit column for WB/Wo 2–4 with 6 rows
  (0.48, 0.40, 0.34, 0.25, 0.18) runs opposite to every other column (CB should fall as rows are added and rise as h/YA
  falls), so it is not used. Circular with WB/Wo = 8 is blank in the figure.
- **D-322** USBR Type III baffle and sill heights are the linear fits of the EM-25 Fig. 11 curves; the Type II, III basin
  lengths are mid-range curve values. EM-25 itself was not available in this environment, so these should be read
  off the curves for a final design.
- **D-323** Eq. 4-9-3 is printed garbled in the IDCM text. The pressure term is taken as Cp·γ·Yo²·Wo/2, matching the exit
  term γQ²/(2VB²WB) = γYB²WB/2 (hydrostatic force on both faces), as in HEC-14 (1983).
- **D-324** For WB/Wo 2–4, YA = Q/(VA·WB), as §4.9.6 words it ("based on the actual width of the basin"). This gives a
  smaller YA than Eq. 4-9-6/4-9-7 at the 4–5 boundary, so smaller elements and less drag: it errs on the safe side.

## Validation

`StillingBasinTests` (integration tests): Bélanger's closed form, ΔE = (y2 − y1)³/(4y1y2), the trapezoid momentum
balance, floor drop closure, the SAF formulas, Table 4-15 rows reproduced at their tabulated Q, Figure 4-8 proportions,
the CSU momentum equation by hand, SI units and the input errors.
