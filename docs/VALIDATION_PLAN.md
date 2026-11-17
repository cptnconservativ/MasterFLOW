# Validation plan

## Principles
1. The engine is never validated only against itself or only against Flow Calc.
2. Reference values come from (a) closed-form analytical solutions, (b) an independent implementation
   (`validation/hand_calcs.py`: different language, closed forms + 300-step bisection), (c) published
   examples, (d) later, HEC-RAS / FHWA Hydraulic Toolbox runs where the comparison is equivalent.
3. Tolerances are fixed **before** comparison:

| Comparison | Tolerance | Rationale |
|---|---|---|
| Engine vs closed form | 1×10⁻¹⁰ relative | both exact to double precision |
| Engine vs independent hand calc | 1×10⁻⁸ relative (1×10⁻⁶ where the reference was printed to 7 significant figures) | limited only by printed digits |
| Engine vs published example | ± half the last printed digit | textbook rounding |
| Engine vs Flow Calc | ± half the last displayed digit, after accounting for documented constant differences | display rounding |

4. A discrepancy is explained, not absorbed by loosening a tolerance.

## Matrix (per geometry)
| Category | Rectangular | Trapezoidal | Triangular |
|---|---|---|---|
| Basic | R1 Q from y | T1 Q from y (Flow Calc Example #1) | V1 symmetric closed forms |
| Typical engineering | R2 normal depth | T2 Chow Ex. 6-2; T3 asymmetric; S2 SI | V2 asymmetric supercritical |
| Boundary | B1 Q = 1e-4 cfs; B3 S = 20 % | overtopping; zero bottom width = triangle | one vertical wall |
| Invalid input | b ≤ 0, NaN | negative z, b = 0 with vertical walls | both z = 0 |
| Convergence | near-critical S = Sc (Fr = 1) | B2 Q = 1e5 cfs (bracket expansion) | Brent stress tests (step function, iteration limit, no sign change) |
| Round trip | — | Q → yn, S, n back-solved | — |
| SI | S1 | S2 | — |

## Still to do
- HEC-RAS / FHWA Hydraulic Toolbox comparison for the trapezoidal and irregular cases (Phase D).
- Controlled one-variable Flow Calc series (blocked: typed input did not register over remote control).
