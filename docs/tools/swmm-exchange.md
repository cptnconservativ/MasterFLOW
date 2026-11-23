# EPA SWMM 5 exchange (★ Premium)

Tool id `swmm-exchange` (license feature of the same name). It moves the selected storm network into and out of
EPA SWMM 5, so that a dynamic-wave run can check the steady HGL, or a SWMM model from a consultant or the City can
be loaded and checked against the IDCM in MasterFlow.

| `mode` | What it does |
|---|---|
| `export` (default) | Writes the network as a `.inp` file (Save offers it) |
| `import` | Reads a `.inp` (`file = path`, or the text pasted under the keys) into the selected network; **Apply to network** replaces its structures and pipes and keeps its name and criteria |
| `compare` | Reads a SWMM report (`.rpt`) and lists, per structure, MasterFlow's HGL against SWMM's peak HGL, the rim clearance of each, SWMM flooding, and pipe flows |

## Export

| SWMM section | From |
|---|---|
| `[OPTIONS]` | CFS (CMS for SI), DYNWAVE, LINK_OFFSETS DEPTH, `duration` (h, default 6 or the storm + 2 h), `routing step` (s, default 5), inertial damping PARTIAL, normal-flow limit BOTH |
| `[JUNCTIONS]` | Every structure with an outgoing pipe. Invert = the lowest pipe invert at the structure; max depth = rim − invert (0 when there is no rim, so SWMM uses the highest crown); `ponded area` (default 0, ALLOW_PONDING NO) |
| `[OUTFALLS]` | Structures with no outgoing pipe. Free outfall → FREE; tailwater elevation → FIXED; the (yc + D)/2 and crown rules have no SWMM equivalent and are written as FIXED at MasterFlow's solved outfall HGL |
| `[CONDUITS]`, `[XSECTIONS]` | Length, n, offsets (pipe invert − node invert); CIRCULAR, or HORIZ_/VERT_ELLIPSE with rise and span |
| `[LOSSES]` | `entry loss` and `exit loss` K for every conduit; a standard-K structure's K becomes the entry loss of the pipe leaving it. IDCM Figure 4-6/4-7 losses cannot be expressed in SWMM (D-331) |
| `[INFLOWS]` | `inflows = design` (default), `minor` or `none`: constant baselines. When the network's flows are typed on pipes, each node gets the flow of its outgoing pipe less its incoming pipes, so SWMM carries the same flows (D-330) |
| `[SUBCATCHMENTS]` etc. | Optional `[Subcatchments]` (Name, Outlet, Area (ac), Impervious (%), Width (ft), Slope (%), then CN, or Soil (IDCM Table 4-4 class → Green–Ampt ψ and Ks), or Suction and Ks; Deficit default 0.20 per IDCM §4.3.6). N-imperv 0.015, N-perv 0.24, depression storage 0.05 / 0.35 in., 25 % zero-storage impervious |
| `[RAINGAGES]`, `[TIMESERIES]` | `storm = rapid-city` (IDCM Table 4-2 frequency storm, `year` 2/10/100, `storm duration` 60–1440 min, 5-min blocks peaking at the first quartile, the same storm the hydrograph routing tool uses) or `type-ii` (`P24`), as 5-min intensities |
| `[COORDINATES]` | Northing/easting; structures without them are placed on a line (schematic map) |

## Import

Junctions and storage units (rim = invert + max depth), outfalls (FIXED stage → tailwater elevation; the highest
when they differ), conduits with CIRCULAR, FORCE_MAIN, FILLED_CIRCULAR (read as circular) and elliptical sections,
offsets as depths or elevations, constant FLOW inflows (a time series is read as its peak), any flow units (GPM, MGD,
LPS, MLD converted), coordinates. Pumps, weirs, orifices, outlets, other section shapes and multi-barrel conduits
are reported and left out, because a MasterFlow network holds single circular or elliptical pipes. The file's unit
system must match the project's.

## Compare

Reads the Node Depth Summary (max HGL), Node Flooding Summary and Link Flow Summary of a SWMM 5.1/5.2 report.
A structure is marked "check" when the two HGLs differ by more than 0.1 ft (0.03 m). Differences are expected: SWMM's
peak includes storage and timing and has no IDCM structure losses.

## Decisions

- **D-330** Pipe flows become node inflows by continuity (outgoing − incoming), so a network whose flows are typed on
  pipes exports with the same flows; a negative difference is reported and not written.
- **D-331** IDCM structure losses are left to the conduit entry/exit K the engineer chooses; no conversion is
  attempted, because hj = V2²/2g − c·V1²/2g depends on two pipes and SWMM's losses on one.
- **D-332** Import keeps the selected network's criteria, name and structure loss defaults, so a SWMM model is checked
  against the Rapid City criteria straight away.

## Validation

`SwmmExchangeTests`: every section written, offsets from the lowest invert, export → import round trip gives the same
pipes, flows and HGL, IDCM Table 4-2 storm depth preserved in the time series (10-yr 120-min = 1.98 in.), Table 4-4
Green–Ampt values, GPM conversion, elliptical sections, unsupported elements reported, report comparison.
EPA SWMM itself was not available in this environment, so the files have not been opened in SWMM yet.
