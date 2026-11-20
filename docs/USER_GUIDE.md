# MasterFlow user guide

MasterFlow 0.10 · Windows desktop · steady, uniform open-channel flow by Manning's equation.

MasterFlow is a calculator for engineers. It gives a fast, traceable answer for one cross-section at
uniform flow. It does not model gradually varied, unsteady or pressurised flow, and it does not choose
Manning's n for you. The responsible engineer reviews the inputs, assumptions and results before use.

---

## 1. Getting started

1. Unzip `MasterFlow-x.y.z-win-x64.zip` and run `MasterFlow.exe`. It needs no installation, no
   administrator rights and no internet connection. It sends nothing anywhere.
2. MasterFlow opens with a sample project. Click a calculation in the list on the left to see it.
3. Change any input. The results update as you type. There is no Calculate button.
4. **File → Save** (Ctrl+S) writes a `.mfproj` project file.

The window has three parts:

| Area | What it holds |
|---|---|
| Left: project | Project information (shown on reports), the list of calculations, template and recycle-bin controls |
| Middle: calculation | Title, folder, description, section, flow inputs, cross-section drawing |
| Right: tabs | **Results**, **Compare** (all ticked calculations side by side), **Rating table** |

## 2. A calculation

### 2.1 Section shapes

| Shape | Inputs | Notes |
|---|---|---|
| Rectangular | Bottom width b | |
| Trapezoidal | Bottom width b, left and right side slopes zL, zR (H:1V) | |
| Triangular | Left and right side slopes zL, zR (H:1V) | Use for V-ditches and gutters |
| Circular | Diameter (in or mm) | Partly full flow; reports full-flow and maximum capacity |
| Elliptical | ASTM C507 standard size (HE or VE), or a custom rise and span | Standard sizes use the C507 four-centred shape; custom is a true ellipse |
| Irregular | Station/elevation points, optional n per point | Surveyed or LiDAR sections, streets, compound channels |

**Channel depth** (open shapes) is optional. When the water is above it, warning W-G-001 says the
channel overtops; the result is still calculated as if the walls continued.

### 2.2 Units

Choose **U.S. customary** (ft, cfs; pipe sizes in inches) or **SI** (m, m³/s; pipe sizes in mm) per
calculation. MasterFlow never converts between them behind your back: the engine computes in the units
you enter (k = 1.486 and g = 32.174 ft/s² for U.S.; k = 1 and g = 9.80665 m/s² for SI). Changing the
unit system does not convert the numbers already entered.

### 2.3 Solve for

Pick the unknown in **Solve for**. Its field turns grey and shows the solved value; the others are inputs.

| Solve for | You enter |
|---|---|
| Normal depth yn | n, slope, discharge |
| Discharge Q | n, slope, depth |
| Slope S | n, discharge, depth |
| Manning's n | slope, discharge, depth |

Slope is entered in **percent** (0.5 % = 0.005 ft/ft).

**Manning's n.** The **n reference** list offers typical values from Chow (1959), Table 5-6 (minimum,
normal, maximum). Picking one fills in the normal value. It is a starting point only.

**Invert elevation** (optional). Enter it to have water-surface, energy-grade and critical water-surface
elevations reported. Irregular sections always report elevations from their own points.

### 2.4 Irregular sections

Type or paste points into the box, one point per line:

```
station, elevation [, n [, description]]
```

- Separators: tab (a paste from Excel), semicolon, comma or spaces.
- A first line that is not numeric is treated as a header and skipped. Lines starting with `#` are ignored.
- Stations may use surveyor notation: `1+25.50` means 125.50.
- **n on a point applies to the segment from that point to the next one.** A blank n uses the
  calculation's Manning's n. The last point's n is not used.
- Every line that cannot be read is listed with its line number; nothing is guessed.

**Composite n** (when n varies across the section):

- *Conveyance subdivision* (default; the HEC-RAS approach): the area is split at each change in n, the
  conveyance of each part is summed, and the report shows the flow, area and velocity of each part.
- *Equal velocity* (Horton–Einstein): one composite n for the whole wetted perimeter.
- *Per-segment conveyance (Flow Calc)*: every segment between two points is its own part, whether or
  not n changes. Use it to reproduce Flow Calc's "Flow Conveyance" results; it gives more conveyance in
  wide, shallow overbanks (the sample creek: yn 6.694 ft against 6.893 ft by HEC-RAS subdivision).

The method in use is printed under the results and in every report, and so is the elliptical pipe
shape (ASTM C507 four-centred for standard sizes, or a true ellipse when "standard size" is off; the
two differ by about 3% in normal depth).

What MasterFlow does with awkward sections, and tells you about (warnings are repeated in a yellow box at the top of the Results tab):

| Situation | What happens | Message |
|---|---|---|
| Water above an end point | "Overtopped" warning with the amount above each end; a vertical wall is added there (counted as wetted perimeter). Results show **Top of section** and **Freeboard (OVERTOPPED)** as a negative number | W-I-003 |
| Water above a point labelled as a bank ("Left bank", "Top of bank", "LB", "RB", "TOB"; not "overbank") | "Out of bank" warning naming the bank and the depth above it | W-I-005 |
| Separate pools below the water surface | All of them carry flow | W-I-002 |
| Several depths carry the same Q (flat overbanks) | All are listed; the **highest** is reported | W-I-001 |
| Several specific-energy minima | All are listed; the one with the lowest energy is critical depth | W-I-004 |
| Solve for n with n on points | Not possible (n would be ambiguous) | E-N-002 |

Up to 5000 points are accepted; a 5000-point section solves in well under a second.

**Point tools** (the buttons under the points box). Each is one undo step.

| Button | What it does |
|---|---|
| Import… | Reads points from a text or CSV file (same rules as pasting; also `|` and `:` separators) |
| Export… | Saves the points as CSV: Station, Elevation, n, Description |
| Sort | Orders points by station; each segment's n follows the ground it was on |
| Mirror | Swaps left and right (stations become first + last − station) |
| Zero sta. / Zero elev. | Moves the first station to 0, or the lowest point to elevation 0 |
| Shift sta. / Shift elev. | Adds the amount in the box (in the length unit) to every station or elevation |

**To points.** On a rectangular, trapezoidal or triangular channel, **To points** replaces the shape by the
same section as station/elevation points, so you can edit it like a surveyed section. The height is the
channel depth if you set one, otherwise 1.5 × the larger of yn and yc, rounded up (the status bar says
which). The normal depth does not change. Pipes cannot be converted.

**Import LandXML…** (project pane) reads cross-sections exported from Civil 3D sample lines or any
other program that writes LandXML. Each section and surface becomes a new irregular calculation, titled
like `Ditch A 12+34.50 (EG)` in a folder named after the alignment. Values are used exactly as they are
in the file. The unit system comes from the file; if the file does not say (or uses something other than
feet or metres) the status bar tells you and you must check it. n is set to 0.035: set n, slope and
discharge for each section. Anything the file contains that cannot be read is listed, not guessed.

The same button reads **pipe networks** (export the storm network to LandXML from Civil 3D):

- Each circular or elliptical pipe becomes a pipe calculation titled `P-1 (CB-1 → MH-1)` in the folder
  `<network>/Pipes`, with the pipe name as item number. The slope comes from the start and end inverts
  and the length; the invert elevation is the upstream invert. Depth is set to the full rise and the
  calculation solves for Q, so it shows the **full-flow capacity**. Enter the design Q and switch to
  *Solve for normal depth* to check it. n is set to 0.013: change it for the material.
- Inlets, manholes and outlets appear on the **Structures** tab with northing/easting, rim and sump
  elevations and pipe inverts, and in a *Structures* sheet in the Excel workbook. Importing the same
  network again updates them.
- **Alignments** in the file (or a separate alignment-only LandXML export) station the structures: each
  structure with northing/easting gets the nearest alignment's name, station (shown as `12+34.56`, or
  `1+234.567` in SI) and offset, positive to the right of stationing. They appear on the Structures tab,
  in the network tables of the PDF/HTML report and in the Excel sheets. Lines, curves and clothoid
  spirals are read; gaps between elements and unsupported elements are reported. Alignments themselves
  are not kept: import again after the road changes.
- Reported, not fixed: missing inverts (no slope), flat or adverse slopes, other pipe shapes (box, egg),
  and sizes that look like inches or millimetres in a feet or metre file.
- Sample: `docs/samples/storm-network.xml`.

### 2.5 Pipes

- **Circular**: enter the diameter in inches (U.S.) or mm (SI). Above about 94 % full, the same Q can
  occur at two depths; the lower one is reported and the upper one is listed (W-P-001). Discharge above
  the maximum free-surface capacity is an error (E-P-001), never a made-up depth.
- **Elliptical**: tick *ASTM C507 standard size* and pick a size (tick *Vertical (VE)* for vertical
  ellipses), or untick it and enter rise and span for a true ellipse.
- Pipe drawings are always to true scale.

### 2.6 Title, folder and description

- **Item no.** (beside Folder) is a free text item or drawing number. It shows in the list and the report
  header, and as `{item}` in titles.
- **Folder** organises the project list, for example `Ditches/Phase 1`. Use **Sort** to order the list by
  folder and then title (natural order: "Ditch 2" before "Ditch 10").
- **Title fields.** A title can contain live values in braces. They update as the inputs change and
  appear in the list, in reports and in Excel. The file keeps the template as typed.

  `{shape} — {D} in pipe, Q = {Q:F1} {Qunit}` → `Circular — 36 in pipe, Q = 71.4 cfs`

  | Field | Meaning | Field | Meaning |
  |---|---|---|---|
  | `{shape}` | Section shape | `{units}` | U.S. or SI |
  | `{folder}` | Folder path | `{Q}` `{Qunit}` | Discharge and its unit |
  | `{yn}` `{yc}` `{unit}` | Normal and critical depth, length unit | `{S}` | Slope in % |
  | `{n}` | Manning's n | `{V}` `{Fr}` `{regime}` | Velocity, Froude number, regime |
  | `{A}` `{T}` | Flow area, top width | `{b}` `{zL}` `{zR}` | Bottom width, side slopes |
  | `{D}` `{rise}` `{span}` | Pipe sizes in in/mm | `{WSE}` | Water-surface elevation |
  | `{pctfull}` | Conduit percent full | `{item}` | Item number |

  Add a .NET number format after a colon: `{Q:F1}`, `{yn:F3}`, `{Q:N0}`. Type `{{` or `}}` for a literal
  brace. A misspelt field shows as `{?name}` so it cannot go unnoticed; result fields show "—" when there
  is no valid result. Hover over the title box for the field list.

## 3. Results

The **Results** tab lists every quantity with its symbol and unit; values in bold were solved for.
Groups: governing values, normal flow (area, wetted perimeter, hydraulic radius, top width, velocity,
velocity head, specific energy, Froude number, conveyance, section factor, V·y, mean and maximum
boundary shear), critical flow at the same discharge, elevations (when known), and for irregular
sections the composite n, energy coefficient α and number of wetted areas.

**Messages** appear under the results with a code. E codes are errors (no result is shown), W codes are
warnings, I codes are information. The full list with thresholds is in
[HYDRAULIC_METHODS.md](HYDRAULIC_METHODS.md). Examples: W-N-001 (n outside 0.008–0.20), W-S-001 (slope
above 10 %), W-F-001 (near-critical flow, 0.86 ≤ Fr ≤ 1.13).

**Copy results** puts the results on the clipboard as tab-separated text for Excel.

If a calculation cannot be solved, MasterFlow shows the error and **no numbers**. It never reports a
value from a solver that did not converge.

### 3.1 Design checks

Open **Design checks** under the inputs and enter any of: a lining description, the permissible shear τp
for that lining (from HEC-15 or your agency's manual), a required freeboard, and minimum or maximum
velocity. Blank limits are skipped. The **Results** tab then lists each check with the computed value, the
limit and **OK**, **FAILS** or **Not evaluated** (hover for the basis). Failures also appear as warnings
(W-C-001 to W-C-004), and the checks are printed in the report and the Excel sheet.

- Shear uses τd = γ·yn·S, the shear at the deepest point of a straight channel (HEC-15 eq. 2.3). Bends are
  not included.
- Freeboard needs a channel depth on regular shapes; on irregular sections it uses the lower end point.
- Pipes: shear and freeboard are not evaluated; velocity limits are.

## 4. Rating tables

On the **Rating table** tab choose a mode (depth → discharge, or discharge → depth), start, end and
step, then **Generate**. Up to 5000 rows. **Copy** puts the table on the clipboard; **CSV…** saves it.
The rating curve is drawn above the table. Generating a table saves its range with the calculation, so
the table is rebuilt from the current inputs in that calculation's report, the batch report and its own
sheet in the Excel workbook, whichever calculation is selected. **Remove** stops including it.

## 4a. Sensitivity to n and slope

The **Sensitivity** tab re-solves the selected calculation with Manning's n multiplied by 0.8, 0.9, 1.1
and 1.2 and with the slope multiplied by 0.5, 0.75, 1.25 and 1.5, keeping the same unknown. Each row
shows yn, Q, V, Fr, the regime and the change in the unknown from the base case. The unknown itself is
not varied (solving for slope varies only n). Per-point n values of an irregular section are scaled
too. A case that does not solve (for example a pipe that can no longer carry Q) is listed as failed with
the reason. Tick **Include in report and Excel** to add the table to this calculation's report and its
workbook sheet; the setting is saved with the calculation. **Copy** puts it on the clipboard for Excel.

Check by hand: at a fixed depth Q is proportional to √S / n, so n + 10 % gives Q − 9.1 % and slope
− 50 % gives Q − 29.3 %.

## 5. Comparing alternatives

Every calculation in the list has a tick box. Ticked, solved calculations appear side by side on the
**Compare** tab, in the **Batch report**, the **Compare report** and the **Excel** workbook. Untick a
calculation to leave it out. The tick is saved with the project.

Calculations in different unit systems are not converted; the comparison says so.

## 6. Reports and exports

| Button | Output |
|---|---|
| **Report…** (Ctrl+E) | The selected calculation |
| **Batch report…** | Every ticked calculation, each starting on a new page |
| **Compare report…** | The side-by-side comparison, landscape |
| **Excel…** | Workbook: comparison sheet, one sheet per ticked calculation, rating table |

Reports are saved as **PDF** by default. Choose *HTML report* in the save dialog's file type list for an
HTML file instead (open it in a browser to print). Reports contain the project information, method,
inputs, cross-section, results, warnings and assumptions, the rating table when generated, notes and
(optionally) the full calculation trace.

**Report settings** live in *Project information* and are saved with the project:

- **Watermark** (for example PRELIMINARY) and **footer text**.
- **Logo…** embeds a company logo in the project file (PNG, JPEG, GIF or SVG, up to 1 MB). PDF reports
  show PNG, JPEG and GIF logos; an SVG logo appears in HTML reports only.
- **Include calculation trace in reports**: the solver's step-by-step record (brackets, iterations,
  residuals). Leave it on for review copies.

Numbers in Excel are stored at full precision; non-finite values are left empty rather than written
as text.

## 6a. Storm networks (HGL and EGL)

**Storm networks…** opens a separate window for storm sewer hydraulic grade lines.

1. **New** adds an empty network, or **Import LandXML** in the main window sets one up from a Civil 3D
   pipe network (pipes, sizes, lengths, inverts, structures and rims). Imported pipes have no design Q:
   give each inlet its inflow (see 3), or type a Q per pipe.
2. Pipes: one per line, columns *Pipe, From (upstream structure), To (downstream structure), Shape,
   Size or rise (in. or mm), Span (elliptical only), Length, Invert up, Invert down, n, Q*. Leave Q blank
   to have it added up from the structure inflows (a pipe carries the inflow at its upstream structure plus
   every pipe entering that structure; a typed Q overrides this and is carried on downstream). Accumulated
   values are marked Σ in the tables. An optional last column, *Q minor*, is the check-storm flow (the
   2-year storm in Rapid City) used only for the minimum-velocity check; leave it blank to add it up
   from the structures' minor inflows. Tab, semicolon
   or comma separated, so a range copied from Excel can be pasted straight in.
3. Structures: *Structure, Type, Rim, Loss method, K, Inflow Q, Minor inflow Q*. Name the minor storm
   in *Minor storm* (the Rapid City preset sets "2-yr"). Leave the loss method blank for *Auto*; the list
   of methods is under the table (IDCM Figs. 4-6/4-7 cases, bends, or StandardK with K).
4. Tailwater rule and, for a known tailwater, its elevation.
5. Criteria: type values or press **Rapid City IDCM 2012** (HGL 1 ft below the rim, 2.5 to 20 fps,
   12 in. minimum, slopes below critical, no surcharge).
6. **Solve** (F5). The status line says whether the HGL criterion is met everywhere.

**Size pipes** picks circular sizes for you: the smallest standard size (12 to 108 in., or 300 to 3000 mm)
that carries the design Q part full (when pressure flow is not allowed) within the velocity limits and is
never smaller than a pipe upstream; then, wherever a structure's HGL is too close to its rim or a pipe still
flows full, it tries one size up on each pipe between that structure and the outfall and keeps the one that
lowers the water level most, repeating until the criteria are met or nothing more helps. Inverts, n, lengths
and elliptical pipes are kept, and no crown is put above a rim. Every change, and anything it could not fix
(for example an inlet too shallow for the 1 ft rule), is listed on the **Sizing** tab; **Undo sizing**
restores the previous sizes.

**LandXML…** writes the network as a LandXML 1.2 pipe network that validates against the published
LandXML-1.2 schema: units stated (foot or meter, cfs or m³/s, ft/s or m/s), sizes, lengths, slopes, Manning n,
inverts at each structure, rims, sumps at the lowest invert, coordinates and structure types. When the
network solves, each pipe gets the schema's *PipeFlow* (Q, HGL up/down, critical depth) and each structure
its *StructFlow* (HGL in the structure, HGL leaving it, structure loss), and every part gets a "MasterFlow
hydraulics" *Feature* listing EGL, velocities, depths, Q full, Q/Q full, regime, friction loss and *Check*
(OK, or the criteria warning codes that part fails). In Civil 3D use *Insert → LandXML* and pick a parts list
that has the sizes; Civil 3D creates a new pipe network from it. Structures need northing/easting (they come
with a LandXML import); the export warns about any that do not.

**Civil 3D results…** puts the results on the network you already have in the drawing instead of a copy.
It saves two files: *name HGL.lsp* and *name HGL.csv*. In Civil 3D, open the drawing, run *APPLOAD* on the
.lsp, and type **MFHGL**. Every pipe in the network of the same name whose pipe name matches gets its HGL up,
HGL down, EGL up, EGL down and flow rate, so pipe labels, profile-view HGL lines and pipe tables show
MasterFlow's numbers. If no network has that name it asks before matching pipe names in every network. The
command lists the pipes it changed, any it could not find and any property it could not set, and the whole
run is one undo (*U*). Civil 3D structures have no HGL property, so structure results (HGL, EGL, loss, rim −
HGL, check) are in the CSV, which also has every pipe result, one row per part, for Excel or a Civil 3D data
link table. Values are written in the network's units; the drawing must use the same units. See
[CIVIL3D_ROUNDTRIP.md](CIVIL3D_ROUNDTRIP.md) for what has and has not yet been checked in Civil 3D.

Results: **Profile** (choose the path; HGL solid, EGL dashed, rim/ground dashed brown, the criterion line
as an orange tick at each structure; an HGL label in red fails the criterion; under the graph a band
lists the station, HGL, EGL and the Rapid City limit (rim/ground − 1 ft) every 50 ft, or 15 m in SI, with
the HGL in red wherever it is above the limit), **Pipes**, **Structures**
(failing rows shaded red), **Messages** and **Trace**. **Report…** saves a PDF (or HTML) with a profile per
path and both tables; **Copy tables** puts them on the clipboard; **Excel…** adds sheets for the pipes,
structures, the 50 ft readings (with limit − HGL and OK/FAILS), every profile point (station, invert, crown, HGL, EGL) and messages. Networks are saved with
the project.

## 6b. Design tools

**Design tools…** opens drainage design tools that share one window: pick a tool, edit its text input
(**Sample** fills in an example), press **Run** (F5), then copy, save as HTML/Excel, or **Apply to network**
where the tool offers it. Inputs are saved with the project. See [docs/tools](tools/README.md) for each tool's
method and sources. The first tool is the Rapid City rational method, which can fill in the storm network's
structure inflows for the design and minor storms.

**Culvert analysis (FHWA HDS-5)** gives the headwater of a culvert for a design flow (Rapid City: 10-yr) and a
check flow (100-yr): inlet control by the HDS-5 equations for the chosen inlet (`inlet =` one of the built-in
HDS-5 rows, or `custom` with your own K, M, c, Y), outlet control, which one governs, flow over the roadway
(give the crest under `[Roadway]`), outlet velocity, and a performance curve. The HTML report draws the curve;
the hand-check table lists every intermediate value (Ku·Q/AD^0.5, dc, Hc, V²/2g, H, ho) so a reviewer can redo
the numbers. `criteria = rapid-city` adds the IDCM §4.7.2 checks. The detention tool's outlet pipe uses the same
engine. See [docs/tools/culverts.md](tools/culverts.md).

## 7. Projects, templates and safety nets

| Task | How |
|---|---|
| New calculation | **+ Channel** (Ctrl+T) |
| From a template | **From template ▾**: built-in generic starting points (street half-section, full street, V-ditch, grass swale, concrete trapezoid, flume, RCP, CMP, HE pipe, natural channel) and your own. Dimensions are generic; edit them to the governing standard detail |
| Save your own template | Select a calculation, **Save as template**. Templates are kept for your Windows user, available in every project. Remove one with ✕ in the template list |
| Duplicate | **Duplicate** (Ctrl+D) |
| Delete | **Delete** moves the calculation to the **recycle bin** (saved with the project). **Restore** brings it back; **Empty recycle bin** removes it for good |
| Undo / redo | Ctrl+Z / Ctrl+Y, per calculation |
| Recent projects | **Recent ▾** |

**Autosave and recovery.** Every minute, unsaved work is copied to a recovery file in your user folder
(never over your own project file). If MasterFlow closes unexpectedly, it offers to restore that copy
the next time it starts with three choices: **Restore** opens it, **Keep for later** saves it as a
project in `%APPDATA%\MasterFlow\recovered` (closing the prompt does the same), and only **Discard**
deletes it. Saving or closing normally removes the recovery copy.

**One window.** MasterFlow runs as a single window. Starting it again, or double-clicking a project
while it is open, brings the open window forward and opens that project there (after asking about
unsaved changes). A recovery copy that belongs to a MasterFlow window that is still open is never
offered, overwritten or deleted.

**Unexpected errors.** If something goes wrong inside MasterFlow, it keeps running where it can,
saves a recovery copy and says so in the status bar.

**Log folder.** MasterFlow keeps a plain-text log of what it did (saves, exports, errors) on your
computer only, one file per day, for 14 days. Use **Log folder** to open it, for example to send a file
with a problem report.

### Where MasterFlow keeps things

| What | Where |
|---|---|
| Projects | Wherever you save them (`.mfproj`, readable JSON with a schema version) |
| Recent files, user templates, recovery copy | `%APPDATA%\MasterFlow` |
| Log | `%APPDATA%\MasterFlow\logs` |

Project files from newer MasterFlow versions with a newer schema are refused rather than misread.

## 8. Keyboard shortcuts

| Keys | Action |
|---|---|
| Ctrl+N | New project |
| Ctrl+O | Open project |
| Ctrl+S / Ctrl+Shift+S | Save / Save as |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+T | New calculation |
| Ctrl+D | Duplicate calculation |
| Ctrl+E | Report of the selected calculation |
| Tab / Shift+Tab | Next / previous field |

## 9. Method in brief

Q = (k/n)·A·R^(2/3)·S^(1/2), R = A/P. Critical depth satisfies Q²T/(gA³) = 1; Fr = V/√(gD), D = A/T.
Unknown depths are found with Brent's bracketed method (no Newton iterations), with a relative depth
tolerance of 1×10⁻¹² and a check that the discharge residual is at most 1×10⁻⁸. Results are checked
against independent hand calculations and published examples; see
[HYDRAULIC_METHODS.md](HYDRAULIC_METHODS.md) and [VALIDATION_RESULTS.md](VALIDATION_RESULTS.md).

Assumptions: prismatic channel, small slope (depth measured vertically), hydrostatic pressure, uniform
roughness unless n is given per segment, velocity head with α = 1.
