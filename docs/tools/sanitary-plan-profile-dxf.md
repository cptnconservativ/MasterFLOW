# Sanitary sewer plan and profile sheets (DXF)

Tool id `sanitary-plan-profile-dxf` (★ Premium; U.S. units). Runs the sanitary sewer design (`sanitary-network`) and
draws it as plan and profile sheets in an R12 DXF file that AutoCAD and Civil 3D open, with the same sheet layout as
the storm sheets (`plan-profile-dxf`). The profile data, the flags and the full sanitary design tables are reported as
well, so the drawing can be checked against the computation form.

## Input

The sheet keys, then the sanitary sewer design input (all of its keys and the `[Manholes]`, `[Loads]` and `[Pipes]`
tables, or the selected network):

| Key | Meaning |
|---|---|
| `runs` | `longest` (default), `all` (every upstream manhole), or manhole names separated by commas. |
| `horizontal scale`, `vertical scale` | ft per inch (default 40 and 4, a vertical exaggeration of 10). |
| `sheet` | Paper size `W x H` in inches (default 34 x 22). |
| `plan` | `yes`/`no`: plan view at the manholes' Northing/Easting (from a LandXML network). |
| `title` | Title block text. |
| `dxf file` | Optional path to write the file as well; otherwise **Save…** and pick `.dxf`. |

## Drawing

- **Plan** at true coordinates: sewers labelled with name, size, length, slope, peak flow and d/D; manholes with name
  and rim; any flags written beside the sewer (layer `MF-SAN-PLAN-FLAG`).
- **Profiles** stationed from 0+00 at the outlet, increasing upstream, split onto sheets at the scales:
  rim line, inverts and crowns, manholes from the lowest invert to the rim (labels: rim, invert in, invert out,
  depth), and the **peak-flow water surface** (invert + Manning normal depth at the design peak flow; a surcharged
  sewer is drawn full). Each sewer is labelled with size, length, slope, n, Qpeak, V at peak and d/D.
- **Flags**, circled on the profile (`MF-SAN-PROF-FLAG`) and listed in the "Flags on the sheets" table with their
  sheet number:
  - `DESIGN EXCEPTION: V x < 2.0 FPS AT PEAK (NEEDS S = y %)`, the sewers in the design's Design exceptions table
    (IDCM 3.12.6, D-706);
  - d/D over the limit, or surcharged at peak;
  - cover below the minimum, Ten States minimum slope, maximum velocity, manhole spacing, slope over 20 %, larger pipe
    upstream, n below 0.013, minimum size;
  - manholes: drop below the required drop, drop manhole needed, outlet crown above an inlet crown.
- **Layers** `MF-SAN-PLAN-PIPE`, `MF-SAN-PLAN-MH`, `MF-SAN-PLAN-TEXT`, `MF-SAN-PLAN-FLAG`, `MF-SAN-PROF-GRID(-TEXT)`,
  `MF-SAN-PROF-GROUND`, `MF-SAN-PROF-PIPE`, `MF-SAN-PROF-MH`, `MF-SAN-PROF-WS`, `MF-SAN-PROF-TEXT`, `MF-SAN-PROF-FLAG`,
  `MF-SHEET`.

## Decisions

- **D-730** The sheets run the sanitary design from the same input instead of reading a solved storm network, so the
  drawing always shows the current design (designed sizes and inverts included). The water surface is the peak-flow
  normal depth the sanitary design checks d/D against; there is no backwater or HGL in a gravity sanitary design.
- **D-731** Every warning the design raises for a drawn sewer or manhole is flagged on the sheets. A sewer below
  2 ft/s at peak is labelled "design exception" with the slope it would need, because the design leaves it at its
  slope and lists it for a written exception (D-706).
- **D-732** Sanitary layers are prefixed `MF-SAN-`, so storm and sanitary sheets can be inserted into one drawing and
  frozen separately. The sheet layout, R12 format, stationing and scales are those of the storm sheets (D-135 to D-137).

## Messages

| Code | Meaning |
|---|---|
| E-SP-001, 002 | Scales or sheet size unreadable |
| E-SP-003 | Run start is not a manhole with an outlet sewer |
| E-SP-004 | Nothing to draw |
| E-SP-005 | Could not write the `dxf file` |
| W-SP-001 | Manholes without coordinates left out of the plan |
| W-SP-002 | Profile taller than the sheet |
| W-SP-003 | Flags on the drawn runs |
| I-SP-001 … 003 | No plan view, file written, plotting note |

The sanitary design's own messages (E-SS, W-SS, I-SS) are passed through.

## Validation

- The profile table's water surface at each end equals the design invert plus the peak normal depth, and its d/D
  equals the design's; stations follow the lengths (0+00, 3+04, 5+08).
- A 300-person load on 8 in. at 0.4 % is flagged as a design exception on the sheet and in the table; 9,000 people on
  the same sewer are flagged for d/D.
- The DXF declares AC1009, ends with EOF, has group-code/value pairs, a SEQEND for every POLYLINE, and ASCII only.
- A 508 ft run at 1" = 10' on 11 x 8.5 sheets splits onto 7 sheets (80 ft each).
- The sample drawing was rendered to an image and checked.
