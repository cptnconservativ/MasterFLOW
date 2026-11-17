# Discovery

Date: 2026-11-17. Prepared for Kyle Hibbs, P.E.

## Workspace inventory
- Cloud workspace `/home/user`: **empty** (no repository, files or documents).
- Shared project folder: empty.
- GitHub: **no account linked** to this session, so no existing MasterFlow repository could be
  listed or attached. If one exists, it must be connected and reconciled (see RISK R-01).
- User's Windows PC (reachable by remote control): Flow Calc installed and licensed; no MasterFlow
  folder was requested or inspected.

**Conclusion:** no existing MasterFlow code, specs, samples or prior roadmap were available.
MasterFlow was started as a new repository. Nothing was overwritten.

## Inputs used
1. The written brief (thread message, 2026-11-17).
2. Flow Calc public website and EULA/privacy pages ([COMPETITOR_PUBLIC_REVIEW.md](COMPETITOR_PUBLIC_REVIEW.md)).
3. Flow Calc installed app, Example Project, read-only ([FLOWCALC_UI_INVENTORY.md](FLOWCALC_UI_INVENTORY.md)).
4. Engineering references listed in [SOURCE_LOG.md](SOURCE_LOG.md).

## Key findings
- **Observation:** Flow Calc treats every open channel as an irregular station/elevation section;
  rectangular/trapezoidal/triangular exist only as point-generating wizards.
- **Observation:** solve-for is n, slope, normal depth or discharge; outputs include critical depth,
  critical slope, Froude, specific energy, velocity-depth product; pipes add dual normal depth,
  full-flow and maximum-discharge values.
- **Observation:** titles use expression tokens (e.g. %%Q2) rendered with results.
- **Inference:** Flow Calc uses k ≈ 1.48592 (two matching data points).
- **Observation:** no quick calculator or alternative-comparison feature was found.

## Missing information / open questions (none blocking)
- Existing MasterFlow repository, if any (none visible).
- Desired distribution (MSIX/Store vs. signed installer) and code-signing certificate — needed in Phase G.
- Whether reports must follow a firm template — the default is an original MasterFlow layout.

## Contradictory requirements
None found. The brief names WPF/WinUI/Avalonia as options; Avalonia was chosen (D-002).
