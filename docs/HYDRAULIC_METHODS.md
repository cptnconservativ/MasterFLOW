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
Programs that use 1.48592 differ from MasterFlow in the last digit (see REFERENCE_TEST_CASES.md). **Decision D-004** keeps 1.486 so results match U.S. hand calculations; a setting may be
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

## 8a. Elliptical conduits (v0.3.0)
Two definitions, chosen in the editor (D-016, D-017):

**ASTM C507 standard sizes (HE and VE).** The 24 horizontal-elliptical sizes of ASTM C507-22 Fig. 5
(15 to 144 in equivalent round) are stored with their published rise, span, R1 (end/invert radius
pair) and tabulated waterway area. The shape is the four-centred construction: two arcs of radius R1
and two of radius R2, tangent where they meet. The printed R2 is rounded and does not close the shape
exactly, so MasterFlow **derives R2 from tangency** with the published rise, span and R1:
R2 = ((M − R1)² + m² − R1²) / (2(m − R1)), with M and m the semi-major and semi-minor axes. The derived
R2 differs from the printed value by at most about 1.5 in, and the resulting full areas agree with
every tabulated area within 0.6 % (validation/astm_c507_check.py; largest difference 0.54 %).
VE pipes are the same shape rotated 90°: rise and span are swapped and the small arcs sit at the sides
of the invert and crown. VE is offered only where C507 lists it. In SI the inch dimensions are
converted at exactly 0.0254 m/in (D-018).

**Custom size.** A true ellipse with the entered rise and span (x²/a² + y²/b² = 1). No end radius is
needed.

Geometry: each shape is a stack of circular or elliptical arc pieces, half-width
w(y) = cx + k√(ρ² − (y − cy)²). Area is integrated in closed form per piece; wetted perimeter is the
closed-form arc length for circular pieces and 16-point Gauss–Legendre on 8 panels for the true
ellipse (agrees with Ramanujan's second approximation to 1×10⁻¹⁰). T = 2w(y); the tests check
T = dA/dy numerically.

Hydraulics: identical to circular conduits (§8). Q(y) peaks below the crown, so the same golden-section
peak search, two-root reporting (W-P-001), capacity error above Qmax (E-P-001), depth-above-rise error
(E-P-002), just-full handling and near-crown warning apply, with "rise" in place of D. Outputs use
"Depth / rise" instead of y/D.

Assumption: the pipe matches the nominal C507 shape; manufacturing tolerances, joint gaps and
deformation are not represented.

## 8b. Irregular (station/elevation) sections (v0.4.0)
**Input.** Points left to right looking downstream: station, elevation, optional n, optional
description. Stations must not decrease (equal stations = vertical wall; overhangs rejected). At least 3
points, finite values, some relief. Identical consecutive points are ignored. A point's n applies to the
segment to the next point; blank n uses the calculation's n (D-022). Depth y is measured from the
lowest point; water-surface elevation WSE = z_min + y is reported.

**Geometry (exact for straight segments).** Each segment contributes: fully wet A = w(y − z̄),
P = segment length, T = w; partly wet, the triangle up to the crossing; vertical, P = wetted height.
A segment lying exactly at the water surface is dry, so A is continuous but P and T step up when a flat
segment wets. All parts below the water surface count, including pools cut off by a ridge (warning
W-I-002). If the water is above an end point, a vertical wall is assumed there and its height counts as
wetted perimeter (warning W-I-003, D-021).

**Composite roughness** (only when segment n values differ):
- *Conveyance subdivision* (default; HEC-RAS approach): the wetted area is split by vertical lines where
  n changes; K = Σ (k/nᵢ) Aᵢ Rᵢ^(2/3), Q = K√S. The report lists each subarea's A, P, K, Q, share and V.
  Effective n = k A R^(2/3) / K is reported and can be **lower than every subarea n**, because the deep
  main channel carries most of the flow. Energy coefficient α = Σ(Kᵢ³/Aᵢ²)/(K³/A²) is reported.
- *Equal velocity* (Horton 1933 / Einstein 1934): nc = [Σ Pᵢ nᵢ^1.5 / P]^(2/3) over the whole area.
- *Per-segment conveyance*: K = Σ (k/nᵢ) Aᵢ Rᵢ^(2/3) with one subarea per
  sloping segment, split by vertical lines at every point. End walls join the end subareas.
  Checked against an independent hand calculation (`validation/hand_calcs_per_segment.py`) on the sample creek: yn 6.694 ft, A 181.05 ft², effective n 0.0272.
**Vertical walls with their own n** (both subdivision methods): a vertical segment has wetted perimeter
but no area, so on its own it would carry no conveyance and its friction would be lost. It joins the
nearest subarea with area on the side its face looks into (a wall stepping down to the right faces right;
the other side if there is none). That subarea's n becomes the equal-velocity composite of its parts,
nc = [Σ Pⱼ nⱼ^1.5 / Σ Pⱼ]^(2/3), which leaves Σ P n^1.5, and so the Horton–Einstein result, unchanged
(D-060). Check: 10-ft rectangle, concrete walls n 0.015, bed n 0.030, y = 2 ft, S = 0.001 gives the
hand-calculated Q with P = 14 ft and nc from the three parts.

HEC-RAS subdivides only the overbanks and treats the main channel as one subarea between bank stations;
MasterFlow has no bank stations and breaks at every n change, which is the same when the main channel
has one n.

**Solving.** Q(y) and Z(y) = A√(A/T) are not monotonic for compound sections (they step down when a flat
overbank wets). The solver samples each interval between point elevations in 16 steps (with a budget of
2048 samples in total: a section with more than 128 distinct elevations gets fewer steps per interval, at
least one, because its intervals are already short; D-034), refines every
sign change with Brent's method, and uses upward expansion above the highest point (where both are
monotonic). Sign changes caused by a step rather than a root are recognised (residual stays large at a
point elevation) and ignored, with a trace note.
- Several normal depths: all are listed; the highest governs (W-I-001, D-019).
- Critical depth: only roots where specific energy has a minimum (Z − Q/√g rising) qualify; the one with
  the lowest specific energy governs; others are listed (W-I-004, D-020). Velocity head and Fr use α = 1.
- Solving for n requires a single n (error E-N-002 when points carry their own n).

## 8c. Additional outputs (v0.5.0)
- Velocity × depth V·y at normal depth (yn is the maximum depth of the section).
- Mean boundary shear τ = γ R S and maximum shear at the deepest point τmax = γ y S (FHWA HEC-15
  form), with γ = 62.4 lb/ft³ (U.S.) or 9810 N/m³ (SI). For channel-lining checks; τmax = γyS is the
  usual straight-channel estimate and does not include bend or side-slope factors.
- With an optional invert elevation z: water surface (HGL) = z + yn, energy grade (EGL) = z + yn + V²/2g
  (α = 1), critical water surface = z + yc. Irregular sections use their own point elevations.
- Manning's n reference list: Chow (1959) Table 5-6 ranges (selection), a starting point only.

## 8d. Design checks (H1)

Optional limits entered by the engineer, in the calculation's units; a blank limit is not checked.

| Check | Computed value | Passes when | Applies to |
|---|---|---|---|
| Lining shear | τd = γ·yn·S (FHWA HEC-15, 3rd ed. 2005, eq. 2.3: shear at the deepest point of a straight channel); γ = 62.4 lb/ft³ or 9810 N/m³ | τd ≤ τp; factor of safety τp/τd reported | Open channels |
| Freeboard | Channel depth − yn (regular shapes); lower end-point elevation − normal WSE (irregular) | f ≥ f req. | Open channels; not evaluated without a channel depth |
| Maximum / minimum velocity | Mean normal-flow velocity V | V ≤ Vmax; V ≥ Vmin | All shapes |

Failures are warnings W-C-001 (shear), W-C-002 (freeboard), W-C-003 (V above max), W-C-004 (V below min);
an unreadable limit is W-C-000 and is not checked. Not included: bend shear (HEC-15 Kb), steep-slope
riprap methods, and permissible-shear values themselves; the engineer enters τp for the lining from the
governing manual. Hand check (tests): rectangle b = 10 ft, y = 2 ft, S = 1 %: τd = 62.4 × 2 × 0.01 =
1.248 lb/ft²; freeboard with channel depth 3 ft = 1.000 ft.

## 9. Rating tables
Each row is a full call to the same calculator (same validation, convergence and acceptance checks).
Rows that fail are omitted and reported, never interpolated. Limit 5000 rows.

## 10. Storm sewer HGL and EGL (H11)

Steady flow through a tree-shaped network of circular and elliptical pipes draining to one or more
outfalls. Each pipe carries a design Q: typed, or accumulated as Qpipe = Qinflow(upstream structure) +
Σ Q(pipes into that structure), using typed values where given. No hydrology or routing is computed (peak
flows are added as entered, a conservative simplification the user should be aware of). An optional
minor-storm (check) flow is accumulated the same way and used only for the minimum-velocity check: the
mean velocity at normal depth for that flow (full-flow velocity if it exceeds the full-flow capacity), as
the IDCM's 2.5 fps minimum applies to the 2-year storm. Without a minor flow the design-flow velocity is
checked and the message says so.

**Outfall.** Tailwater rule per network: free outfall (depth = yc), a known tailwater elevation, (yc + D)/2
above the outlet invert (the HEC-22 convention) or the outlet crown. The depth at any pipe outlet is never
taken below critical depth: if the downstream water level is lower, the pipe starts at yc and the structure
level is reported as not controlling (I-NW messages).

**Pipes.** The energy equation is integrated upstream by the standard step,
z₁ + y₁ + V₁²/2g = z₂ + y₂ + V₂²/2g + S̄f·Δx, with Sf = (Q·n / (k·A·R^⅔))² and S̄f the average of the
two ends; steps are L/(0.25·D), between 40 and 2000 per pipe. Each step's depth is found by bisection on
a bracket, never by an unbracketed Newton step; a step with no root stops the solve with E-NW-020 rather
than returning a number. Above the crown A and P stay at the full-pipe values, so the same equation
carries pressure (surcharged) flow and the HGL rises above the crown at the full-flow friction slope.

**Steep pipes.** When no subcritical profile exists from the outlet, the pipe is computed supercritical
from yc at the entrance (S2/S3 profile). If the outlet water level is high enough, a hydraulic jump is
placed where the momentum function M = Q²/(gA) + A·ȳ of the supercritical and subcritical profiles are
equal; above the crown M carries the extra pressure term A_full·(y − D). If the subcritical depth is
below the sequent depth everywhere the jump is swept out of the pipe and reported.

**Structures.** The structure water level (HGL) is the pipe-outlet HGL of the pipe leaving it plus a loss,
and it becomes the downstream control for every pipe entering it.
- Rapid City IDCM (2012) Figs. 4-6 and 4-7 (credited there to the City of Waco):
  hj = V2²/2g − c·V1²/2g, V2 the outlet pipe velocity, V1 the inflow velocity.
  c = 0.5 inlet on main line (Case I), 0.25 inlet with branch lateral (II), 0.5 manhole with 45° lateral (III),
  0.25 manhole with 90° lateral (IV), 0.35 with 60°, 0.75 with 22.5°, 0.75 45° wye or cut-in (V);
  beginning of line hj = 1.25·V2²/2g (VI); bends with radius = D: 0.50/0.43/0.35/0.20·V2²/2g for 90°/60°/45°/22.5°.
- Standard method, IDCM Eq. 4-6-1: hs = K·Vo²/2g with K per structure or the network default.
- *Auto*: no inflow → beginning of line; one inflow → Case I; more → Case II at inlets, Case IV otherwise.
- A negative hj is taken as 0 and reported (W-NW). V1 is the velocity of the inflow pipe carrying the most
  flow (decision D-042; the IDCM does not say which inflow to use, and this reading was confirmed by the
  project engineer on 2026-11-18). The EGL at a structure is the HGL plus the outlet velocity head, and never below
  the HGL.

**Criteria (optional, per network).** HGL and/or EGL a set distance below the rim, minimum and maximum
velocity, no pressure flow, minimum pipe size and slopes below critical. The *Rapid City IDCM 2012* preset
sets HGL ≥ 1 ft below the rim (§4.6.7), 2.5 to 20 fps, 12 in. minimum, slopes below critical and no
surcharge. A check that fails is a warning (W-NW), shown in red in the tables and on the profile; the
solve still reports every number so the designer can see by how much.

**Outputs.** Pipes table (size, slope, Q, Qfull, yn, yc, regime, velocities, HGL/EGL at both ends, hf),
structures table (rim, HGL, EGL, rim − HGL, required, OK/FAILS, loss method and equation, hj, V2, V1), the
profile of every path from a beginning of line to its outfall (station 0 at the outfall), messages and the
step-by-step trace. Under each profile a band gives the station, HGL, EGL and the criterion limit
(rim/ground − required clearance) every 50 ft (15 m in SI) and at the upstream end; the ground between
structures is a straight line between rims, and at a structure the upstream-side value is read. The same
numbers go to the app, PDF, HTML and Excel (readings and profile points as numbers).

**Automatic sizing.** Pass 1 (upstream first): the smallest standard circular size, not below the minimum
size or any pipe upstream, with Qfull = (k/n)(πD²/4)(D/4)^⅔S^½ ≥ Q when pressure flow is not allowed and
normal-depth velocity ≤ the maximum. Pass 2: for each structure failing the HGL criterion (or each pipe
flowing full when not allowed), every circular pipe on its path to the outfall is tried one size larger
(pipes downstream kept at least as large) with a full re-solve, and the change that lowers that structure's
water level most is kept; repeated until met or no change helps. Sizes whose crown would rise above a rim
are not used. Greedy, so the result is a sound design, not a proven minimum-cost one.

**Limits.** Tree networks only (no loops, one outlet pipe per structure); no hydrology, routing or
inlet capacity (H7); no partial-flow correction to junction losses beyond the equations above; no
gradually varied flow in open channels downstream of the outfall.

## References
Chow, V. T. (1959) *Open-Channel Hydraulics*. McGraw-Hill. · Brent, R. P. (1973) *Algorithms for
Minimization without Derivatives*. · USACE EM 1110-2-1601 (1994) *Hydraulic Design of Flood Control
Channels*. · FHWA HDS-4 *Introduction to Highway Hydraulics*. · ASTM C507-22 *Reinforced Concrete
Elliptical Culvert, Storm Drain, and Sewer Pipe*, Fig. 5. · Horton, R. E. (1933) and Einstein, H. A. (1934)
equal-velocity composite n, as given in Chow (1959) §6-6. · USACE HEC-RAS *Hydraulic Reference Manual*,
conveyance subdivision. · City of Rapid City (2012) *Infrastructure Design Criteria Manual*, Ch. 4 Storm
Sewer Design, §4.6 (HGL, Figs. 4-6/4-7, Eq. 4-6-1). · FHWA HEC-22 (2009) *Urban Drainage Design Manual*, tailwater convention.
