# Plan and profile sheets (DXF)

Tool id `plan-profile-dxf` (**premium**). It draws the selected, solved storm network as plan and profile sheets in a
DXF file that AutoCAD and Civil 3D open. The HGL and EGL come from the network solution. The same profile data is
reported as tables and figures, so the drawing can be checked against the numbers.

## Input

| Key | Meaning |
|---|---|
| `runs` | `longest` (default: the longest path to the outfall), `all` (every beginning-of-line structure), or structure names separated by commas. |
| `horizontal scale`, `vertical scale` | ft per inch (U.S.; default 40 and 4, a vertical exaggeration of 10) or 1:n (SI; default 500 and 50). |
| `sheet` | Paper size `W x H`, in inches (default 34 x 22, ANSI D) or mm (841 x 594, A1). |
| `plan` | `yes`/`no`: draw the plan view at the structures' Northing/Easting. |
| `title` | Title block text. |
| `dxf file` | Optional path to write the file as well; otherwise use **Save…** and pick `.dxf`. |

## Drawing

- **Model space at true size.** The plan sits at the structure coordinates, so it overlays the Civil 3D drawing. The
  profile sheets are stacked below the plan. Each sheet border is drawn at the paper size times the horizontal scale.
  Plot the borders at that scale.
- **Plan view.**
  - Pipes are drawn as lines, labelled with name, size, length, slope and Q.
  - Structures are drawn as circles, labelled with name and rim.
- **Profile layout.**
  - Stations run from 0+00 at the outfall and increase upstream.
  - A grid of stations and elevations, labelled on both sides.
  - Long runs continue onto further sheets.
- **Profile content.**
  - Rim/ground line through the structures.
  - Pipe inverts and crowns.
  - Each structure is a box from its lowest invert to the rim, labelled with rim, inverts in and out, HGL and EGL.
  - The HGL (solid) and EGL (dashed) follow the standard-step profile inside each pipe. Ticks at each structure show
    the rise across it.
  - Structures whose HGL is closer to the rim than the network criterion (IDCM §4.6.7: 1 ft) are circled.
- **Title block.** Network, run, station range, scales and sheet number.
- **Layers.** `MF-PLAN-PIPE`, `MF-PLAN-STRUCT`, `MF-PLAN-TEXT`, `MF-PROF-GRID(-TEXT)`, `MF-PROF-GROUND`, `MF-PROF-PIPE`,
  `MF-PROF-STRUCT`, `MF-PROF-HGL`, `MF-PROF-EGL` (DASHED), `MF-PROF-TEXT`, `MF-PROF-FLAG`, `MF-SHEET`.

## Decisions

- **D-135** The output is R12 ASCII DXF (AC1009) in model space. R12 needs no handles or object dictionaries, so it is
  simple to write correctly, and every CAD program reads it. Paper-space layouts with viewports would tie the file to
  a newer DXF version. They can be set up in Civil 3D, where most offices already have sheet templates.
- **D-136** Plan views are not cut into rotated plan sheets. The plan is drawn once at true coordinates so it overlays
  the base drawing, and Civil 3D's Plan Production can create the plan viewports. The profile sheets are complete.
- **D-137** Profile stations start at the outfall and increase upstream. Pipes are drawn between structure centres.
  The HGL and EGL inside each pipe are the solver's own profile points, so the drawing shows exactly what was computed.

## Validation

- **Profile tables match the solution.** On a test network, the station, rim, invert, HGL and EGL in the profile
  tables equal the network solution values. Stations (0+00, 2+00, 3+50) follow the pipe lengths.
- **The DXF is well formed.**
  - Group codes come in code/value pairs.
  - It declares AC1009 and ends with `EOF`.
  - Every POLYLINE has a SEQEND.
  - It is ASCII only, and the layers and DASHED linetype are present.
- **Sheet splitting.** A 350 ft run at 1" = 10' on 11 x 8.5 sheets splits onto 5 sheets: 80 ft of profile per sheet.
- **Clearance flag.** A high tailwater raises the HGL within 1 ft of the rim, and the structure is flagged on the
  sheet and in the table.
- **Visual check.** The drawing was rendered to an image and checked.
