# Sanitary lift station and force main

Tool id `sanitary-lift-station` (free, U.S. units). Checks a wastewater lift station and its force main and produces
the hydraulic items Rapid City IDCM §3.13.3 item 2 asks the design report to contain: number and capacity of pumps,
horsepower, the system head curve with the force main size, length and C, and detention times in the wet well and
force main. Limits follow the Ten States Standards, *Recommended Standards for Wastewater Facilities* (2014), Ch. 40.
Stormwater pump stations use `pump-station` (HEC-24) instead.

## Input

Keys: `average flow`, `peak flow`, `minimum flow` (gpm; minimum defaults to the average), `pumps` (identical, default
2), `wet well diameter` (ft) or `wet well area` (ft²), `wet well bottom`, `pump off`, `lead on`, `lag on`, `high
alarm`, `inlet invert`, `discharge elevation`, `force main length` (ft), `force main diameter` (in., inside), `hazen
C` (120), `minor K`, `efficiency` (0.65), `min cycle time` (10 min), `wave speed` (1,300 ft/s), `pressure class`
(psi), `min velocity` (2 ft/s), `max velocity` (8 ft/s), `max wet well detention` (30 min), `max force main
detention` (120 min).

Tables: `[Pump curve]` Q (gpm) and head (ft) for one pump; `[Force main profile]` station and elevation (optional).

## Method

- System head: TDH = (top − wet well level) + 10.44·L·Q^1.852/(C^1.852·D^4.8655) + K·V²/2g, with Q gpm and D in.
  The top is the discharge elevation, or the force main's highest profile point when that is higher (no siphon
  credit; W-LS-006). Curves are drawn at the pump-off level (greatest static head) and the lag-on (or lead-on) level.
- Operating points: n identical pumps in parallel on one force main; each pump's head at Q/n equals the system head
  at Q (bisection). Firm capacity = all pumps but one at the pump-off level, compared with the peak flow.
- Brake horsepower = Q·H/(3960·η) for one pump at pump off; the next standard motor size is listed.
- Wet well: operating volume between pump off and lead on; the shortest cycle is 4V/Q, at an inflow of half the
  pump rate, and the volume for the minimum cycle time is V = T·Q/4. Cycles at the average and minimum flow are
  V/Qin + V/(Q − Qin).
- Detention at the minimum flow: wet well volume from the bottom (or pump off when no bottom is given) to lead on, and
  the force main volume, each divided by the flow.
- Surge: Joukowsky ΔH = a·V/g for an instant stop of the running pumps (an upper bound), plus the greatest static
  head, against the pipe pressure class.
- High points in the profile are listed for air release / vacuum valves.
- Charts: pump curves (1 to n pumps) with both system curves, and the force main profile with the one-pump HGL.

## Decisions

- **D-711** Identical pumps on one force main, as in the HEC-24 tool (D-460).
- **D-712** The 30 min wet well and 120 min force main detention limits are MasterFlow defaults (common practice;
  neither the IDCM nor Ten States sets a number). The IDCM asks only that the times be reported and odor addressed.
- **D-713** Ten States clauses are cited by chapter (Ch. 40, pumping stations and force mains) because the section
  numbers could not be checked against the 2014 text in this environment.
- **D-714** NPSH, wet well geometry (submergence, vortices), standby power and transient analysis beyond Joukowsky
  are left to the pump supplier and the design report.

## Messages

| Code | Meaning |
|---|---|
| E-LS-001 | SI project |
| E-LS-002 | A required value is missing |
| E-LS-003 | Pump curve missing or not falling with Q |
| E-LS-004 | Profile row invalid |
| E-LS-005 | Flows, pumps or force main values out of range |
| E-LS-006 | Control levels out of order |
| E-LS-007 | No wet well size |
| W-LS-001 | Firm capacity below the peak flow |
| W-LS-002 | Force main velocity below 2 ft/s with one pump |
| W-LS-003 | Force main velocity above the maximum with all pumps |
| W-LS-004 | Cycle shorter than the minimum (the extra lead-on height is given) |
| W-LS-005 | Wet well detention too long |
| W-LS-006 | Force main high point above the discharge |
| W-LS-007 | Force main detention too long |
| W-LS-008 | Shut-off head below the static head |
| W-LS-009 | Static + surge above the pressure class |
| W-LS-010 | Force main under 4 in. |
| W-LS-011 | Pump-on or alarm level at or above the inlet invert |
| W-LS-012 | Operating point off the end of the pump curve |
| I-LS-001 … 003 | Air valves at high points; IDCM report items; limits in use |
