# Culverts for aquatic organism passage (HEC-26)

Tool id `aop-culvert` (free). Checks an embedded culvert designed by the stream simulation approach of FHWA HEC-26,
*Culvert Design for Aquatic Organism Passage* (2010): a barrel wide enough for a natural bed, that bed stable at the
high design flow, hydraulics similar to the natural reach, and enough depth and slow enough water at the passage flows.
Headwater at the road design flows is still checked with the HDS-5 culvert tool (`hds5-culvert`).

## Input

| Key | Meaning |
|---|---|
| `shape` | `box` or `pipe` (circular) |
| `span`, `rise` | Box span and rise, or the pipe diameter (`span`) |
| `embedment` | Depth of bed material in the barrel |
| `wall n` | Manning n of the walls (default 0.012 box, 0.024 pipe) |
| `slope`, `stream slope` | Barrel slope and the reach slope |
| `bankfull width` | Reach bankfull width |
| `stream bottom width`, `stream side slope` | Trapezoid for the natural channel comparison |
| `D50`, `D84`, `D95` | Bed material sizes, mm |
| `Shields`, `safety factor` | F* (default 0.047) and SF (default 1.0) |
| `min depth`, `max velocity` | Species criteria at the low and high passage flows |

`[Flows]`: Name, Q, Use (`stability`, `high passage`, `low passage`).

## Method

- Bed width: box span, or the chord of the pipe at the embedment depth. Checked against bankfull width (≥ 1.0).
- Pipe embedment 30–50 % of the diameter; bed material at least 2·D95 deep; barrel slope within 25 % of the reach.
- Normal depth in the barrel above the bed, by Manning with a composite n (Horton): n = [(Pb·nb^1.5 + Pw·nw^1.5)/P]^(2/3).
- Bed n by Limerinos (1970): n = 0.0926·R^(1/6)/(1.16 + 2·log10(R/D84)) with R and D84 in ft (0.1129 with m), on the bed
  partition hydraulic radius Rb = (V·nb/(k·√S))^1.5 (Einstein, equal velocity), iterated to agreement.
- Bed shear τb = γ·Rb·S; critical shear τc = F*·(γs − γ)·D (Shields, γs = 2.65·γ). The stable size is SF·τb/(F*·(γs − γ)).
  At a `stability` flow HEC-26 asks for D84 to be stable; D50 may move.
- The natural channel: normal depth in the trapezoid with a Limerinos n, and the ratio of barrel bed shear to reach shear.
- `low passage` flows: depth ≥ `min depth`. `high passage` flows: average velocity ≤ `max velocity`.

## Decisions

- **D-420** The bed n uses Limerinos with D84, the common choice for gravel and cobble beds; HEC-26 lists it with other
  grain-roughness relations. Where R/D84 is so small that Limerinos fails (denominator ≤ 0.1), n = 0.07 is used.
- **D-421** Stability uses Shields with F* = 0.047 for every size, without a hiding factor, so coarse fractions in a
  mixture are judged a little conservatively. F* and SF are inputs.
- **D-422** The 2·D95 bed depth, 30–50 % pipe embedment and ±25 % slope match are HEC-26 guidance; the warning says
  where a state rule (for example 1.2·W + 2 ft) may be stricter.
- **D-423** Normal depth only; inlet and outlet losses and headwater are left to the HDS-5 tool. A flow above the
  barrel's open-channel capacity is reported as flowing full.
- **D-424** HEC-26's worked examples were not available as text in this environment. The tests check Limerinos, the
  embedded pipe geometry, Manning with the composite n and the shear by hand.

## Messages

| Code | Meaning |
|---|---|
| E-AO-001 … 009 | Unknown shape; span, rise, embedment, slope, bed sizes, wall n or flows missing or invalid |
| E-AO-010 | No flow area above the bed |
| W-AO-001 | Bed width less than bankfull |
| W-AO-002 | Pipe embedment outside 30–50 % |
| W-AO-003 | Bed less than 2·D95 deep |
| W-AO-004 | Barrel slope more than 25 % from the reach |
| W-AO-005 | Flow exceeds open-channel capacity |
| W-AO-006 | D84 mobile at a stability flow |
| W-AO-007/008 | Low-flow depth or high-flow velocity outside the species criteria |
| W-AO-009 | Barrel bed shear more than 1.25 × the reach |
| I-AO-001 | No bankfull width |
