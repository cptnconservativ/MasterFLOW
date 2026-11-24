# Feature ideas

Written 2026-11-18 after the v0.11.0 feature review (see [FEATURE_MATRIX.md](FEATURE_MATRIX.md)).
These are ideas to go further. Ranking is
engineering value for a drainage or roadway engineer against effort; **H/M/L** = value, **S/M/L** = effort.

## A. Hydraulics that engineers reach for right after Manning

| # | Idea | Value | Effort | Notes |
|---|---|---|---|---|
| A1 | **Culvert inlet and outlet control (FHWA HDS-5)** | H | L | Headwater for a given Q, controlling regime, performance curve. The most-asked follow-on to a pipe capacity check. Needs the HDS-5 inlet coefficients (public domain) and tailwater from the channel calc |
| A2 | **Gutter spread and inlet capacity (FHWA HEC-22)** | H | M | Spread T, gutter depression, grate/curb/combination inlet interception and bypass. Street templates already exist, so this extends them |
| A3 | **Channel lining check (FHWA HEC-15)** | H | S | Compare the γyS shear already computed with permissible shear for grass, riprap (D50), turf mats, concrete; pass/fail with factor of safety. Riprap D50 sizing |
| A4 | **Freeboard check** | M | S | Channel depth − yn against a required freeboard (fixed or a formula such as USBR); report pass/fail |
| A5 | **Weirs and orifices** | M | M | Sharp-crested, broad-crested, V-notch, orifice; rating curves; useful for outlet structures |
| A6 | **Parabolic and other shapes** (parabolic swale, arch pipe, box culvert with fillets, pipe-arch CSP sizes) | M | M | Arch and box sizes from public manufacturer/ASTM tables, as for elliptical |
| A7 | **Velocity limits by lining / material** | M | S | Warn when V is outside min (self-cleansing, e.g. 2 ft/s for sewers) or max for the lining |
| A8 | **Hydraulic jump and sequent depth** | M | S | For supercritical results: conjugate depth, jump length, energy loss |
| A9 | **Simple storm sewer HGL (pipe network)** | H | L | Run-by-run Manning capacity and HGL with junction losses; bigger product step, treat as a separate phase |
| A10 | **Rational-method Q helper** | M | S | Q = CiA with IDF lookup entered by the user, feeding the Discharge field |

## B. Workflow and data exchange

| # | Idea | Value | Effort | Notes |
|---|---|---|---|---|
| B1 | **Batch from spreadsheet** | H | M | Import a CSV/xlsx of many pipes or ditches (shape, size, n, S, Q) and solve all at once; one report |
| B2 | **LandXML export and DXF section export** | M | S | Send the water surface back to Civil 3D, or a section drawing into CAD |
| B3 | **Pick n by station range from an aerial or land cover list** | L | M | For long surveyed sections |
| B4 | **Sensitivity / range check** | H | S | Show how yn and V change for n ± 10 % or 20 %, slope min/max; one table in the report |
| B5 | **Command-line batch** (`MasterFlow.exe --report project.mfproj`) | M | S | Lets a firm regenerate reports from scripts |
| B6 | **Project-wide find and replace** of n, slope or units across calculations | M | S | |
| B7 | **Station display option (12+34.50)** in point grids and reports | L | S | Formatter already exists (LandXML import) |
| B8 | **User precision settings** | M | S | Decimal places per quantity, saved per user |
| B9 | **Quick calculator** pane that does not create a project item | L | S | |

## C. Review and QA (where MasterFlow stands apart)

| # | Idea | Value | Effort | Notes |
|---|---|---|---|---|
| C1 | **Checker sign-off** | H | S | "Prepared by / checked by / date" on each calculation, shown in the report and locked by a hash so later edits clear the check |
| C2 | **Report diff between project versions** | M | M | What changed in inputs and results since the last issued report |
| C3 | **Design criteria profiles** | H | M | A firm or agency profile (min velocity, max velocity, freeboard, max Fr, allowed n) checked automatically, with warnings in the report |
| C4 | **Narrator and keyboard-only test pass** | M | S | Needs a Windows run (on the release checklist) |
| C5 | **Signed releases and auto-update check (opt-in, off by default)** | M | S | Needs the firm's certificate (G2); update check must stay opt-in under the no-telemetry rule |

## Suggested order

1. A3 lining check and A4 freeboard (small, uses values already computed).
2. B4 sensitivity table and C1 checker sign-off (small, high value in review).
3. A1 culverts (HDS-5), then A2 gutters and inlets (HEC-22).
4. B1 batch from spreadsheet, then C3 criteria profiles.
5. A9 storm sewer HGL as its own phase.

Each one gets the same treatment as the rest of MasterFlow: published method, independent hand
calculation, tolerances set beforehand, structured warnings, and no numbers on nonconvergence.
