# Network optimizer (premium)

Chooses the pipe sizes and inverts of the selected storm network at **least cost** (pipe price × length, trench
excavation and structure depth) or **least excavation**, while every pipe and structure meets the rules below.
Flows, pipe lengths, n values, structure locations and rims are not changed. **Apply to network** writes the new
sizes and inverts back and re-solves the HGL.

Tool id `network-optimizer`. It is a premium feature: it runs with a license that covers it (see
[licensing](../premium/licensing.md) once the licensing PR is merged).

## Rules every design meets

| Rule | Source / default |
|---|---|
| Part-full capacity at normal depth: Q ≤ `maximum fill` × Qfull (Manning, full section) | IDCM §4.6.1 open-channel design; fill 1.0 |
| Minimum velocity of the minor-storm flow at normal depth (the design flow when a pipe has no minor flow, I-NO-002) | IDCM §4.6.2(3a), 2.5 ft/s for the 2-yr; network criteria |
| Maximum velocity of the design flow at normal depth | IDCM Table 4-13, 20 ft/s; network criteria |
| Slope not steeper than critical for the design flow (`subcritical = yes`) | IDCM §4.6.6(4); network criteria |
| Minimum size | IDCM §4.6.2(2), 12 in.; network criteria |
| Never larger into smaller at a structure | IDCM §4.6.6(2) |
| Inflow crown at or above the outlet crown (crowns matched or a drop) | IDCM §4.6.6(3) |
| Every inflow invert at least `minimum drop` above the outlet invert | 0.1 ft / 0.03 m |
| Cover, rim to the outside top of the pipe, at both ends | `minimum cover` 2.0 ft / 0.6 m; not checked at an outfall |
| Depth, rim to invert, at both ends | `maximum depth` 20 ft / 6 m; not checked at an outfall |
| Invert limits per structure (`[Invert limits]`: minimum and/or maximum for every invert there) | the outfall's minimum defaults to its existing lowest invert |
| Optional slope limits | `minimum slope`, `maximum slope` (percent) |
| HGL at least the network's `HglBelowRim` below every rim (`hgl check = yes`) | IDCM §4.6.7, 1 ft |

Pipe wall is D/12 + 1 in. (SI: + 25 mm), the RCP Wall B approximation the quantity takeoff uses; `wall = <value>`
overrides it.

## Cost

* Pipe: price per LF (per m) from `[Pipe prices]` (size in in. or mm, price). Only the listed sizes are used.
* Trench excavation: width = OD + `side clearance`, depth at each end = rim − (invert − wall − `bedding depth`),
  averaged, ground straight between rims, × `excavation price` per CY (m³). Same rules as the quantity takeoff.
* Structure depth: (rim − outlet invert + `structure base depth` − `base structure depth`) × `structure depth price`
  when positive. Base structure prices are the same for every design and are left out.
* `objective = depth`: the trench excavation volume only (no prices needed; the standard sizes are used when there
  is no price table).

The cost comparison table prices the existing design the same way and lists the rules it breaks (I-NO-003).

## Method (D-080)

Dynamic programming over the branching network, upstream first. Every invert is a multiple of `grid step`
(0.05 ft / 0.01 m). For a pipe of size D, the slope window [S_min, S_max] comes from the capacity, velocity,
critical-slope and slope limits; the cost of the pipe and everything above it, g(e₂, D), is the least over upstream
inverts e₁ with e₁ − e₂ inside the window of h(e₁, D) plus the pipe cost. At a structure, h(e₁, D) is the structure
cost plus, for each inflow, the least g over sizes not larger than D and downstream inverts at least
max(drop, D − D_in) above e₁ (which is the crown rule). Because the pipe cost splits into a part that depends only on
e₁ and a part that depends only on e₂, each pipe is solved with a sliding-window minimum and each inflow with a
suffix minimum, so the run time is linear in the number of invert levels. The result is the **exact optimum on the
grid**; a brute-force enumeration of every design on a small network gives the same cost (test
`DynamicProgram_EqualsBruteForce_OnAChain`).

When no design exists, the first pipe (upstream first) where none is possible is named (E-NO-010), with whether no
size carries the flow or the cover, depth and invert limits cannot all be met.

## HGL loop (D-081)

The HGL depends on the whole network and the tailwater, so it is checked after the optimization with the network
solver. While a structure's HGL is closer to the rim than required, the structure with the largest shortfall is
taken and these changes are tried: one size up on each of up to six pipes on its path to the outfall (largest energy
loss first), and lowering its outlet by the shortfall. Each is re-optimized and re-solved, and the change that gains
the most clearance there per unit of added cost is kept (I-NO-004 lists each one). A structure no change helps is
reported (W-NO-002). This loop is a heuristic: the design it ends with meets every rule, but is not proven to be the
cheapest one that also meets the HGL criterion.

Pipes left flowing full under backwater are listed (W-NO-003); setting `maximum fill` below 1 leaves room for
junction losses.

## Output

* **Pipe design**: old and new size and inverts, slope, the allowed slope window and what sets each end, Q, Qfull,
  Q/Qfull, V, minor V, cover at each end and the flow condition from the HGL solution.
* **Structures**: rim, old and new lowest invert, depth, the highest inflow above the outlet (drop), HGL, rim − HGL
  and the HGL check.
* **Cost comparison**: existing and optimized, by item, with the number of rules each breaks.
* **Chart**: profile of the longest path, ground, existing and optimized inverts, optimized crown and HGL.

## Messages

E-NO-001 no network; E-NO-002 elliptical pipe (circular only); E-NO-003 rim missing; E-NO-004 a structure with more
than one outlet; E-NO-005 a bad value; E-NO-006 least cost without prices; E-NO-007 grid too fine (over 5,000 levels);
E-NO-008 the network does not solve; E-NO-009 the optimized network does not solve; E-NO-010 no feasible design;
W-NO-001 invert limit for an unknown structure; W-NO-002 HGL still too high; W-NO-003 pressure flow; I-NO-001
assumptions; I-NO-002 design flow used for the minimum velocity; I-NO-003 rules the existing design breaks;
I-NO-004 HGL loop changes.

## Decisions

* D-080: exact dynamic program on an invert grid rather than a general-purpose solver: it is deterministic, needs no
  external library, and its optimality can be checked by enumeration. A 60-pipe network solves in about 2 s.
* D-081: the HGL is handled by an outer loop with the full solver, not inside the dynamic program, because the HGL at
  a structure depends on everything downstream and on the tailwater.
* D-082: the outfall invert may not go below its existing invert unless `[Invert limits]` says so, because the
  receiving channel sets it. The sample adds a row allowing 3 ft (1 m) lower, marked as an example.
* D-083: drops at structures cost nothing beyond the structure depth. A maximum drop (to avoid drop manholes) is not
  yet a rule; use `[Invert limits]` at the structure where it matters.
