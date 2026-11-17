# MasterFlow

MasterFlow is a Windows desktop application for steady, uniform open-channel flow calculations
(Manning's equation), built for practising civil engineers who need fast answers they can audit.

**Status (2026-11-17): v0.3.0.** Rectangular, trapezoidal and triangular channels, circular pipes and elliptical pipes
(ASTM C507 HE/VE standard sizes or a custom true ellipse; partly full, both normal depths, full-flow and maximum capacity);
solve for discharge, normal depth, slope or Manning's n; critical depth/velocity/slope,
Froude number and regime; rating tables; versioned project files; printable HTML/PDF reports.
Irregular (station/elevation) sections are next (see [ROADMAP.md](ROADMAP.md)).

> MasterFlow is an independent product. It targets workflow parity with commercial tools such as
> Flow Calc 365 but contains no code, text, graphics or report layouts from them.

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
