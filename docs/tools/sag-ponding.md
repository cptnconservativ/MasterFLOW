# Sag inlet ponding and clogging

Tool id `sag-ponding`. Free. For one sag (low point on a vertical curve), it finds the ponded depth, spread and
ponded length for the 10-yr and 100-yr flows in three cases: inlets clean, inlets clogged per IDCM Table 4-11, and
the low-point inlet fully clogged. Flanking inlets and an overland overflow path relieve the low point. U.S. units
only (the IDCM limits and the HEC-22 coefficients are in ft and cfs).

## Criteria (Rapid City IDCM 2012)

| Item | Requirement | Source |
|---|---|---|
| Clogging | Grate 40 %, combination 33 %, curb opening 0 % | Table 4-11 |
| 10-yr spread | Local: to the crown; collector 12 ft dry; 3-lane arterial 24 ft dry; > 3 lanes 36 ft dry; no curb overtopping | Table 4-6 |
| 100-yr depth | 18 in. (local), 12 in. (collector, arterial) over the flow line; within the right-of-way | Table 4-9 |
| Flanking inlets | Required on arterials (three inlets at a sag); placed where the gutter is about 0.2 ft above the low point | §4.5.4(2) |
| Flanking distance | X = (200 · d · K)^0.5, K = L / A | Eq. 4-5-2, Table 4-12 |
| Approach gradient | 0.3 % within 50 ft of the low point (AASHTO) | §4.5.4(2) |

Table 4-12's header says inches, but its values are reproduced by X = (200 · d · K)^0.5 with d in **feet**
(K = 50, d = 0.3 gives 55 ft; K = 167, d = 0.8 gives 163 ft). The tool uses feet.

## Method

1. **Low-point inlet capacity** uses the same HEC-22 sag equations as the inlet network tool: curb weir
   (depressed Cw = 2.3 on L + 1.8W, undepressed 3.0 L), orifice and transition; grate weir on L + 2W and orifice
   on the clear area (the lesser); combination = grate in weir flow, grate + curb orifice in orifice flow.
2. **Clogging** reduces the grate length, width and open area, and the curb opening length, by the clog fraction
   (Table 4-11 by default, or `clog =` in %).
3. **Flanking inlets** (one each side) take flow once the pond rises above `flanking depth`, as sag inlets on the
   depth over them. They are placed at X from the low point.
4. **Overflow** above `overflow depth` (the low point of the right-of-way) is a broad-crested weir,
   Q = 3.0 · L · (d − d_overflow)^1.5.
5. The depth that passes Q through all three is found by bisection. Spread follows from the gutter section; above
   the crown the whole street is ponded.
6. The ponded length is 2X at the ponded depth (symmetric curve assumed).

Checks apply to the clean and the Table 4-11 cases. The fully clogged case is a resilience check and is reported as
REVIEW, not FAILS. Output: ponding table, sag curve table, and a rating chart of flow against depth for each case.

## Validation

* Table 4-12: X matches 55 ft (K 50, d 0.3), 163 ft (K 167, d 0.8) and 28 ft (K 20, d 0.2).
* Undepressed curb, L = 10 ft, Q = 5 cfs: d = (5 / 30)^(2/3) = 0.303 ft, T = 15.1 ft on 2 %. The tool agrees.
* Grate 3 × 2 ft, Q = 2 cfs, clean and 40 % clogged: weir depth by hand plus Sx · W/2 to the curb, within 0.001 ft.
* Fully clogged low point with flanks and overflow: flanking plus overflow flow equals the inflow.

Decisions:

* D-145: free. It is a check of IDCM criteria, like the street capacity tool.
* D-146: clogging and the grate-depth offset follow the inlet network tool, so that both tools give the same
  answer for the same inlet. The grate depth is taken at the middle of the unclogged width.
* D-147: the fully clogged case does not fail the design. The IDCM requires flanking inlets so that a clogged low
  point still drains; the tool shows how deep it ponds and where the water goes, for the engineer to judge.
