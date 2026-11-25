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

## Worked examples from the design manuals

Each case runs the manual's printed inputs through the MasterFlow tool (`tests/MasterFlow.IntegrationTests/ManualWorkedExampleTests.cs`).
Manuals: FHWA HEC-9 (2005), HEC-11 (1989, SI), HEC-15 (2005), HEC-17 (2016), HEC-20 (2012), HEC-21 (1993), HEC-23 (2009),
HDS-2 (2024), HDS-6 (2001), HDS-7 (2024) with FHWA-HRT-09-028 (2009), NRCS TR-210-60 (2019), and the ADS StormTech catalog.

| Manual, example | Inputs | Manual | MasterFlow | Note |
|---|---|---|---|---|
| HEC-11 §5.4.1 Example 1 | Va 2.96 m/s, d 3.60 m, 1V:2H, φ 41°, Ss 2.65 | K1 0.73; D50 bed 0.085 m, bank 0.131 m | K1 0.73; 0.081 m, 0.130 m | Manual reads Chart 1; Eq. 6 gives 0.081 m |
| HEC-15 §6.3.1 riprap (CU) | Q 40 cfs, B 2 ft, Z 3, S 0.02, D50 5 in. | d 1.72 ft, n 0.062, D50 ≥ 0.45 ft, fails | d 1.72 ft, n 0.062, D50 ≥ 5.3 in., fails | Found and fixed the U.S. Blodgett constant (0.319 → 0.262) |
| HEC-15 §4.3 grass (CU) | Q 17.5 cfs, B 3 ft, Z 3, S 0.03, Cn 0.142, Cf 0.9, τp,soil 0.068 | d 0.70 ft, n 0.032, τd 1.31, τp 2.7 lb/ft² | 0.70, 0.032, 1.31, 2.66 | Agrees |
| HEC-17 Table 6.2 | T and service life n | e.g. 100-yr, 50 yr: 0.39 | 0.39 | All six cells checked |
| HEC-20 §6.5.2 Example 2 | Q 780 cfs, W 350 ft, S 0.0014, n 0.027, D90 0.96 mm | y 1.05 ft, Seq 0.000108 | 1.05 ft, 0.000108 | Example 1 uses log-law shear, not in the tool |
| HEC-21 Example 1 | 500 ft, 3 %, Wp 18 ft, i 6.0 in/hr, T 10 ft | Lo 1,837 ft (no inlets) | 1,849 ft | Manual reads Chart 5 |
| HEC-21 Example 3 | 1,200 ft, 3 %, Wp 34 ft, i 7.3 in/hr, 1 × 1.5 ft grate | Qf 4.1 cfs, Lo 800 ft, E 0.245, Lc 196 ft | 4.13, 805, **0.265**, **212** | Difference: MasterFlow adds the HEC-22 side-flow term; with E = Eo it gives 196 ft |
| HEC-23 DG 14 §14.4 | Spill-through, y 2.7 ft, SBR 23.2, Qf/Af 7,720/613.5 | V 12.6 ft/s, Fr 1.35, D50 1.1 ft, apron 5.4 ft | 12.6, 1.35, 13.0 in., 5.4 ft | Agrees |
| HEC-9 §4.7 Example 2 (SI), Bent 2 | WS 65.30/64.55, debris bottom 61.98, WD 13.70, Ac 27.41, Vr 3.10, yr 0.99 | Fh 295 kN, FD 79 kN, total 374 kN, el. 63.46 | 297, 83, 379, 63.46 | Manual rounds B to 0.76 (CD 0.36); unrounded B 0.756 gives CD 0.378 |
| HEC-9 §4.7 Example 2 (CU), Bent 2 | WS 214.25/211.82, bottom 203.35, WD 45, Ac 295.07, Vr 10.17, yr 3.25 | Fh 65,966 lb, FD 19,589 lb, el. 208.22 | 66,100 lb, 18,500 lb, 208.22 | **Manual arithmetic slip:** its own inputs give FD 17,700 lb |
| FHWA-HRT-09-028 ch. 5 (HDS-7 §11.1.4) | Six-girder, s 2.5 m, W 11.2 m, h* 0.6 and 2.4 | CD 1.403/1.003, CL −0.955/−1.462, CM 0.247/0.092; 2.114/1.714 …; drag 27.1 kN/m | Same to 3 decimals; 27.06 kN/m | Agrees |
| HDS-2 Examples 6.1, 6.2 | Colorado Mountain, A 66.4 mi², S 26 %, P 28.8 in., SE 40 % | Q25 900 cfs, SE 0.167 log, 600–1,300 cfs | 900, 0.167, 621–1,323 | Manual rounds to hundreds |
| HDS-6 §4.13.8 Problem 7 (Yang) | Q 7,820 cfs, y 7.58 ft, V 3.02 ft/s, S 0.00022, D50 0.00105 ft | 91 ppm, 1,900 tons/day | 95 ppm, 1,980 tons/day | MasterFlow's Rubey fall velocity 0.136 ft/s against the given 0.141 |
| TR-210-60 Figure 2–2, 2–1; ch. 1 breach | Hazard rows, P100, PMP; Vs, Hw, A, L | Rules as printed | Same | Criteria checks, no numeric example in TR-210-60 |
| StormTech catalog | 8 chambers at the catalog layout, 40 % porosity | e.g. SC-310 29.3 ft³, MC-3500 175.0 ft³ | 29.30, 175.0 (all within 0.3 %) | Validates the chamber data |

Not matched: HEC-20 Example 1 (log-law shear), HDS-6 Problem 2 (USBR MPM form with wall correction), HEC-11 Example 2
(chart-based bank geometry), HEC-24 (no complete numeric example; its Eq. 7-6 Vmin = 15·Qp·t is the tool's wet-well rule).
HEC-26, HEC-18 and HDS-5 were not in the reference set.
