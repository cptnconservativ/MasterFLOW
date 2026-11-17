# Architecture

```
MasterFlow.Desktop (Avalonia, MVVM)
   │ uses
   ├── MasterFlow.Reporting ──► MasterFlow.Application ──► MasterFlow.Hydraulics ──► MasterFlow.Domain
   ├── MasterFlow.Persistence ───────────────────────────────────────────────────────► MasterFlow.Domain
```

| Layer | Responsibility | Must not |
|---|---|---|
| Domain | Unit systems and constants, section records, `UniformFlowInput`, `Project` | contain calculation logic or UI types |
| Hydraulics | Geometry (`ISectionGeometry`), `UniformFlowCalculator`, `RootFinder`, `CalcMessage` | reference UI, files, or culture-dependent formatting of inputs |
| Application | Rating tables, `ResultSummary` (single source of labels/units/precision), `CrossSectionDrawing` | reference Avalonia |
| Persistence | `.mfproj` JSON, schema version, migrations, atomic save | silently accept newer schemas |
| Reporting | Self-contained HTML + SVG reports | fetch external resources |
| Desktop | Views, view models, dialogs, clipboard | perform hydraulics itself |

Key properties
- **Determinism:** the engine is pure and static; identical input gives identical output.
- **Failure is explicit:** `UniformFlowResult.Succeeded=false` carries NaN numbers and error messages;
  every display path checks it.
- **Traceability:** each result carries a `Trace` (equation, constants, bracket, iterations, root).
- **No DI container:** the app has one window and a handful of services; the only seam is
  `IDialogService` so view models are testable.
- **Extensibility:** new shapes implement `ISectionGeometry`. Closed conduits will declare
  `DischargeIsMonotonic = false`, which routes them to a two-root search (Phase C).
