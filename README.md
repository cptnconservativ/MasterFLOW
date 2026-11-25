# MasterFlow

MasterFlow is a Windows desktop application for steady, uniform open-channel flow calculations
(Manning's equation), built for practising civil engineers who need fast answers they can audit.

**Status (2026-11-18): v0.11.0.** Rectangular, trapezoidal and triangular channels, irregular station/elevation sections
(per-segment n, conveyance subdivision or equal-velocity composite n), circular pipes and elliptical pipes
(ASTM C507 HE/VE standard sizes or a custom true ellipse; partly full, both normal depths, full-flow and maximum capacity);
solve for discharge, normal depth, slope or Manning's n; critical depth/velocity/slope,
Froude number and regime; rating tables; versioned project files; printable HTML/PDF reports.
v0.5.0 adds V·y, boundary shear, HGL/EGL elevations, a Manning n reference list and rating curves.
v0.6.0 adds side-by-side alternative comparison and Excel (.xlsx) export; v0.7.0 adds folders, sorting,
a recycle bin, recent files, autosave with crash recovery and a report logo; v0.8.0 adds live title
expressions such as "{shape}, Q = {Q:F1} {Qunit}" and built-in and user templates. v0.9.0 writes reports directly as PDF (single, batch and comparison); v0.9.1 adds a per-calculation tick for what goes into batch outputs; v0.10.0 adds a local-only error log with crash recovery and performance budgets (5000-row tables, 5000-point sections); v0.10.1 adds the user guide, screen-reader names and a scripted release (docs/RELEASE_CHECKLIST.md); v0.11.0 adds point-grid tools, regular → irregular conversion, LandXML (Civil 3D) cross-section import, rating tables saved per calculation and item numbers. Further ideas: [docs/FEATURE_IDEAS.md](docs/FEATURE_IDEAS.md). Next: Phase G packaging and release work (see [ROADMAP.md](ROADMAP.md)).

See the [user guide](docs/USER_GUIDE.md) for how to use it.

> MasterFlow is an independent product, built from published engineering methods and its own
> independent validation.

## Build and run

Requires the .NET 10 SDK.

```
dotnet build MasterFlow.slnx
dotnet test MasterFlow.slnx
dotnet run --project src/MasterFlow.Desktop            # starts the app with a sample project
dotnet run --project src/MasterFlow.Desktop -- my.mfproj
```

Publish a self-contained Windows build:

```
dotnet publish src/MasterFlow.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Layout

| Path | Purpose |
|---|---|
| `src/MasterFlow.Domain` | Units, section definitions, inputs, project model (no logic) |
| `src/MasterFlow.Hydraulics` | Geometry, Manning/critical-flow engine, Brent solver, messages |
| `src/MasterFlow.Application` | Rating tables, result formatting, plot geometry |
| `src/MasterFlow.Persistence` | Versioned JSON project file (`.mfproj`) |
| `src/MasterFlow.Reporting` | HTML calculation report with SVG cross-section |
| `src/MasterFlow.Desktop` | Avalonia UI (MVVM) |
| `tests/*` | Engine, application, integration and view-model tests |
| `validation/` | Independent Python hand calculations used as test references |
| `docs/` | Discovery, methods, validation, architecture, decisions, competitor review |

## Keyboard

Ctrl+N new · Ctrl+O open · Ctrl+S save · Ctrl+Shift+S save as · Ctrl+Z / Ctrl+Y undo/redo ·
Ctrl+T add channel · Ctrl+D duplicate · Ctrl+E export report.

## Engineering responsibility

Results must be reviewed by the responsible engineer. See [docs/HYDRAULIC_METHODS.md](docs/HYDRAULIC_METHODS.md)
for equations, assumptions and limits, and [docs/VALIDATION_RESULTS.md](docs/VALIDATION_RESULTS.md) for verification.
