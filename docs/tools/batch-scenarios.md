# Batch scenarios (premium)

Tool id `batch-scenarios`. It runs many cases at once and compares them side by side.

## Storm network (`tool = network`)

Each row under `[Scenarios]` is the selected network with that row's changes. The network is then solved by the same
HGL/EGL solver. Blank cells keep the network's own value. Keys (the column headers):

| Key | Effect |
|---|---|
| `tailwater` | An elevation, or `free`, `crown` or `hec22` ((yc + D)/2) |
| `flow factor` | Multiplies the typed pipe flows and the structure inflows (for example ×1.6 for a larger storm) |
| `minor flow factor` | The same for the minor-storm flows |
| `n` | Manning's n of every pipe |
| `junction k` | The network's default K |
| `hgl below rim` | The HGL criterion |
| `pipe <name> size` | Rise in in. (mm) |
| `pipe <name> n` | One pipe's n |
| `structure <name> inflow` / `minor inflow` | One structure's inflow |

Output:
* A scenario summary: changes, whether it solved, worst rim − HGL, number of structures below the criterion, maximum
  V, minimum minor-storm V, pipes flowing full, outfall HGL.
* HGL by structure.
* Rim − HGL by structure.
* Q and V by pipe.
* One chart with the HGL profile of the longest path for every scenario.

## Any design tool (`tool = <id>`)

The other tool's full input goes between `[Base input]` and `[End base input]`. When that block is missing, the tool's
sample input is the base (I-BS-002).

* Each scenario column replaces a `key = value` line. Use `Section.key` for a key inside a section. A key that is not
  in the base input is added, and that is reported (W-BS-002).
* `[Compare]` rows (Table, Row, Column) name result cells by the start of the table title, of the first-column label
  and of the column header, ignoring case. They are tabulated with one column per scenario.
* `chart = <title start>` overlays that chart from every scenario.
* A scenario that fails is reported (W-BS-001), and the others still run.

Scenario names must not contain commas: a row with no tab is split on commas. A batch runs at most 50 scenarios.

## Messages

* Errors:
  * E-BS-001: scenarios missing, duplicated or too many.
  * E-BS-002: no network, or an unknown tool.
  * E-BS-003: unknown network key.
  * E-BS-004: bad value.
  * E-BS-005: the license does not cover the premium tool named by `tool =` (the same gate as the Run button).
* Warnings:
  * W-BS-001: a scenario failed.
  * W-BS-002: a key was added.
  * W-BS-003: a compare cell was not found.
  * W-BS-004: no chart to overlay.
* Information: I-BS-001 and I-BS-002.

## Validation

Tests check that:
* The base network scenario reproduces the reference HGLs (MH-2 104.5139, CB-3 107.3672).
* Every changed scenario equals a direct solve of the changed network, for flows, tailwater, n, size and inflows.
* Every compared culvert cell equals a direct culvert run with the changed input.

## Decisions

* D-095: overrides are text replacements of the other tool's own input, so every tool works without changes to it,
  and a scenario can be pasted back into that tool unchanged.
* D-096: network scenarios change the network in memory only. **Apply to network** is not offered, because a
  comparison should not change the design.
* D-097: design storms are scaled by flow factors for now. Once hydrograph routing (#25) is merged, a scenario key can
  select a storm by return period.
