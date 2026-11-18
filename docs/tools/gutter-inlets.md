# Gutter spread and inlets (IDCM 2012 §4.4–4.5, HEC-22)

Inlets are listed in flow order along one gutter. For each inlet, gutter Q = local Q + bypass from the
inlet above.

| Item | Method |
|---|---|
| Spread and capacity | IDCM Eq. 4-4-1, Q = (0.56/n) Sx^1.67 S^0.5 T^2.67 (exponents as printed), solved for T; depth d = T·Sx |
| Gutter velocity | IDCM Eq. 4-4-2, V = (1.12/n) S^0.5 Sx^0.67 T^0.67 |
| Allowed spread | Table 4-6: local to the crown (width/2); collector and two-lane arterial (width − 12)/2; three-lane (width − 24)/2; more lanes (width − 36)/2. Assumes equal flow on both sides. No curb overtopping: d ≤ curb height |
| Curb opening on grade | HEC-22 Eq. 4-22 LT = 0.6 Q^0.42 S^0.3 (1/(n Se))^0.6, E = 1 − (1 − L/LT)^1.8; depressed: Se = Sx + (a/W)·Eo, Eo = 1 − (1 − W/T)^2.67 |
| Grate on grade | E = Rf Eo + Rs (1 − Eo), Rf = 1 − 0.09 (V − Vo), Rs = 1 / (1 + 0.15 V^1.8 / (Sx L^2.3)). Vo (splash-over) must be entered from HEC-22 for the grate type |
| Combination on grade | Taken as the grate (IDCM §4.5.2: Type B is a grate for continuous-grade capacity) |
| Sag | Ponded depth d where capacity = gutter Q: curb weir Cw (L + 1.8W) d^1.5 (Cw 2.3 depressed, 3.0 otherwise) or orifice 0.67 h L √(2g(d − h/2)); grate weir 3.0 (L + 2W) d^1.5 or orifice 0.67 A √(2gd); the lesser governs. Combination = grate + curb opening |
| Clogging | IDCM Table 4-11: grate 40 %, combination 33 %, curb 0 %. A combination (Type B) inlet on grade is a grate (§4.5.2), so 40 % on grade and 33 % only in a sag. Applied by reducing L (and W and A for sag grates) |

**Apply to network** sets each named structure's inflow to the flow it intercepts.

Decision (D-054): the IDCM exponents (1.67, 2.67, 0.67) are used as printed so results match hand checks
against the manual; they differ from HEC-22's 5/3, 8/3 and 2/3 by under 0.2 % in spread. Grate splash-over
velocity is an input rather than built-in coefficients, so the value comes from the HEC-22 chart for the
specific grate.
