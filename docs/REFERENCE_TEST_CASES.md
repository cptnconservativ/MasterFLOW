# Reference test cases

Rounded reference values checked against MasterFlow and against the independent hand calculations in
`validation/`. The hand calculations are the authority; the rounded values check display precision
and the effect of the Manning constant k.

| ID | Inputs | Rounded reference | Independent hand calc | MasterFlow | Note |
|---|---|---|---|---|---|
| T1 / I1 | Trapezoid b = 10 ft, z = 2 (also as points 0/5, 10/0, 20/0, 30/5 ft), n = 0.013, S = 1.00 %, y = 1.50 ft, solve Q | Q = 247.07 cfs, A = 19.50, P = 16.71, T = 16.00, D = 1.219, R = 1.167, yc = 2.27, Sc = 0.22 %, V = 12.67, hv = 2.49, E = 3.99, Fr = 2.023 | Q = 247.0851 (k = 1.486); yc = 2.2745; Sc = 0.2205 %; Fr = 2.0235 (`hand_calcs.py`, `hand_calcs_irregular.py`) | Q = 247.0851 | **Q 247.07 vs 247.085 (0.006 %)** is exactly the ratio 1.48592/1.486: k from the exact metric conversion vs the conventional 1.486 (D-004). All other values agree to displayed precision |
| P2 / P3 | Circular D = 36 in, n = 0.013, S = 1.00 %; y = 2.73 ft → Q, and Q = 71.37 → both depths | Q = 71.37 cfs; ND 32.76 in; dual ND 34.66 in; full flow 66.69 cfs; max Q 71.74 cfs at 2.81 ft; % full 91.00 | Q at y = 2.73 ft: 71.374 (k = 1.486) / 71.370 (k = 1.48592); full flow 66.698 / 66.695; roots 2.7295067672 and 2.8886196484 ft (`hand_calcs_circular.py`) | yn = 32.754 in, alternate 34.663 in; Qfull 66.698; Qmax 71.748 at 2.815 ft | Near the crown dQ/dy is small, so rounding Q to 0.01 cfs moves the depth by ~0.01 in. Hydraulic depth A/T exceeds the diameter near the crown, so Fr there has limited meaning and MasterFlow flags it |
| PS1 | Sample 6 creek with overbanks (n 0.06 / 0.035), Q = 500 cfs, S = 0.1 %, per-segment conveyance | yn = 6.69 ft, A = 181.05 ft², effective n = 0.0272 | yn = 6.6943 ft, A = 181.048 ft², n = 0.02723, 7 subareas (`hand_calcs_per_segment.py`) | yn = 6.694 ft | Agrees to displayed precision |

## Planned controlled cases
One-variable-at-a-time series for slope (0, 1e-6, 0.1 %, 1 %, 10 %, 30 %), depth near zero, pipe near
full, full circular pipe, overbank geometry, repeated stations, invalid sections, each against an
independent hand calculation (see VALIDATION_PLAN.md).
