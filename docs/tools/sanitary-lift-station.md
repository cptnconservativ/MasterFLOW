# Sanitary lift station and force main

Tool id `sanitary-lift-station` (free, U.S. units). Checks a wastewater lift station and its force main and produces
the hydraulic items Rapid City IDCM §3.13.3 item 2 asks the design report to contain: number and capacity of pumps,
horsepower, the system head curve with the force main size, length and C, and detention times in the wet well and
force main. Limits follow the Ten States Standards, *Recommended Standards for Wastewater Facilities* (2014), Ch. 40.
Stormwater pump stations use `pump-station` (HEC-24) instead.

## Input

Keys: `average flow`, `peak flow`, `initial average flow` (gpm; the first-year average dry-weather flow, default = average; `minimum flow` is read as the same key), `pumps` (identical, default
2), `wet well diameter` (ft) or `wet well area` (ft²), `wet well bottom`, `pump off`, `lead on`, `lag on`, `high
alarm`, `inlet invert`, `discharge elevation`, `force main length` (ft), `force main diameter` (in., inside), `hazen
C` (120), `minor K`, `efficiency` (0.65), `min cycle time` (10 min), `wave speed` (1,300 ft/s), `pressure class`
(psi), `min velocity` (2 ft/s), `max velocity` (8 ft/s), `max wet well detention` (30 min), `force main
detention protect` (35 min), `force main detention control` (90 min), `max total detention` (180 min).

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
- Detention at the initial average flow (the longest detention, as the utilities below require): the wet well
  operating volume (pump off to lead on) divided by the flow, also shown at the design average flow and for the whole
  volume below lead on; the force main volume divided by the flow; and their sum.
- Surge: Joukowsky ΔH = a·V/g for an instant stop of the running pumps (an upper bound), plus the greatest static
  head, against the pipe pressure class.
- High points in the profile are listed for air release / vacuum valves.
- Charts: pump curves (1 to n pumps) with both system curves, and the force main profile with the one-pump HGL.

## Decisions

- **D-711** Identical pumps on one force main, as in the HEC-24 tool (D-460).
- **D-712** Detention limits (Kyle asked for industry-accepted values, 2026-11-26). Neither the IDCM nor the Ten
  States Standards gives a number; the IDCM asks for the times and for odor control to be addressed (§3.13.3 2d, 2e).
  Defaults, all at the initial (first-year) average dry-weather flow:
  - Wet well: 30 min. City of Fort Wayne *Lift Station and Force Main Design* SA8.07.1: "The maximum detention time in
    the wet well shall average no more than thirty (30) minutes", computed for initial and build-out average flow; 30
    min at average flow is also the common textbook value (Ludwigson, *Lift Station Design*, PDH course 401).
  - Force main: Clean Water Services (Oregon) *Design and Construction Standards* Ch. 9 §9.01.3.i, detention = force
    main volume / ADWF one year after start-up: under 35 min no H2S controls; 35 to 90 min protect the discharge
    manhole; over 90 min active H2S control. (Fort Wayne SA8.15.3 asks for H2S prevention only above 6 h, a looser
    limit.)
  - Wet well plus force main: 180 min (New Braunfels Utilities *Lift Station Design Criteria* §3.06.1, "the wet well
    retention time and force main flush time should not exceed 180 minutes").
  The wet well time uses the operating volume (V = Qavg·T, as the cycle-volume formula); the volume held below lead on
  is shown too.
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
| W-LS-007 | Force main detention 35–90 min (protect the discharge) or over 90 min (active H2S control) |
| W-LS-008 | Shut-off head below the static head |
| W-LS-009 | Static + surge above the pressure class |
| W-LS-010 | Force main under 4 in. |
| W-LS-011 | Pump-on or alarm level at or above the inlet invert |
| W-LS-012 | Operating point off the end of the pump curve |
| W-LS-013 | Wet well plus force main detention over the total limit |
| I-LS-001 … 003 | Air valves at high points; IDCM report items; limits in use |
