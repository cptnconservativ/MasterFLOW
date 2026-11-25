# Slope-area peak discharge from high-water marks

Tool id `slope-area` (free). Estimates the peak discharge of a flood that has already happened, from surveyed
cross-sections and the high-water marks it left. Used after a flood to check a gage, calibrate a model or document a
damaging event, by the slope-area method of Dalrymple and Benson (1967), USGS TWRI Book 3 Chapter A2.

## Input

One block per cross-section, upstream to downstream (two or more; three or more preferred):

```
[XS 1]
ws = 105.30            # high-water elevation (average of both banks)
length = 200           # reach length to the next section downstream
n = 0.045, 0.035, 0.050  # Manning n per subsection, left to right
banks = 30, 70         # stations that split the subsections
Station  Elevation
...
```

## Method

- Each section is cut at the bank stations; area A, wetted perimeter and conveyance K = (k/n)·A·R^(2/3) per subsection
  (k = 1.486 U.S., 1.0 SI), summed. α = Σ(Ki³/Ai²)/(Kt³/At²).
- Reach between sections u and d: Q = √(Ku·Kd)·√[(Δh + (1 − c)·Δhv)/L], Δh = fall of the water surface,
  Δhv = αuVu²/2g − αdVd²/2g, c = 0.5 when the reach expands (Δhv > 0) and 0 when it contracts. Solved directly,
  since every velocity head is proportional to Q².
- All reaches together: Σ Q²·Li/(Ki·Ki+1) = total fall + Σ(1 − ci)·Δhv,i.
- Criteria flagged (TWRI 3-A2): fall at least 0.5 ft or the velocity head or a reach 75 mean depths long; Ku/Kd between
  0.8 and 1.25; expanding reaches avoided; Froude number under 1.

Hand check: identical 20 ft wide rectangular sections, 5 ft deep, n = 0.030, fall 0.2 ft in 200 ft:
K = (1.486/0.030)·100·(100/30)^(2/3) = 11,050; Q = 11,050·√0.001 = 349 cfs.

## Decisions

- D-680: Subsection conveyance is split only at the bank stations (vertical division lines); n changes within the channel
  are entered as more banks.
- D-681: Reach and all-section discharges are both reported; the all-section value is the result.

## Limits

The USGS computation of the "fall" from marks on both banks, rating the measurement (good/fair/poor) and the
slope-conveyance variant are left to the engineer.
