# ★ Site feasibility (go/no-go in ten minutes)

Takes a parcel and returns what a go/no-go meeting needs: area, floodplain, soils, slopes, the Rapid City zoning
standards, a lot yield, a drainage concept (pre/post peaks, detention and WQCV, pond footprint and location), a rough
grading picture, an order-of-magnitude cost range, and a list of constraints with a verdict (GO, GO WITH CONDITIONS,
NO-GO). Premium feature `site-feasibility`.

## Data

`fetch = yes` queries free public services. Only coordinates (or the parcel PIN) are sent, and every answer is cached
on this computer (`%LOCALAPPDATA%\MasterFlow\site-data`), so a second run, or a run with no internet, reads the cache.
`refresh = yes` fetches again. A section entered in the input always wins over fetching, and it is how the tool runs
fully offline.

| Data | Service | Entered instead |
|---|---|---|
| Boundary | Pennington County tax parcels, Rapid City MSP ArcGIS service, layer 5 (`parcel = PIN`) | `[Boundary]` Latitude, Longitude |
| Floodplain | FEMA NFHL MapServer layer 28 (falls back to the Rapid City MSP copy, layer 4) | `[Flood zones]` Zone, Percent (`AE floodway`, `AE`, `X (0.2 % annual chance)`) |
| Soils | NRCS Soil Data Access (SSURGO): map units clipped to the boundary, dominant-component HSG | `[Soils]` Map unit, Name, HSG, Percent; or `assume hsg = C` |
| Elevation and slopes | USGS 3DEP image service `getSamples` on a 15 × 15 grid (`elevation grid`) | `mean slope`, `relief`, `[Slope classes]` Range (%), Percent |
| Rainfall | IDCM 2012 Table 4-2 (design, always available); NOAA Atlas 14 PFDS for comparison | `rainfall = atlas14` uses Atlas 14 (with a warning) |
| Zoning | Rapid City does not publish its zoning map as a GIS layer | `zoning = LDR-1` (GAD, PF, LDR-1, LDR-2, MDR, HDR, GC, LI) |
| Utilities | Rapid City does not publish mains as a public layer | `[Utilities]` Utility, Distance (ft), Size (in) |

When a service cannot be reached the tool says so (W-SF-010) and carries on with what it has. A part that has no data
is reported as UNKNOWN, never guessed: no slopes → error E-SF-008; no soils → no runoff or pond (W-SF-020); no flood
data → nothing excluded for floodplain and an UNKNOWN row in the go/no-go table.

## Method

| Step | Method |
|---|---|
| Area | Local plane projection of the lat/long boundary (R = 20,902,231 ft), shoelace area |
| Flood and slope shares | 60 × 60 sample grid over the boundary; with fetched data the exclusions are the union of floodway, SFHA and steep cells; with entered shares they are summed (conservative) |
| Slopes | Central differences on the 3DEP grid; least-squares plane for the general direction of fall; the lowest node is the suggested pond location |
| Runoff | TR-55: CN from Table 2-2 by HSG (existing cover entered; developed cover from the zoning lot size, e.g. 7,500 ft² lots → residential 1/4 ac), Tc by the NRCS lag method, graphical peak (Type II) for the IDCM 2-, 10- and 100-yr 24-h depths |
| Detention | TR-55 Ch. 6 Figure 6-1: Vs/Vr = 0.682 − 1.43 r + 1.64 r² − 0.804 r³, r = pre-development peak / post-development peak; the largest of the three storms |
| WQCV | Rapid City SWQM (2009) §2.2.2: 1.16·a·(0.91I³ − 1.191I² + 0.78I), a = 1.0 for 40 h |
| Pond footprint | (detention + WQCV) / `pond depth` × `pond footprint factor` (1.3) |
| Yield, single- and two-family | lots = developable ft² / (lot area / `lot efficiency` + frontage/2 × `row width`) |
| Yield, multi-family | MDR/HDR base + per-unit area on building lots of `building lot` ac |
| Yield, commercial/industrial | buildable area × maximum coverage |
| Earthwork | Balanced pads on a plane: cut ≈ disturbed area × mean slope × lot depth / 8, plus pond excavation |
| Cost | Quantities × `[Unit costs]` (placeholders until you enter yours), + contingency 20 % + engineering 12 %; range −30 % / +50 % |

## Decisions

- **D-200** Site feasibility is one design tool (same input, report, PDF and Excel as the others) and is premium.
- **D-201** Public data are fetched synchronously and cached per request (hash of URL and body); a failed fetch falls back to the cache, then to entered data. Same pattern as the Atlas 14 tool.
- **D-202** Rapid City zoning standards are built in from Title 17 as published (Zoneomics mirror through Ord. 6723, read 2026-11-21). GC minimum lot area/width and HDR multi-family side yards were not confirmed and carry a warning.
- **D-203** Shares of the site in flood zones and slope classes are measured on a sample grid instead of polygon clipping: simple, robust to bad geometry, and within about 1 % at 60 × 60.
- **D-204** Dual hydrologic groups (C/D) use the first (drained) letter, as a developed site is drained.
- **D-205** Floodway is always excluded; SFHA and slopes over `steep slope` (25 %) are excluded unless `exclude floodplain = no` / `exclude steep = no`.
- **D-206** Lots on a double-loaded street each take half their frontage times the ROW width.
- **D-207** The earthwork is an order of magnitude only; the grading solver (idea 7) replaces it.
- **D-208** Costs exclude land, fees, dry utilities and off-site streets.
- **D-209** Verdict: any FATAL → NO-GO; any CONSTRAINT or UNKNOWN → GO WITH CONDITIONS; else GO. FATAL: floodway > 25 %, SFHA > 50 %, no yield, yield < 75 % of `target lots`, cost per lot above `max cost per lot` even at the low end.

Other inputs: `target lots`, `max cost per lot`, `other exclusions` (ac), `impervious` (%), `water quality = no`,
`wq drain time`, `street length`, `storm sewer ratio`, `retaining` (ft² of wall face), `contingency`, `engineering`,
`range low`, `range high`, `steep slope`.

Live services were not reachable from the build environment, so the parsers are tested against recorded response
shapes; check the first fetched run against the FEMA FIRM, Web Soil Survey and a contour map.
