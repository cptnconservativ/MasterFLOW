# Discovery

Date: 2026-11-17. Prepared for Kyle Hibbs, P.E.

## Workspace inventory
- Cloud workspace `/home/user`: **empty** (no repository, files or documents).
- Shared project folder: empty.
- GitHub: **no account linked** to this session, so no existing MasterFlow repository could be
  listed or attached. If one exists, it must be connected and reconciled (see RISK R-01).

**Conclusion:** no existing MasterFlow code, specs, samples or prior roadmap were available.
MasterFlow was started as a new repository. Nothing was overwritten.

## Inputs used
1. The written brief (thread message, 2026-11-17).
2. Engineering references listed in [SOURCE_LOG.md](SOURCE_LOG.md).

## Key decisions from discovery
- Regular shapes (rectangle, trapezoid, triangle) are first-class parametric sections, with
  irregular station/elevation sections alongside them (D-007).
- Solve-for is n, slope, normal depth or discharge; outputs include critical depth, critical slope,
  Froude, specific energy and velocity-depth product; pipes add dual normal depth, full-flow and
  maximum-discharge values.
- Titles use expression tokens rendered with results.
- k = 1.486 (U.S.) is kept so results match U.S. hand calculations (D-004).

## Missing information / open questions (none blocking)
- Existing MasterFlow repository, if any (none visible).
- Desired distribution (MSIX/Store vs. signed installer) and code-signing certificate — needed in Phase G.
- Whether reports must follow a firm template — the default is an original MasterFlow layout.

## Contradictory requirements
None found. The brief names WPF/WinUI/Avalonia as options; Avalonia was chosen (D-002).
