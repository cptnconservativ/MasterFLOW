# Hydraulic methods

Scope of v0.2: **steady, uniform flow in prismatic open channels** of rectangular, trapezoidal and
triangular section, and **circular conduits flowing partly full** (free surface). Normal depth (uniform flow) and critical depth (minimum specific energy for the
discharge) are always reported separately. Gradually varied, rapidly varied, unsteady and pressurised
flow are **not** modelled.

Code: `src/MasterFlow.Hydraulics/UniformFlowCalculator.cs`, `Geometry/SectionGeometry.cs`,
`Numerics/RootFinder.cs`.

## 1. Units
Calculations run entirely in one consistent system chosen per calculation; the engine never converts.

| System | Length | Q | k (Manning) | g |
|---|---|---|---|---|
| U.S. customary | ft | cfs | 1.486 | 32.174 ft/s² |
| SI | m | m³/s | 1.0 | 9.80665 m/s² |

k = 1.486 is the conventional U.S. value (Chow 1959; FHWA HDS-4). The exact conversion is
(1/0.3048)^(1/3) = 1.48592; the 0.005 % difference is below any meaningful precision of n.
Evidence suggests Flow Calc uses 1.48592 (see FLOWCALC_TEST_CASES.md), which explains last-digit
differences. **Decision D-004** keeps 1.486 so results match U.S. hand calculations; a setting may be
added if users want exact-conversion results.

Display conversions: slope is entered and displayed in percent (labelled "%") and divided by 100 in
the view model before reaching the engine. No other conversions exist in v0.1.

## 2. Geometry (general trapezoid; Chow Table 2-1)
Bottom width b ≥ 0, side slopes zL, zR ≥ 0 (H:V). Rectangular: zL = zR = 0. Triangular: b = 0.

- A = y (b + (zL + zR) y / 2)
- P = b + y (√(1+zL²) + √(1+zR²))
- T = b + (zL + zR) y
- R = A/P, D = A/T

Validity: rectangular b > 0; trapezoid b ≥ 0 with b > 0 or zL+zR > 0; triangle zL+zR > 0.
Optional channel depth does not limit the calculation; exceeding it gives warning W-G-001 (the walls
are assumed to continue at the same slope).

## 3. Manning's equation
Q = (k/n) A R^(2/3) S^(1/2). Conveyance K = (k/n) A R^(2/3); section factor AR^(2/3) = Qn/(k√S).

Assumptions: uniform flow (friction slope = bed slope), prismatic channel, constant n over the wetted
perimeter, small slope (depth measured vertically, cos θ ≈ 1), fully rough turbulent flow, hydrostatic
pressure. Applicability: Manning n values are empirical; the result is only as good as n.

| Solve for | Method |
|---|---|
| Discharge | Direct evaluation |
| Slope | Closed form S = [Qn/(kAR^(2/3))]² |
| n | Closed form n = kAR^(2/3)S^(1/2)/Q (warning W-N-001 outside 0.008–0.20) |
| Normal depth | Bracketed root of f(y) = Q(y) − Q (section 5) |

For these sections Q(y) increases strictly with y (walls never converge upward), so the normal depth
is unique. This is **not** true of closed conduits near the crown (Phase C will report both roots).

## 4. Critical flow
Critical depth satisfies Q²T/(gA³) = 1, solved as Z(y) = A√(A/T) = Q/√g (Z increases monotonically).
Reported: yc, Vc, Ac, Tc, Ec = yc + Vc²/2g, Z, and critical slope Sc = [Qn/(kAcRc^(2/3))]²
(the slope at which this Q would flow uniformly at yc with the same n).
Closed-form checks: rectangle yc = (q²/g)^(1/3); symmetric triangle yc = (2Q²/(gz²))^(1/5).

## 5. Froude number and regime
Fr = V/√(gD), D = A/T. Regime: subcritical Fr < 0.86; near-critical 0.86 ≤ Fr ≤ 1.13 (warning
W-F-001; unstable water surface, USACE EM 1110-2-1601 recommends avoiding this range);
supercritical Fr > 1.13. Specific energy E = y + V²/2g relative to the invert.

## 6. Numerical method
Brent's method (Brent 1973): inverse quadratic interpolation and secant steps, falling back to
bisection, so convergence is guaranteed once a sign change is bracketed — no derivatives, no Newton.

| Item | Value |
|---|---|
| Lower bracket | y = 0 (Q = 0, Z = 0) |
| Upper bracket | start at 1 length unit, doubled until the sign changes; each doubling moves the lower bound up |
| Search limit | 1×10⁵ length units → error E-X-002 if not bracketed |
| Depth tolerance | 1×10⁻¹² relative (0.5·tol·(1+|y|)) |
| Max iterations | 200 (Brent) + up to 200 expansions |
| Acceptance check | after convergence, |Q(y) − Q|/Q ≤ 1×10⁻⁸ (and the same for Z), else error E-X-002 |
| Failure behaviour | `Succeeded = false`, all numbers NaN, error message; UI and report show "No result" |

Every solve writes a trace line: method, function, final bracket, iterations, root.

## 7. Validation rules and messages
| Code | Severity | Condition |
|---|---|---|
| E-G-001 | Error | Invalid geometry (see §2), non-finite or ≤ 0 channel depth |
| E-N-001 | Error | n ≤ 0 or not a number |
| E-S-001 | Error | S ≤ 0 (no normal depth on horizontal or adverse slopes) |
| E-Q-001 / E-Y-001 | Error | Q or y ≤ 0 when they are inputs |
| E-X-001 / E-X-002 | Error | Non-finite result / non-convergence or residual above limit |
| W-N-001 | Warning | n outside 0.008–0.20 |
| W-S-001 | Warning | S > 10 % (Manning less reliable; air entrainment, vertical-depth assumption) |
| W-S-002 | Warning | S < 0.001 % (uniform flow unlikely to develop) |
| W-G-001 | Warning | Depth exceeds channel depth |
| W-F-001 | Warning | Near-critical flow |
| I-F-001/002 | Info | Regime statement |
| E-P-001 | Error | Q above maximum free-surface conduit capacity |
| E-P-002 | Error | Depth above the conduit crown |
| W-P-001 | Warning | Two normal depths (Qfull < Q ≤ Qmax) |
| W-P-002 | Warning | Depth above 90 % of the rise |
| I-P-001 | Info | Conduit just full; Froude undefined |

## 8. Circular conduits (partly full)
Geometry (Chow Table 2-1): θ = 2 arccos(1 − 2y/D); A = D²/8 (θ − sin θ); P = Dθ/2; T = 2√(y(D − y)).
Full: A = πD²/4, P = πD, R = D/4, T = 0. Diameter is entered in inches (U.S.) or millimetres (SI) and
divided by 12 or 1000 once, in the view model; the engine receives feet or metres. Depth stays in ft/m.

Because the wetted perimeter grows quickly near the crown, Q(y) **peaks below the crown** at
y ≈ 0.938D with Qmax ≈ 1.0757 Qfull (verified: 0.93818, 1.07571). Consequences:

| Discharge | Normal depth result |
|---|---|
| Q ≤ Qfull | One root on (0, y_Qmax] |
| Qfull < Q ≤ Qmax | Two roots: reported normal depth = lower root; "alternate normal depth" = upper root; warning W-P-001 |
| Q > Qmax | **Error E-P-001**: free-surface flow impossible; conduit would surcharge (pressurised flow not modelled) |

Method: golden-section search for the peak on [0.5D, D] (tolerance 1×10⁻¹² D), then Brent on each
branch. When solving Q, S or n from a given depth, the alternate depth for the resulting Q is also
reported if it exists. Depth > D is error E-P-002. At y = D the pipe is "just full": T = 0, so D = A/T and
Fr are undefined (NaN, info I-P-001, regime "Undefined"). Above 90 % full warning W-P-002 notes that
A/T and Fr lose meaning. Critical depth uses the same Z(y) root with the upper bracket just below the
crown (Z → ∞ as T → 0, so a root always exists).

Additional outputs: y/D, percent full, Qfull, Vfull, slope to carry Q just full = [Qn/(kAfRf^(2/3))]²,
Qmax, depth at Qmax, Q/Qfull.

Assumption: the conduit is a true circle with smooth uniform n; joints, deflection, debris and
sediment are not represented. The "just full" values describe gravity flow, not pressure flow.

## 9. Rating tables
Each row is a full call to the same calculator (same validation, convergence and acceptance checks).
Rows that fail are omitted and reported, never interpolated. Limit 5000 rows.

## References
Chow, V. T. (1959) *Open-Channel Hydraulics*. McGraw-Hill. · Brent, R. P. (1973) *Algorithms for
Minimization without Derivatives*. · USACE EM 1110-2-1601 (1994) *Hydraulic Design of Flood Control
Channels*. · FHWA HDS-4 *Introduction to Highway Hydraulics*.
