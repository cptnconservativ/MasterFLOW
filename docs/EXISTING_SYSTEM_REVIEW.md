# Existing system review

There was **no existing MasterFlow system** to review (empty workspace, no linked repository).
This document therefore records the state of the codebase created on 2026-11-17 so later reviews
have a baseline.

| Item | State |
|---|---|
| Purpose | Steady uniform open-channel flow calculator (Manning) with reports |
| Stack | C# 13 / .NET 10, Avalonia 11.3.23, CommunityToolkit.Mvvm 8.4.2, xUnit 2.9 |
| Projects | Domain, Hydraulics, Application, Persistence, Reporting, Desktop + 4 test projects |
| Capabilities | Rect/trap/tri; solve Q, yn, S, n; critical flow; Froude/regime; warnings; rating tables (CSV/clipboard); `.mfproj` save/load (schema 1); HTML report → PDF; batch report; undo/redo; light/dark theme |
| Partially implemented | Report logo (supported in engine, no UI to pick a logo); Flow Calc parity items listed as planned in the matrix |
| Known defects | None open. Not yet run on Windows hardware. |
| Build | `dotnet build` succeeds with warnings-as-errors |
| Tests | 72 passed, 0 failed |
| Retain / refactor / replace | Retain all; the geometry interface will be extended for pipes and irregular sections (non-monotonic Q) |
