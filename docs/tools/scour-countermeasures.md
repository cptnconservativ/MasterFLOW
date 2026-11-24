# Bridge scour countermeasures (HEC-23): pier and abutment riprap

Tool id `scour-countermeasures` (free). Sizes riprap at bridge piers and abutments by FHWA HEC-23, *Bridge Scour and
Stream Instability Countermeasures* (3rd ed., 2009), Design Guidelines 11 and 14. Use it with the bridge scour tool
(`bridge-scour`, HEC-18): the scour depths set how deep the riprap toe and filter must go. Bank revetment between the
abutments is sized with the riprap revetment tool (`riprap-revetment`, HEC-11; HEC-23 DG 12).

## Input

`Ss` (default 2.65).

`[Piers]`: Name, Shape (`round` K = 1.5, `rectangular` K = 1.7), Width a, Local V, Average V, Multiplier. Give the local
velocity upstream of the pier, or the average velocity Q/A and a multiplier from 0.9 (pier near the bank, straight
reach) to 1.7 (pier in the main current around a bend). With no multiplier, 1.7 is used and a warning is given.

`[Abutments]`: Name, Type (`spill-through` or `vertical wall`), Depth y at the abutment toe, Velocity V, or Q total and A
total of the bridge opening, SBR, Qf and Af of the set-back overbank.

## Method

**Piers (DG 11).** D50 = 0.692·(K·V)²/((Ss − 1)·2g). Extent at least 2a from the pier face all round; thickness at least
3·D50; top at the streambed; filter under the riprap but stopping short of its edge.

**Abutments (DG 14).**
- Fr = V/√(g·y) ≤ 0.8: D50/y = K/(Ss − 1)·(V²/(g·y)), K = 0.89 spill-through, 1.02 vertical wall.
- Fr > 0.8: D50/y = K/(Ss − 1)·(V²/(g·y))^0.14, K = 0.61 spill-through, 0.69 vertical wall.
- Characteristic velocity by the set-back ratio: SBR < 5 uses Q/A of the whole contracted opening; SBR ≥ 5 uses Qf/Af
  of the set-back overbank.
- Thickness max(1.5·D50, D100), D100 ≈ 2·D50; toe apron 2y into the waterway, not more than 25 ft (7.6 m).

The result for each element is the required D50, the smallest standard class (Class I 6 in. to Class X 42 in., HEC-23
DG 4) at or above it, the thickness and the extent.

## Check

Pier, round nose, V = 8.5 ft/s, Ss = 2.65: D50 = 0.692·(1.5·8.5)²/(1.65·64.4) = 1.059 ft = 12.7 in. → Class IV (15 in.),
3.75 ft thick, 8 ft out from each face of a 4 ft pier. Vertical-wall abutment, y = 6.5 ft, V = 6.2 ft/s (Fr 0.43):
D50 = 6.5·1.02/1.65·(38.44/209.3) = 0.738 ft = 8.9 in. → Class II. The test suite reproduces these.

## Decisions

- **D-410** A pier with only an average velocity and no multiplier uses 1.7, HEC-23's upper value, and warns.
- **D-411** D100 ≈ 2·D50 for the abutment thickness (the upper end of the DG 4 gradations), so the layer is never
  thinner than HEC-23 asks.
- **D-412** Guide banks (DG 15), spurs, partially grouted riprap and articulating concrete blocks are not sized here.
  Their HEC-23 design guidelines rely on charts and product test data; the tool points to them when riprap exceeds
  Class X.
- **D-413** HEC-23's worked examples were not available as text in this environment; the equations are checked by hand
  in the tests.

## Messages

| Code | Meaning |
|---|---|
| E-SC-001 | Ss not above 1 |
| E-SC-002/003/004 | Unknown pier shape; width missing; no velocity |
| E-SC-005/006/007/008 | Unknown abutment type; depth missing; SBR ≥ 5 without Qf, Af; no velocity |
| E-SC-009 | No piers or abutments |
| W-SC-001/002 | No multiplier (1.7 used); multiplier outside 0.9–1.7 |
| W-SC-003 | D50 above Class X |
| I-SC-001 | No SBR: Q/A used |
| I-SC-002 | Design flood reminder |
