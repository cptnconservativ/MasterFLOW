# Sanitary bypass pumping plan for tie-ins

Tool id `sanitary-bypass` (Free; U.S. units). Plans the bypass pumping for a tie-in or repair on a sanitary sewer: how
much flow arrives hour by hour, when to do the work, what pump and line to rent, how long to pump, and how long the
sewer upstream can hold the flow if the pumps stop.

With `connection = <pipe>` the flow, land uses, inverts and rims come from the project's **Sanitary sewer network
design** (`sanitary-network`, saved with the project, or its tables pasted into this input). The bypass draws from the
connection's upstream manhole and discharges into its downstream manhole (or `discharge to`). Without a design, give
`average flow`, `pattern`, `static head` and `suction lift`.

## Input

| Key | Default | Meaning |
|---|---|---|
| `connection`, `discharge to` | — | Pipe to be plugged; receiving manhole (default: the pipe's downstream manhole) |
| `average flow` | design Qave + infiltration | gpm; an entered value replaces the design flow |
| `pattern` | blend of the loads upstream | 24 multipliers, `residential`, `commercial` or `flat` (default patterns in sanitary-wet-weather.md, D-761) |
| `wet weather factor`, `safety factor` | 1.0, 1.0 | On the flow and on the design flow |
| `work start`, `work duration`, `setup time` | 22:00, 6 h, 1 h | Plug time, plugged hours, pumping before and after (each) |
| `design flow` | `daily peak` | `daily peak` (highest hour of the day), `window peak` (highest hour while pumping), `design peak` (IDCM Qpeak + I/I of the pipe), or gpm |
| `standby pumps`, `pump capacity`, `pump efficiency` | 1, blank, 0.60 | Blank capacity = one duty pump for the design flow |
| `discharge length`, `suction length` | connection + 20 ft, 25 ft | ft |
| `line sizes`, `suction diameter`, `max line velocity` | 4–18 in., line size, 8 ft/s | Smallest size at or under the velocity is chosen |
| `C`, `fittings K`, `riser height` | 150, 5, 3 ft | Hazen-Williams C; discharge-side K; rise above the receiving rim |
| `site elevation`, `water temperature`, `NPSH required` | suction rim, 60 °F, blank | NPSH check |
| `response time`, `backup elevation` | 30 min, blank | Time to restore pumping; lowest service or basement upstream |

## Method

**Flow at the connection.** Q(h) = (Qave_sanitary × p(h) + infiltration) × wet weather factor, p the hourly multiplier
by clock hour. The default pattern is the flow-weighted blend of the default land-use patterns of the loads upstream.

**Pumping period** = work duration + 2 × setup time, from work start − setup. Volume = ∫ Q dt (1-min steps); highest
flow = the highest quarter-hour sample. **Lowest-flow window**: every quarter-hour start over the day, ranked by the
highest flow while pumping, then by volume.

**Pumps.** Duty pumps = ⌈design flow / pump capacity⌉ (one when blank) plus `standby pumps` of the same size.
Water hp = Q·TDH/3960; brake hp = whp / efficiency; drive from the standard motor sizes of the lift station tool.

**Line and head.** Static head = receiving rim + riser − pump-down level, where the pump-down level is the lowest
invert at the suction manhole. TDH = static + Hazen-Williams friction 10.44·L·Q^1.852/(C^1.852·D^4.8655) + K·V²/2g on
the discharge side + suction friction with an entrance K of 1.0.

**Suction and NPSH.** Suction lift = suction manhole rim (pump on the surface) − pump-down level. NPSHa = atmospheric head
33.96·(1 − 6.8754×10⁻⁶·z)^5.2559 ft − lift − suction losses − vapor head (Antoine equation). At Rapid City's 3,200 to
3,400 ft the atmospheric head is about 30 ft, against 34 ft at sea level.

**Storage if the bypass stops.** For each level (the incoming crown at the suction manhole, a backup elevation, and
the lowest rim upstream), storage = Σ over the pipes upstream of the suction manhole of the volume between the
normal-depth water surface of the pumping-period average flow and the level (200 slices per pipe), plus the manholes
(including the suction manhole) from their inverts. Time = storage / flow, at the highest pumping flow and at the
design flow. W-BP-004 when the time to overflow is shorter than the response time.

## Check by hand

100 gpm flat for 4 h plus 1 h setup each side: 36,000 gal. 6 in. line, 500 ft, C 150: hf = 10.44·500·100^1.852 /
(150^1.852·6^4.8655) = 0.40 ft; TDH = 10 + 0.40 + 0.02 = 10.42 ft (test `FlatFlow_TdhVolumeAndPumps_ByHand`). With the
residential pattern a 4 h window is lowest from 02:00 to 06:00 (highest multiplier 0.45). A level 1 ft pipe 10 ft long
stores π/4 × 10 = 7.85 ft³ from empty to the crown.

## Decisions

- **D-765** The default pump design flow is the highest hour of the day (`daily peak`), not the highest hour of the
  work window, so a tie-in that overruns into the morning peak is still covered. `window peak` sizes for the planned
  window only; `design peak` uses the IDCM design peak for a bypass that runs for days or through wet weather.
- **D-766** One standby pump of the duty size is the default (100 % standby). The pump-down level is the lowest invert
  at the suction manhole and the pumps sit at its rim.
- **D-767** The line is sized by a maximum velocity (default 8 ft/s, adjustable) rather than a minimum: bypass lines run
  at the varying diurnal flow and cannot hold 2 ft/s at the night low. A low average-flow velocity is noted (I-BP-002).
  C = 150 (HDPE) and a discharge K of 5 are starting values, not published requirements.
- **D-768** The 30 min response time is a MasterFlow default for the crew to start a standby pump or clear a clog; use
  the contract's or the utility's requirement.
- **D-769** Storage counts only the pipes and manholes upstream of the suction manhole, from the normal-depth water
  surface of the pumping-period average flow, with each pipe carrying its share of the connection flow by its upstream
  average flow. Service laterals and building drains are not counted (conservative).

## Messages

| Code | Meaning |
|---|---|
| E-BP-001 | SI project |
| E-BP-002 | Connection given but no sanitary design, or the pasted design has errors |
| E-BP-003 | Unknown connection or discharge manhole |
| E-BP-004 | No connection and no average flow, or flow ≤ 0 |
| E-BP-005 | Unreadable pattern |
| E-BP-006 | Work start, duration, setup, or factors below 1 |
| E-BP-007 | Unreadable design flow, or design peak without a connection |
| E-BP-008 | Pump, line or temperature input invalid |
| E-BP-009 | Lift unknown (rims missing, or no static head and suction lift without a design) |
| W-BP-001 | No listed line size under the velocity limit |
| W-BP-002 | NPSH available below the pump's requirement, or under 3 ft |
| W-BP-003 | No standby pump |
| W-BP-004 | The sewer overflows before the response time if the pumps stop |
| I-BP-001 … 006 | Sea-level atmosphere, low line velocity, one duty pump, design flow below the IDCM peak, a lower-flow window, early surcharge |
