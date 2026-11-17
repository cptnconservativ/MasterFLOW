# Flow Calc comparison cases

Flow Calc is one comparison point, not the authority. Independent values come from
`validation/hand_calcs.py`.

| ID | Source | Inputs | Flow Calc displayed | Independent hand calc | MasterFlow | Note |
|---|---|---|---|---|---|---|
| FC-1 | Example Project, "Example #1" (irregular points 0/5, 10/0, 20/0, 30/5 ft, n = 0.013) | S = 1.00 %, y = 1.50 ft, solve Q | Q = 247.07 cfs, A = 19.50, P = 16.71, T = 16.00, D = 1.219, R = 1.167, yc = 2.27, Sc = 0.22 %, V = 12.67, hv = 2.49, E = 3.99, Fr = 2.023 | Q = 247.0851 (k = 1.486); yc = 2.2745; Sc = 0.2205 %; Fr = 2.0235 | Q = 247.0851 | **Q differs by 0.006 %**: matches if Flow Calc uses k = 1.48592 (exact SI conversion). Inference from one case. All other values agree to displayed precision |
| FC-2 | Example Project, "Example #3" (circular, D = 36 in, n = 0.013) | S = 1.00 %, y = 32.76 in, solve Q | Q = 71.37 cfs; dual ND = 34.66 in; full flow 66.69 cfs; max Q 71.74 cfs at 2.81 ft; % full 91.00; D = 3.933 ft; Fr = 0.939 | Q at y = 2.73 ft: 71.374 (k = 1.486) / 71.370 (k = 1.48592); full flow 66.698 (k = 1.486) / 66.695 (k = 1.48592) | Pipes not yet implemented (Phase C) | Full flow 66.69 matches k = 1.48592 (66.70 with 1.486): **second data point supporting the k inference.** Hydraulic depth A/T exceeds the diameter near the crown, so Fr there is of limited meaning — MasterFlow will flag this |

## Planned controlled cases (blocked: typed input did not register)
One-variable-at-a-time series for slope (0, 1e-6, 0.1 %, 1 %, 10 %, 30 %), depth near zero, pipe near
full, full circular pipe, overbank geometry, repeated stations, invalid sections. To be run when the
user can enter values (or by the user, see VALIDATION_PLAN.md).
