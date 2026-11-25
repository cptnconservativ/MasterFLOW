# Snowmelt and rain-on-snow

Daily snowpack accounting that gives the water available for runoff (rain plus melt leaving the pack) for spring
snowmelt and rain-on-snow design events. Use the peak day, or the whole series, as the rainfall input of a runoff model
such as the hydrograph routing tool. The tool is free and works in U.S. units (inches, °F, mph).

## Input

| Key | Meaning |
|---|---|
| `initial swe` | Snow water equivalent on the ground at the start, in. |
| `melt factor` | Degree-day factor Cm for days without rain, in/°F/day. Typical values are 0.03 to 0.10, rising through the melt season. |
| `base temperature` | Tb, °F (default 32). |
| `rain snow threshold` | Precipitation is snow at or below this temperature and rain above it (default 34 °F). |
| `forest cover` | Canopy, %. Up to 60 % uses the open or partly forested rain-on-snow equation; above 60 % the heavily forested one. |
| `wind`, `wind coefficient` | Wind speed 50 ft above the snow (mph) and the basin wind coefficient k (1.0 open plains, about 0.3 dense forest). Used on rain days. |
| `liquid capacity` | Liquid water the pack holds, as a % of its ice content, before water drains (default 3). |
| `[Days]` | Day, average air temperature (°F), precipitation (in). |

## Method

1. Precipitation is snow when Ta ≤ the threshold, otherwise rain.
2. On a day with rain and Ta above 32 °F, melt follows EM 1110-2-1406:
   - open or partly forested: M = (0.029 + 0.0084·k·v + 0.007·Pr)(Ta − 32) + 0.09
   - heavily forested: M = (0.074 + 0.007·Pr)(Ta − 32) + 0.05
3. On other days, M = Cm·(Ta − Tb) when Ta > Tb.
4. Melt is limited to the ice in the pack. Melt and rain fill the pack's liquid storage up to its capacity; the rest is water for runoff. When the pack is gone, everything runs off.

## Decisions

| ID | Decision |
|---|---|
| D-520 | Rain days use the EM 1110-2-1406 rain-on-snow equations (from Snow Hydrology, 1956), which assume saturated air near the given temperature; dry days use the degree-day method because it needs only temperature. |
| D-521 | Cold content, refreezing and the antecedent temperature index are not modeled, so melt starts on the first warm day. For a pack that is well below freezing this overstates early melt (conservative for runoff). |
| D-522 | The forested equation applies above 60 % canopy, the split in EM 1110-2-1406. |
| D-523 | The rain/snow threshold defaults to 34 °F and the liquid capacity to 3 %; both are inputs because practice varies. |
| D-524 | Validation: hand calculations of both rain-on-snow equations, degree-day melt, liquid storage and the period water balance (SnowmeltTests). EM 1110-2-1406 was not reachable from the build environment. |
