# Design tools

**Design tools…** (main window) opens one window for every drainage design tool. Each tool:

- takes text input: `key = value` lines, then tables under `[Section]` headings (tab- or comma-separated;
  the first line of a table is its header; `#` starts a comment). **Sample** fills in a worked example,
  using the selected storm network's structure names where it can;
- shows its result as aligned text, with every error, warning and assumption listed under Messages.
  A part that cannot be computed is reported, never estimated;
- copies (text plus tab-separated tables), saves an HTML report, a PDF report or an Excel workbook (tools that draw charts show them in the HTML and PDF);
- may offer **Apply to network**, which writes its result into the selected storm network and re-solves it.
  Nothing changes in the network until you press it.

The last input of each tool is saved with the project. Tools are discovered automatically (any class
implementing `IDesignTool` in MasterFlow.Application), so each tool lives in its own file with its own
tests; every tool's sample input is run by the test suite and must finish without errors.

| Tool | Source | Doc |
|---|---|---|
| Rational method | Rapid City IDCM 2012 §4.2 | [rational-method.md](rational-method.md) |
| ★ NOAA Atlas 14 rainfall (IDF lookup) | NOAA Atlas 14 via PFDS | [atlas14-idf.md](atlas14-idf.md) |
| Gutter spread and inlets | IDCM §4.4–4.5, HEC-22 | [gutter-inlets.md](gutter-inlets.md) |
| ★ Inlet capacity and bypass routing | HEC-22, IDCM Table 4-11, MHFD Table 7-6 | [inlet-network.md](inlet-network.md) |
| Street capacity and gutter spread (10-yr and 100-yr) | IDCM §4.4.9–4.4.11, HEC-22 | [street-capacity.md](street-capacity.md) |
| Sag inlet ponding and clogging | IDCM §4.5.3–4.5.4, Tables 4-11 and 4-12; HEC-22 | [sag-ponding.md](sag-ponding.md) |
| Bridge deck drainage: deck drain spacing | FHWA HEC-21 (1993), HEC-22 grate equations | [deck-drainage.md](deck-drainage.md) |
| TR-55 hydrology: curve number, Tc, peak discharge, unit hydrograph | NRCS TR-55 (1986), NEH 630 Ch. 16 | [tr55-hydrology.md](tr55-hydrology.md) |
| Flood frequency from annual peaks (log-Pearson III, EMA, low outliers, historical data) | USGS Bulletin 17C (2019) | [flood-frequency.md](flood-frequency.md) |
| Detention pond routing | IDCM §4.10; modified rational or entered hydrographs | [detention-routing.md](detention-routing.md) |
| Pond stage–storage from a TIN surface | Civil 3D LandXML surface; exact TIN volumes | [pond-storage.md](pond-storage.md) |
| ★ Multi-stage outlet designer | IDCM §4.10, SWQM 2009 §2.3.2 | [outlet-designer.md](outlet-designer.md) |
| Emergency spillways and dam breach | NRCS TR-60 (2005); Froehlich (1995, 2008), MacDonald–Langridge-Monopolis, USBR | [spillway-breach.md](spillway-breach.md) |
| Backwater / drawdown profile | Standard step (Chow 1959) | [backwater-profile.md](backwater-profile.md) |
| Channel water surface profile and lining checks | IDCM §4.8, Table 4-14; HEC-15; Chow (1959) | [channel-profile.md](channel-profile.md) |
| ★ Natural channel water surface profiles and bridges (surveyed cross-sections, several flows) | HEC-RAS Hydraulic Reference Manual Ch. 2 and 5 (energy method, conveyance subdivision, mixed regime; bridge energy, Yarnell, pressure and weir flow) | [river-profile.md](river-profile.md) |
| ★ Storm network plan review | IDCM §4.6 | [plan-review.md](plan-review.md) |
| ★ Review comment tracker and response letter | Agency review rounds; network results; IDCM §4.6 plan review | [review-comments.md](review-comments.md) |
| ★ Plan and profile sheets (DXF) with HGL and EGL | Network solution; IDCM §4.6.7 clearance | [plan-profile-dxf.md](plan-profile-dxf.md) |
| Quantity takeoff and cost opinion | Network geometry, your unit prices | [quantity-takeoff.md](quantity-takeoff.md) |
| Culvert analysis | FHWA HDS-5 (2012), IDCM §4.7 | [culverts.md](culverts.md) |
| Culverts for aquatic organism passage (stream simulation) | FHWA HEC-26 (2010) | [aop-culvert.md](aop-culvert.md) |
| Outlet protection: riprap apron, riprap basin, plunge pool | FHWA HEC-14 (2006) §10, IDCM §4.9 | [outlet-protection.md](outlet-protection.md) |
| Riprap bank and channel revetment, filter check | FHWA HEC-11 (1989); HEC-23 DG 4 classes | [riprap-revetment.md](riprap-revetment.md) |
| Hydraulic jump and stilling basins: free jump, USBR Type III and IV, SAF, USBR Type VI impact, CSU rigid boundary | FHWA HEC-14 (2006) Ch. 6, 8, 9; IDCM §4.9.2, §4.9.6 as a check | [stilling-basin.md](stilling-basin.md) |
| Bridge scour: contraction, pier, abutment | FHWA HEC-18 (2012) | [bridge-scour.md](bridge-scour.md) |
| Bridge scour countermeasures: pier and abutment riprap | FHWA HEC-23 (2009) DG 11, DG 14 | [scour-countermeasures.md](scour-countermeasures.md) |
| ★ Batch scenarios | Any tool or the network HGL, side by side | [batch-scenarios.md](batch-scenarios.md) |
| ★ Cost estimate and bid tabulation | Network takeoff, your prices and bids | [cost-estimate.md](cost-estimate.md) |
| ★ Water quality and LID sizing | Rapid City Storm Water Quality Manual (2009) via IDCM §4.1.7; MHFD USDCM Vol. 3 option | [water-quality.md](water-quality.md) |
| Erosion and sediment control (RUSLE, silt fence, sediment basin) | Rapid City SWQM 2009 Ch. 1; USDA AH 703 (RUSLE) | [erosion-sediment.md](erosion-sediment.md) |
| ★ Network optimizer: least-cost sizes and inverts | IDCM §4.6; dynamic programming | [network-optimizer.md](network-optimizer.md) |
| ★ Drainage report builder | IDCM §1.15, §4.1.8; PDF, Word, HTML; live values and changes since issue | [drainage-report.md](drainage-report.md) |
| ★ Drainage report builder | IDCM §1.15, §4.1.8; PDF, Word, HTML | [drainage-report.md](drainage-report.md) |
| ★ Plan-review pre-checker: report, plans and calculations | IDCM §1.15, §4.1–4.10; SWQM 2009 | [submittal-precheck.md](submittal-precheck.md) |
| ★ What changed? Redesign diff | Re-runs every network, channel and tool for two versions | [redesign-diff.md](redesign-diff.md) |
| ★ Site feasibility: lot yield, drainage concept, cost range, go/no-go | FEMA NFHL, NRCS SSURGO, USGS 3DEP, NOAA Atlas 14, RCMC Title 17, TR-55, SWQM | [site-feasibility.md](site-feasibility.md) |
| ★ Hydrograph network routing | NRCS UH, TR-55, Muskingum–Cunge, IDCM §4.3 | [hydrograph-routing.md](hydrograph-routing.md) |
| ★ Dynamic-wave storm sewer routing: unsteady HGL, surcharge, flooding | EPA SWMM 5 dynamic wave (link-node), IDCM §4.6.7 | [dynamic-wave.md](dynamic-wave.md) |
| ★ Climate stress test | Reruns the storm network for future rainfall projections, finds the rainfall increase at which each structure and pipe first fails, and writes a client resilience memo. [climate-stress.md](climate-stress.md) |
| ★ Construction-phase companion | Checks submittals (pipe, castings, riprap) and RFIs against the design, re-solves the network for each change, and drafts the response letter. [construction-companion.md](construction-companion.md) |
| ★ Institutional memory search: past projects, calcs and review letters | Your project folders; runs on the computer | [memory-search.md](memory-search.md) |
| ★ Permit and approval tracker: critical path to construction | Rapid City, Pennington County, SD DANR, USACE, FEMA, SDDOT approvals | [permit-tracker.md](permit-tracker.md) |
| ★ Utility conflict finder: 3D clashes and separation rules | Ten States Standards §38.3; your separation rules | [utility-conflicts.md](utility-conflicts.md) |
| ★ Grading solver: finished grades from ADA, driveway and drainage constraints | 2010 ADA Standards §403, §405; IBC 1804.4; balanced earthwork | [grading-solver.md](grading-solver.md) |
| ★ Field-to-model: as-built survey, photos and HGL certification | IDCM §4.6; NAD83 State Plane (SD South/North) | [field-to-model.md](field-to-model.md) |
| ★ Inverse design: largest or smallest input that still meets the criteria | Any tool or storm network; your constraints | [inverse-design.md](inverse-design.md) |
| ★ EPA SWMM 5 exchange: export .inp, import .inp, compare SWMM results with the HGL | EPA SWMM 5.2 input and report formats; IDCM Table 4-2, 4-4 | [swmm-exchange.md](swmm-exchange.md) |

Tools marked **★** need a MasterFlow Premium license key (so do **Size pipes**, **LandXML…** export and **Civil 3D results…** in the Storm networks window) (see [../premium/licensing.md](../premium/licensing.md)).
