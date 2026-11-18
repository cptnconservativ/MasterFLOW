# Civil 3D round trip: HGL results back to the drawing

MasterFlow reads a Civil 3D pipe network from LandXML, solves the HGL/EGL, and offers two ways back:

| Route | What Civil 3D gets | Use it when |
|---|---|---|
| **Civil 3D results…** → `name HGL.lsp` + `name HGL.csv` | Command **MFHGL** writes HGL up/down, EGL up/down and flow rate onto the *existing* pipes, matched by network and pipe name. The CSV has every pipe and structure result. | You want labels, pipe tables and profile-view HGL lines on the design network itself. |
| **LandXML…** → `name.xml` | A new pipe network (Insert → LandXML) with the schema's PipeFlow/StructFlow results and a "MasterFlow hydraulics" Feature on every part. | You want a network built from MasterFlow (for example after *Size pipes*), or an archive copy of the analysis in an open format. |

Values are in the network's units (ft, cfs, ft/s or m, m³/s, m/s). Nothing is converted, so the drawing must
use the same units as the MasterFlow network.

## What the files contain

**PipeFlow** (one per pipe, LandXML 1.2): `flowIn` = design Q, `hglUp`, `hglDown`, `depthCritical`.

**StructFlow** (one per structure): `hglIn` = HGL in the structure, `hglOut` = HGL entering the outlet pipe
(structure HGL less the structure loss), `lossIn` = structure head loss (0 at an outfall), `lossOut` = 0. The
schema does not define these two loss attributes further, so this use is stated in the element's `desc`.

**Feature "MasterFlow hydraulics"** on every pipe: Q, Q full, Q/Q full, Manning n, V up, V down, V full,
depth up/down, normal and critical depth, HGL and EGL up/down, friction loss, regime, condition, flows full,
minor storm Q and V, and *Check*. On every structure: HGL, EGL, HGL leaving, structure loss, loss method,
rim − HGL, rim − EGL, outfall, and *Check*. *Check* is `OK` or the codes of the criteria warnings that name
that part (for example `W-NW-001` HGL too close to the rim, `W-NW-003` velocity above the maximum,
`W-NW-005` pipe under pressure). A network-level Feature records the units, criteria source, tailwater rule
and warning count.

**MFHGL** tries, for each value, the COM property name first and then the .NET API name
(`HGLUp`/`HydraulicGradeLineUp`, `HGLDown`/`HydraulicGradeLineDown`, `EGLUp`/`EnergyGradeLineUp`,
`EGLDown`/`EnergyGradeLineDown`, `FlowRate`) and prints which ones it set.

## What is tested here

- The LandXML export validates against the published LandXML-1.2.xsd (U.S. and SI, solved and unsolved,
  circular and elliptical pipes). The test also shows the validator rejects the old `EllipPipe` tag and a
  structure with no Center.
- PipeFlow, StructFlow, Feature, the LISP data and the CSV hold the solver's numbers (checked to 1e-5).
- The LISP file has balanced parentheses, escapes quotes and backslashes in names, and writes numbers with a
  decimal point even when Windows uses a decimal comma.
- A network that does not solve writes geometry only (LandXML) or nothing (LISP/CSV), and says why.

## To verify in Civil 3D (not yet done: no Civil 3D in the build environment)

1. **MFHGL sets the properties.** Import a network from LandXML into MasterFlow, solve, *Civil 3D results…*.
   In the drawing: `APPLOAD` the .lsp, run `MFHGL`. Expect "n of n pipes updated" and "Properties set: …".
   Open a pipe's *Pipe Properties → Part Properties → Hydraulic Properties* and compare HGL/EGL/flow with the
   CSV. If it says "Could not set", run the command it prints (`vlax-dump-object …`) on a pipe and send the
   list: the property names then need changing in `Civil3DWriteBack.PipeProperties`.
2. **Flow units.** Check that *Flow Rate* reads the cfs value from the CSV (Civil 3D may hold flow in its own
   internal unit through the API).
3. **Network match.** The network name in MasterFlow must equal the Civil 3D network name (it does after an
   import unless renamed). Rename it in MasterFlow to see the "Match pipe names in every network?" prompt.
4. **Undo.** `U` once should restore the previous HGL/EGL values.
5. **Labels and profile view.** A pipe label with *Hydraulic Grade Line Up/Down* and the profile view's HGL
   display should show the new values (regenerate if needed).
6. **Insert → LandXML** of the MasterFlow export: confirm it imports without errors, the parts list maps
   circular and elliptical sizes, structures land at the right coordinates and elevations, and whether Civil 3D
   keeps the PipeFlow HGL (look at the imported pipes' hydraulic properties) or drops it.
