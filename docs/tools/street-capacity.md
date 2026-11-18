# Street capacity and gutter spread (IDCM 2012 §4.4.9–4.4.11, HEC-22)

One street cross-section, any number of reaches (slope and flows). Each reach is checked for the initial (10-yr)
and major (100-yr) storms, and gets its allowable capacity so inlet locations can be set from it.

| Item | Method |
|---|---|
| Gutter section | `GutterSection` (shared with the inlet tools): uniform cross slope Sx, or composite gutter W on Sw. Depression a = W (Sw − Sx) |
| 10-yr spread | Uniform: IDCM Eq. 4-4-1 Q = (0.56/n) Sx^1.67 S^0.5 T^2.67. Composite: HEC-22 Eq. 4-4, Q = Qs/(1 − Eo), Qs on the pavement width T − W, Eo = 1/{1 + (Sw/Sx)/[(1 + (Sw/Sx)/(T/W − 1))^2.67 − 1]}; spread inside the gutter is a triangle on Sw |
| Depth, velocity | Depth at curb d = T·Sx + a (T·Sw inside the gutter). V = Q/A, A = Sx T²/2 + aW/2 (equal to Eq. 4-4-2 for a uniform section) |
| 10-yr limits (Table 4-6) | No curb overtopping (d ≤ curb height); local may spread to the crown; collector and two-lane arterial leave 12 ft dry, three-lane 24 ft, more lanes 36 ft; arterials with raised medians use the 12 ft rule. Allowed spread = (pavement − dry)/2 each side |
| Q allowed (10-yr) | Gutter capacity at the governing spread: the least of the Table 4-6 spread, the curb-full spread and the crown |
| Cross pans (Table 4-10) | Reach flagged "cross pan": depth ≤ 6 in. for local and collector (taken as the curb depth arriving at the pan); arterials allow no cross flow |
| 100-yr depth (Table 4-9) | Manning's equation (1.486/n) A R^(2/3) S^0.5 on the full street section, ROW to ROW (parkway, curb, gutter, crown), conveyance summed by subarea where n changes. Limit over the gutter flow line: local 18 in., collector and arterial 12 in. Water above the ROW line or at the building ground line (if entered) fails |
| Q allowed (100-yr) | Lesser of Q at the Table 4-9 depth and Q with water at the ROW line |
| Figure | Street section with the widest 10-yr spread and deepest 100-yr water, the 12/18 in. limit and the allowed spread lines (HTML report) |
| Table 4-8 | `GutterSection.AverageVelocitySpreadRatio(T1/T2)` gives Ta/T2 for travel-time velocity (not used by the checks) |

Decision (D-059): the composite gutter uses HEC-22 Eq. 4-4 with the IDCM exponents (1.67, 2.67) and Ku = 0.56,
the same convention as D-054, so a uniform section gives exactly Eq. 4-4-1. With HEC-22 Example 4-2 data
(W 2 ft, Sw 0.0833, Sx 0.02, S 0.01, n 0.016, T 8 ft) this gives Eo 0.689 and Q 1.957 cfs; HEC-22 prints 0.69 and
1.94 with 5/3 and 8/3. The 100-yr check uses plain Manning on the street section rather than Eq. 4-4-1, as §4.4.10(2)
requires ("Manning's Formula … with an n value applicable to the actual boundary conditions").

Limits and assumptions:
- Q10 is the flow in one gutter; both gutters are taken as equal (as in the inlet tool). Q100 is the whole street.
- Streets with a raised median: `raised median = yes` applies the 12 ft rule (Table 4-6 footnote). The section is
  always crowned at mid-width, so a median street (§4.4.7: each side acts as its own street) should be run per side.
- Building inundation is checked only against the single building ground line entered.
- Hand check: `validation/hand_calcs_street.py` (independent Python of the same equations) reproduces the sample to
  the tested tolerances.
