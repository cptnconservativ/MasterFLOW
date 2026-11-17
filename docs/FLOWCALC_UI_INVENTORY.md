# Flow Calc — UI inventory (observed)

Observed 2026-11-17 through the normal user interface of the licensed installation on the user's PC
(window title "Flow Calc 2026", version 26.8.23.0). No binaries, files or memory were inspected.
Only the built-in **Example Project** was opened; nothing was saved.

## Access limitations (exact)
- Computer-use **clicks** worked; **typed text did not register** in Flow Calc's numeric fields
  (tried twice, with and without a prior click). New calculation cases could therefore not be entered.
  Values could only be changed through pickers (e.g. the n-value table).
- **Print Channel** opened a window owned by a process outside the approved app list, so the report
  preview / print dialog could not be seen. It may still be open on the user's second monitor.
- Not yet inspected: report preview, PDF export, batch printing run, Backup Settings, Import, Recent
  Files, About/info, settings gear, Change View buttons, rating-table analysis window, elliptical pipe,
  metric mode, context menus, keyboard shortcuts, high-DPI behaviour.

## Main window
| Element | Observation |
|---|---|
| Ribbon row 1 | Licensed indicator, Backup Settings, New File, Open, Import, Save, SaveAs, Recent Files, version label |
| Ribbon row 2 | Add Folder, Add Channel, Add Rating Table, Re-Sort, Example Project |
| Left pane | Project tree: Recycle Bin, project folder, channel items (page icon), rating-table items (table icon); Expand All / Collapse All |
| Item detail pane | Send to Recycle Bin, Duplicate To Root, Channel Analysis (or Rating Table Analysis), Toggle Channel Batch Printing (ON/OFF), Print Channel; fields Date Created, Item Number, Item Name (expression), rendered Item Name, 1st/2nd Sort Name, Channel Description |

## Channel Analysis window (one per item)
| Region | Observation |
|---|---|
| Header | Item Number, Item Name expression, rendered name |
| Channel Type / Base Units | Irregular · Circular · Elliptical · Imperial · Metric · Reset (gear) |
| Solve For | Checkbox chooses the unknown among N Value, Channel Slope (%), Normal Depth, Discharge; each value has an info icon and spinner; n has a picker |
| Nc composite method | Constant/Uniform or Flow Conveyance (disabled for pipes) |
| Status | "Channel Input OK" or "N Channel Errors" list |
| Irregular geometry grid | Columns: delete, Elev, Station (ft) shown as 0+00.00, Elevation (ft), N Value, Description; toolbar: Add Point, Copy/Paste/Import/Export, Delete, Clear, Stations, Elevations, Zero, Re-Sort; wizard button |
| Pipe geometry | Pipe Diameter (in), Span (in), Rise (in); note "*Nominal Pipe Conversion (25 mm / 1 inch)" |
| Plot | Cross-section with water fill, grid, axis in feet/inches, station labels of water edges |
| Output list | Each value has an info icon and a copy icon; Print Channel |
| Change View | Four layout buttons |

## Outputs listed (irregular channel)
N value, channel slope, normal depth, discharge, channel width, channel depth, water elevation/HGL,
flow area, wetted perimeter, top width, hydraulic depth, hydraulic radius, critical depth,
critical slope, velocity, velocity head, specific energy, velocity-depth product, Froude number.

## Additional outputs (circular pipe)
Pipe diameter, dual normal depth, pipe area, percent full, full flow at channel slope, full-flow slope
for the discharge, maximum pipe discharge, depth for maximum pipe discharge.

## Dialogs
- **Irregular Channel Wizard**: "Standard Channels" (rectangular: n, depth, bottom width;
  trapezoidal: + left/right side slope H:V; triangular V: n, depth, side slopes) with "Apply With
  Channel Name" and "Apply Channel Template"; "Irregular Channels" tab lists 7 factory templates
  (rural road sections with 6:1 ditches; 60/80/100-ft right-of-way street sections, 2 % cross fall,
  some with 14-ft median). Red dots mark required fields.
- **Copy/Paste/Import/Export**: clipboard and data-file sources, delimiters comma, semicolon, colon,
  vertical bar, space, tab.
- **Standard Nc Values**: table citing *Handbook of Hydraulics*, 7th ed., Table 7-14 (Brater, King,
  Lindell, Wei), with Best/Good/Fair/Bad columns; click a value to apply.

## Validation messages observed
- "Pipe Diameter: Value must be greater than 0.00000"
- "N Value: Value must be greater than 0.00700" (after the n picker was closed without a choice, the
  field showed "Error" — observed UX weakness: cancelling a picker cleared the value).
- Output pane: "Channel calculation results will appear when there are no channel input errors."
