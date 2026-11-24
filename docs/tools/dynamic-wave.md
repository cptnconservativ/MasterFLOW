# Dynamic-wave storm sewer routing

Tool id `dynamic-wave` (premium). Routes inflow hydrographs through the selected storm network by the full dynamic
wave (St. Venant) equations. It uses the link-node method of EPA SWMM 5 (Rossman, *SWMM Reference Manual Vol. II:
Hydraulics*, 2017, Ch. 3).

Where the steady HGL/EGL run assumes every pipe carries its peak flow at once, this run follows each flood wave in
time. It accounts for storage in pipes and structures, backwater from the outfall, surcharge, flooding at the rim
and flow reversal.

## Input

| Key | Meaning |
|---|---|
| `hydrograph` | `triangle` (default) or `table` |
| `time to peak`, `recession ratio` | Triangle shape, min; recession = ratio × time to peak (NRCS 1.67) |
| `duration` | Simulated time, min (default: end of the hydrographs + 30 min) |
| `time step` | s; blank = automatic, half the smallest open-channel Courant time, 0.25–5 s |
| `manhole area` | Plan area of each structure (default 12.57 ft², a 4-ft manhole) |
| `junction losses` | `yes`: K·V²/2g at each pipe entrance, K from the upstream structure or the network default |

Each structure's triangle peaks at its design inflow. That is the structure's inflow when given, otherwise its typed
outgoing pipe flow less its typed incoming pipe flows. `[Hydrographs]` takes a Time (min) column and one flow column
per structure.

## Method

**Links (pipes).** The momentum equation of each pipe is solved for the flow at time t + Δt (SWMM Eq. 3-13):

Q = (Q_old − ΔQ_pressure + ΔQ_inertial) / (1 + ΔQ_friction + ΔQ_loss)

- ΔQ_friction = g·n²·|V|·Δt/(k²·R^(4/3)).
- ΔQ_pressure = g·Ā·(H₂ − H₁)·Δt/L.
- The inertial terms 2·V·(Ā − Ā_old) and V²·(A₂ − A₁)·Δt/L are damped by σ: σ = 1 below Fr 0.5, 0 above Fr 1.
- ΔQ_loss = K·|V₁|·Δt/(2L).
- Areas and the hydraulic radius come from the mid-length depth (full-pipe values when both ends are full).
- A downstream free fall holds its end at min(critical, normal) depth.
- Flow is limited to Manning flow at the upstream depth when the water surface is steeper than the pipe (SWMM's
  "slope" normal-flow limit).

**Nodes (structures).**
- *Free surface.* Continuity gives H = H_old + ½(net_old + net)·Δt/A_s. A_s is the structure plan area plus half the
  surface area of each connected pipe.
- *Surcharged* (above the highest connected crown). The SWMM surcharge algorithm sets the head so that net inflow
  balances storage, through ΣdQ/dH.
- *Flooding.* The head cannot rise above the rim, and the excess inflow leaves the system as flood volume (SWMM's
  default, no ponding). Structures without a rim are sealed.
- *Outfall.* The outfall holds the network's tailwater elevation, or min(critical, normal) depth in the outlet pipe if
  that is higher. With a fixed tailwater, the run starts with the system level at the tailwater (a static start).

**Iteration.** Each time step runs up to 8 Picard trials with 0.5 under-relaxation after the first, until every head
changes by less than 0.005 ft.

## Output

- **Structures.** Peak HGL and its time, the steady HGL from the network run and the difference, rim − HGL against
  the network criteria (Rapid City 1 ft), surcharge and flood duration, flood volume and peak inflow.
- **Pipes.** Peak flow and its time, design Q, peak velocity, peak depth/rise, full-flow time and the smallest
  Courant time.
- **Continuity.** Inflow, outfall, flooding and storage change.
- **Charts.** HGL against time, pipe flows against time, and the peak HGL profile along the longest path against
  inverts, crowns, rims and the steady HGL.

## Decisions

- **D-380** SWMM's link-node method and term names are followed so that results can be compared with SWMM, for
  example after `swmm-exchange` export. SWMM's variable time step is not used: the run takes one fixed step, with a
  warning when it exceeds a pipe's Courant time.
- **D-381** Structure losses use the standard K·V²/2g at each pipe entrance (IDCM Eq. 4-6-1, the network's K), not
  the IDCM Figure 4-6/4-7 case equations. Those depend on the ratio of inflow to outflow velocity, which has no
  stable unsteady form. So the steady and dynamic HGLs differ partly through the loss method.
- **D-382** Storage is accounted the link-node way: each pipe's volume is assigned half to each end structure
  through its surface width. The continuity error therefore measures the solution's convergence, as in SWMM.
- **D-383** No surface ponding or overland return: flood volume leaves the system. The overland flow tools handle
  where it goes.
- **D-385** After the inflow stops, water in pipes under a fixed tailwater can slosh slowly between structures,
  like a U-tube. In the sample this is about ±0.04 ft and ±0.4 cfs, and it does not shrink with a smaller time step.
  It is the undamped physical oscillation of the link-node model, because friction vanishes as V → 0. It is not an
  instability.
- **D-384** Not compared against SWMM itself, which cannot run here. The checks are Manning normal depth for steady
  part-full flow, the full-pipe friction slope and K·V²/2g for steady pressure flow (both within 0.01 ft), a static
  tailwater at rest, and continuity within 0.5 % with and without flooding.

## Messages

| Code | Meaning |
|---|---|
| E-DW-001 | No storm network selected |
| E-DW-002..006 | Bad hydrograph type, triangle shape, manhole area, time step or duration |
| E-DW-007 | Pipe without rise, length or n |
| E-DW-008/009 | `[Hydrographs]` missing or naming an unknown structure |
| E-DW-010 | No design inflows for the triangle hydrographs |
| W-DW-001 | Structures without a rim are sealed |
| W-DW-002 | Structure floods |
| W-DW-003 | Peak HGL closer to the rim than the criterion |
| W-DW-004 | Time step above a pipe's Courant time |
| W-DW-005/006 | Continuity error above 5 %, or many time steps not converged |
| I-DW-001 | Reversed peak flow |
| I-DW-002 | Dynamic vs steady HGL, and the IDCM §4.6.7 steady requirement |
