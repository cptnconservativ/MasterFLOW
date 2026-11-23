# Utility conflict finder (★ Premium)

Tool id `utility-conflicts` (license feature of the same name). Checks every utility line against every other in 3D
before the plans go out, and against the separation rules.

## Input

- The project's **storm networks** come in automatically (`include storm networks = yes`): each pipe from its
  structures' Northing/Easting and its inverts, outside diameter = rise × (1 + `storm wall`, default 0.2 for RCP walls).
  Structures are checked as cylinders of `structure diameter` (default 4 ft) from the rim to 0.5 ft below the lowest
  invert. Pipes whose structures have no coordinates are left out (W-UC-001).
- **Other utilities** under `[Utilities]`: Name, Type, Size (outside diameter, in.), Northing, Easting, Elev, Ref
  (`invert`, `center` or `top`; default invert). One vertex per row; consecutive rows with the same Name form a line.
  Types: water, sanitary, storm, gas, electric, telecom (any other name is allowed; `*` in the rules matches it).
- **Rules** under `[Rules]`: Type A, Type B, Horizontal, Vertical (edge to edge, ft), Water above (yes: the water main
  must cross above the other line), Reference. The first matching row wins. Without a `[Rules]` section the defaults
  below are used.

| A | B | Horizontal | Vertical | Water above | Source |
|---|---|---|---|---|---|
| water | sanitary | 10 ft | 1.5 ft | yes | Ten States Standards §38.31–38.32 |
| water | storm | 10 ft | 1.5 ft | | Ten States Standards §38.3 (verify locally) |
| water | gas / electric / telecom | 5 / 5 / 3 ft | 1.0 ft | | typical utility practice |
| sanitary | storm | 3 ft | 0.5 ft | | typical |
| gas / electric / telecom | storm | 3 / 3 / 2 ft | 1.0 ft | | typical; verify with the owner |
| * | * | 2 ft | 0.5 ft | | general minimum |

The defaults are typical values, not the City's: confirm them with the City of Rapid City and each utility owner.

## Checks

| Kind | Meaning |
|---|---|
| CLASH | The pipes overlap where they cross (vertical clearance below zero) |
| THROUGH STRUCTURE | A line passes through a storm manhole or inlet |
| WATER BELOW | A water main crosses under a sewer where the rule requires it above |
| VERTICAL | Crossing clearance less than the rule |
| STRUCTURE | A line passes closer than 1 ft (or the rule, if less) to a structure wall within its depth |
| HORIZONTAL | Lines that do not cross run closer in plan than the rule; the parallel length is given. For water over sewer by at least the vertical rule, the Ten States separate-trench allowance is noted |

Each finding gives the location (northing, easting), the rule, the actual distance, the shortfall and a fix (how far
to lower or raise which line, or move apart). A plan chart shows the lines, the storm structures and the findings.

## Decisions

- D-270: lines are straight segments between entered vertices at centerline elevation, and clearances are edge to edge
  from outside diameters, which is how separation rules are written; bends and fittings are not modelled.
- D-271: the storm network is read from the project rather than entered again, so a redesign is re-checked by running
  the tool again.
- D-272: the rule table is input, with typical defaults and their sources shown in the report, because separation rules
  vary by city, utility owner and state; the tool never hides which rule produced a finding.
