# Bridge scour (HEC-18): contraction, pier and abutment

Tool id `bridge-scour` (free). Computes scour at a bridge for one flood by FHWA HEC-18, *Evaluating Scour at Bridges*
(5th ed., 2012). Run it once for the design flood (100-yr or overtopping) and once for the check flood (500-yr). The
hydraulics come from the natural channel profile tool (`river-profile`) or a HEC-RAS run.

## Input

Keys (lengths in ft, flows in cfs; m and m³/s in SI projects; D50 always in mm):

| Key | Meaning |
|---|---|
| `D50` | Median bed-material size, mm |
| `y1`, `V1` | Approach main-channel depth and velocity |
| `Q1`, `W1` | Approach main-channel flow and width |
| `Q2`, `W2` | Flow through the bridge main channel (with overbank flow forced in) and its width less piers |
| `y0` | Existing depth in the bridge section before scour |
| `S1` | Energy slope at the approach (live-bed k1); optional |
| `bed elevation` | Bridge-section bed, to report scoured elevations; optional |

Contraction scour is computed when `Q2` or `W2` is given; then `Q1`, `W1`, `Q2`, `W2` and `y0` are all required.

`[Piers]`: Name, Shape (`square`, `round`, `circular`, `group`, `sharp`), Width a, Length L, Skew (deg), Depth y1 and
Velocity V1 just upstream of the pier, Bed (`clear-water`, `plane`, `small dunes`, `medium dunes`, `large dunes`).

`[Abutments]`: Name, Type (`vertical wall`, `vertical wall with wing walls`, `spill-through`), L' (length of active
flow blocked by the embankment), ya (average depth of the blocked overbank flow), Ve = Qe/Ae, Skew (deg, 90 =
perpendicular to the flow).

## Method

**Live-bed or clear-water (Eq. 6.1).** Vc = Ku·y1^(1/6)·D50^(1/3), Ku = 11.17 (U.S.) or 6.19 (SI). V1 > Vc is live-bed.

**Live-bed contraction (modified Laursen, Eq. 6.2).** y2/y1 = (Q2/Q1)^(6/7)·(W1/W2)^k1. k1 = 0.59, 0.64 or 0.69 for
V*/T below 0.5, 0.5 to 2.0, and above 2.0, where V* = √(g·y1·S1) and T is the fall velocity of D50.

**Clear-water contraction (Eq. 6.4).** y2 = [Ku·Q2²/(Dm^(2/3)·W2²)]^(3/7), Dm = 1.25·D50, Ku = 0.0077 (U.S.) or 0.025
(SI). Contraction scour is ys = y2 − y0, and never below zero.

**Pier scour (CSU, Eq. 7.1).** ys/a = 2.0·K1·K2·K3·(y1/a)^0.35·Fr1^0.43.
- K1 from Table 7.1: square 1.1, round, circular or group 1.0, sharp 0.9. K1 = 1.0 when the skew exceeds 5°.
- K2 = (cos θ + (L/a)·sin θ)^0.65, with L/a no more than 12.
- K3 from Table 7.3: 1.1 for clear-water, plane bed and small dunes, 1.2 medium dunes, 1.3 large dunes. Clear-water
  conditions always use 1.1.
- Wide piers in shallow flow (y/a < 0.8, a/D50 > 50, Fr < 1) take Kw (Eq. 7.4): 2.58·(y/a)^0.34·Fr^0.65 for V/Vc < 1
  and (y/a)^0.13·Fr^0.25 otherwise.
- Round-nose piers aligned with the flow are limited to 2.4·a (Fr ≤ 0.8) or 3.0·a (Fr > 0.8).

Total pier scour is local plus contraction scour. The scoured bed is the bed elevation less the total.

**Abutment scour.**
- Froehlich (Eq. 8.1): ys/ya = 2.27·K1·K2·(L'/ya)^0.43·Fr^0.61 + 1, Fr = Ve/√(g·ya).
- HIRE (Eq. 8.2) for L'/ya > 25: ys/ya = 4·Fr^0.33·(K1/0.55)·K2.
- K1 = 1.00 vertical wall, 0.82 with wing walls, 0.55 spill-through. K2 = (θ/90)^0.13.

## Decisions

- **D-350** Fall velocity for k1 uses Rubey (1933) for quartz at 20 °C. It sits slightly below the HEC-18 chart for
  coarse sand, which gives a larger V*/T and a k1 the same or one step higher (more scour). Without S1 the tool takes
  k1 = 0.64 and warns.
- **D-351** The NCHRP 24-20 abutment method, also in HEC-18 (2012), is not included. It reads its amplification factors
  from charts that could not be obtained as text here. Froehlich (with its +1 safety term) and HIRE are given, and
  HEC-23 riprap is the usual countermeasure.
- **D-352** Not included: coarse-bed pier scour (D50 ≥ 20 mm with the Sheppard-Melville option), pressure-flow
  (vertical contraction) scour, debris rafts and long-term degradation. Model a debris raft as a wider effective pier.
- **D-353** One flood per run. HEC-18 asks for both the design and check floods; the tool reminds the user (I-BS-002).

## Messages

| Code | Meaning |
|---|---|
| E-BS-001 | D50 missing or not above zero |
| E-BS-002 | y1 or V1 missing |
| E-BS-003 | Contraction scour started but a value is missing |
| E-BS-004/005 | Unknown pier shape or bed condition |
| E-BS-006/007 | Pier width, depth or velocity missing; skew out of 0–90° |
| E-BS-008/009/010 | Unknown abutment type; L', ya or Ve missing; skew out of range |
| W-BS-001 | No energy slope: k1 = 0.64 assumed |
| W-BS-002 | Clear-water scour with D50 below 0.2 mm |
| I-BS-001 | y2 below the existing depth: no contraction scour |
| I-BS-002 | Run for the design and check floods |
