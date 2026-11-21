# Channel water surface profile and lining checks

Tool id `channel-profile` (free). A water surface profile for a ditch, swale or channel made of several reaches.
Each reach can have its own section, slope, flow, roughness and lining. The tool checks each reach against Rapid
City IDCM §4.8 and Table 4-14 and against FHWA HEC-15. The single-reach **Backwater / drawdown** tool is
unchanged.

## Profile

* **Reaches** are listed from downstream to upstream. Each one is a trapezoid (b, zL, zR), a rectangle or a
  triangle, with its length, slope, Q, top-of-bank depth and lining.
* **Method:** the standard step (Chow 1959; HEC-RAS Hydraulic Reference Manual Ch. 2):
  WS₂ + V₂²/2g = WS₁ + V₁²/2g + L·(Sf₁ + Sf₂)/2 + C·|Δ(V²/2g)|. Sf comes from Manning's equation, averaged over
  the step. The transition loss C applies only where the section changes: `contraction` (0.1) where the
  velocity head rises in the direction of flow, and `expansion` (0.3) where it falls.
* **Downstream boundary:** `normal`, `critical` or a water surface elevation. Set the bed elevation with
  `downstream invert`.
* **Mixed regime:** a subcritical profile runs upstream from the boundary. A supercritical profile runs downstream
  from critical depth at the head of each steep reach. At every station, the one with the greater specific force
  M = Q²/(gA) + A·ȳ governs, so the jump falls where they cross. HEC-RAS uses the same rule. Where no subcritical
  depth exists, critical depth is used and reported.
* **Two runs**, as IDCM Table 4-14 requires: one with the **capacity n** (the maximum n, for depth and
  freeboard), and one with the **stability n** (the minimum n, for velocity, Froude number and shear). The
  defaults by lining come from Table 4-14. A value on the unsafe side of the table is warned (W-CP-003).

## Checks per reach (IDCM Table 4-14 unless noted)

| Item | Grass, erosive | Grass, nonerosive | Riprap | Concrete |
|---|---|---|---|---|
| Max velocity (stability n) | 5 ft/s | 7 ft/s | 12 ft/s | 18 ft/s |
| Min velocity (capacity n) | 2 ft/s | 2 | 2 | 2 |
| Max Froude (stability n) | 0.5 | 0.8 | 0.8 | — |
| Froude 0.95–1.05 prohibited | §4.8.3, all linings | | | |
| Max depth | 5 ft | 5 ft | — | — |
| Freeboard (capacity n) + velocity head (note 2) | 1.0 ft | 1.0 ft | 2.0 ft | 2.0 ft |
| Longitudinal slope | 0.2–0.6 % | 0.2–0.6 % | 0.2–1.0 % | — |
| Side slope, flattest allowed | 4H:1V | 4H:1V | 2.5H:1V | 1.5H:1V |
| Shear | `tau p` (lb/ft²) if given: τd = γ d Sf ≤ τp (HEC-15 Eq. 2.3) | | | |
| Riprap D50 | — | — | HEC-15 §6.1 (slopes < 10 %): D50 ≥ SF τd / (F* (γs − γ)), F* 0.047, SF 1.0, γs 165 lb/ft³ | — |

The `custom` lining takes your own `V max`, `Fr max`, `tau p` and both n values. Without a D50, the required
D50 is reported, along with its value at transitions, where IDCM §4.8.3(2)(c) raises the velocity by 20 % (shear
× 1.44).

## Outputs

* a reach summary, a criteria table with OK/NO, and the profile table: both runs, WSE, EGL, regime, freeboard,
  velocity, Froude number, τ = γRSf and τd = γdS
* a profile chart (invert, WSE and EGL, stability WSE, top of bank) and a velocity and Froude chart
* a section drawing of each reach at its deepest water

## Validation

* Uniform channel: every station equals Manning normal depth for both n values.
* An M1 backwater over 1500 ft in a 10-ft trapezoid matches the existing single-reach standard step
  (`GradualFlow`) to 0.001 ft.
* A frictionless contraction from a 30-ft to a 20-ft rectangle reproduces E_up = E_down + 0.1·Δhv to 1e-4 ft.
* Steep to mild: critical depth at the head of the steep reach, an S2 drawdown toward normal depth, a
  supercritical zone, then a jump to the subcritical tailwater.
* Riprap D50 is checked by hand against HEC-15 §6.1 at normal depth.

Decisions:

* D-115: free. It is a single-element calculation (D-098).
* D-116: a new multi-reach tool, so the existing Backwater tool stays unchanged. Its SI support and pipe sections
  stay there. This tool is U.S.-only because IDCM Table 4-14 is in ft/s.
* D-117: no grass permissible shear is built in. The IDCM limits grass by velocity and Froude number. HEC-15
  vegetation shear depends on cover class and soil, so it is entered when wanted.
