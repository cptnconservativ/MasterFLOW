# Channel and swale linings (HEC-15): riprap, grass, RECP/TRM

Tool id `channel-lining` (free, U.S. units). Designs flexible linings for roadside channels and swales by FHWA HEC-15,
*Design of Roadside Channels with Flexible Linings* (3rd ed., 2005), one row per reach. The channel profile tool
(`channel-profile`) keeps its IDCM Table 4-14 checks and simple HEC-15 shear check for a profile of reaches; this tool
gives the full HEC-15 lining design for one section at normal depth.

## Input

`[Reaches]`: Name, Q (cfs), S, B (bottom width, 0 for a V ditch), Z (H:1V), Rc (bend radius, blank for straight),
Lining (`riprap`, `grass`, `recp`), D50 (in., blank to size it), Cn, Cf, D75 (in.), tau soil (lb/ft²), tau p (lb/ft²),
n. Key `phi` (angle of repose for riprap, default 42°).

- Riprap: D50 to check, or blank to size.
- Grass: Cn (grass roughness coefficient, HEC-15 Tables 4.1 and 4.2, from stem height and density or retardance
  class), Cf (cover factor, Table 4.3), and the soil: D75 for non-cohesive soil or tau soil for cohesive soil (HEC-15
  Eq. 4.6 from plasticity index and void ratio).
- RECP/TRM: the product's tested permissible shear (tau p) and its n.

## Method

- Normal depth by Manning with the lining's n at that depth (iterated).
- τd = γ·d·S (maximum, at the deepest point). Side shear K1·τd: K1 = 0.77 (Z < 1.5), 0.066·Z + 0.67 (1.5–5), 1.0
  (Z ≥ 5). Bend shear Kb·τd: Kb = 2.0 (Rc/T ≤ 2), 2.38 − 0.206·(Rc/T) + 0.0073·(Rc/T)² (2–10), 1.05 (≥ 10); protection
  for Lp = 0.604·R^(7/6)/n downstream of the bend.
- Riprap (Ch. 6, S ≤ 10 %): D50 = SF·τ/(F*·(γs − γ)), γs = 165 lb/ft³; F* and SF from Re* = V*·D50/ν: 0.047 and 1.0
  for Re* ≤ 4·10⁴, 0.15 and 1.5 for Re* ≥ 2·10⁵, linear between. Side slope D50 = (K1/K2)·D50 with
  K2 = √(1 − sin²θ/sin²φ). n by Blodgett: 0.319·da^(1/6)/(2.25 + 5.23·log10(da/D50)). Sized by iteration (the depth
  depends on D50 through n).
- Grass (Ch. 4): n = 0.213·Cn·τo^−0.4, τo = γ·R·S; effective soil shear τe = τd·(1 − Cf)·(ns/n)² against the soil's
  permissible shear (0.4·D75 lb/ft² for non-cohesive D75 > 0.05 in., 0.02 lb/ft² for finer soil, or tau soil).
  ns = 0.016 for fine soil, 0.015·D75^(1/6) otherwise. The equivalent permissible shear on the lining is shown.
- RECP/TRM (Ch. 5): τd (with the bend factor) against the product's τp; the safety factor is reported.

## Decisions

- **D-450** Cn and Cf are inputs: the tool does not carry HEC-15's grass tables, so the engineer reads them for the
  species, height and cover expected. The sample uses Cn 0.142 and Cf 0.90 as illustrations.
- **D-451** Steep-slope riprap (S > 10 %, HEC-15 §6.3) and the Bathurst n for da/D50 < 1.5 are not computed; the tool
  warns (W-CL-001, W-CL-002).
- **D-452** In a bend the riprap is sized for Kb·τd and the side slope check uses that value too (conservative).
- **D-453** HEC-15's worked examples were not available as text in this environment; each equation is checked by
  hand in `ChannelLiningTests`.
- **D-454** The soil grain roughness for D75 > 0.05 in. uses ns = 0.015·D75^(1/6) (D75 in inches).

## Messages

| Code | Meaning |
|---|---|
| E-CL-001 | SI project |
| E-CL-002 … 007 | Missing geometry, side slope too steep, grass or RECP values missing, unknown lining, no reaches |
| W-CL-001/002 | Steep slope; da/D50 < 1.5 |
| W-CL-003 | No soil data for grass |
| W-CL-004 | Lining fails |
| W-CL-005 | Froude number 0.9–1.1 |
| I-CL-001/002 | Grass on steep slope; RECP soil check |
