# Inverse design (★ Premium)

Tool id `inverse-design` (license feature of the same name). Most tools answer "does this design pass?". This tool
answers the reverse: **how far can one input go before a criterion fails?** Examples:

- the largest low-flow orifice that still holds the 2-yr release to the pre-development peak;
- the most flow pipe P1 can take before the HGL comes within 1 ft of the MH-2 rim;
- the largest contributing area the pond can serve; the smallest pipe size, the highest tailwater, the lowest rim.

## Input

| Key | Meaning |
|---|---|
| `tool` | The design tool whose input is varied (and re-run). Its input is the one saved in the project, or the text between `[Base input]` and `[End base input]`; with neither, its sample input (I-ID-001). Leave blank when only a storm network is varied |
| `vary = <key>` | A `key = value` line of that input (`Section.key` for a key inside a section) |
| `vary = <Section> \| <row> \| <column>` | One cell of an input table: the row by its first cell, the column by its header (or its start). The unit in the header, e.g. "(ft)", labels the answer |
| `vary = network: <name> \| <item> \| <field>` | A storm network: pipe `q` (cfs or m³/s), `size` (in. or mm), `n`; structure `inflow`, `rim` |
| `find` | `max` (default) or `min` |
| `from`, `to` | The range searched |
| `steps` | Grid steps across the range (default 12, 2–100) |
| `tolerance` | Bisection tolerance (default range ÷ 1000) |

Under `[Constraints]`, one per line, a live value reference (the same references as the
[drainage report](drainage-report.md#living-report-live-values-and-what-changed-since-issue)), an operator and a
limit:

```
[Constraints]
{value: detention-routing | Routing results | 2-yr | Peak outflow} <= 4
{value: detention-routing | Routing results | max | Drain time} >= 12
{network: Main St | min | clearance} >= 1
{value: hds5-culvert | Criteria | Design (10-yr) freeboard | Result} = PASS
```

`<=`, `<`, `>=`, `>` take a number; `=` takes a text (OK, PASS). A constraint can read any tool or network in the
project, not only the one being varied, so a downstream check can limit an upstream input.

## Method

1. The input is set to each of `steps + 1` values evenly spread from `from` to `to`; the tool (or network solver) is
   re-run and every constraint is resolved.
2. For `find = max`, the last value that meets all constraints and the next one that does not bracket the limit (the
   mirror for `min`); bisection then narrows the bracket to the tolerance. The reported answer is always a value that
   was run and met every constraint.
3. The governing constraint is the one that fails first just past the answer.

## Output

- Summary: the answer, the room left from the value the design uses now, and the governing constraint.
- **Constraints at the answer**: limit, value, utilization (value ÷ limit for ≤, limit ÷ value for ≥; 100 % is at the
  limit), result and source.
- **Trials**: every run, in order of the varied value, with each constraint's value and whether all were met.
- Chart: utilization of each constraint across the range, with the 1.0 limit line.

Warnings: W-ID-002 every value in the range passes (the answer is past the range: widen it); W-ID-003 none passes;
W-ID-004 the constraints switch between met and not met more than once, so there is more than one feasible band.

## Decisions

- D-240: a scan followed by bisection, not Newton or secant iteration: design tools are piecewise (critical duration
  changes, inlet/outlet control switches, weirs start flowing) and a scan finds the bracket even where the response has
  kinks, and shows the whole response for the engineer to judge.
- D-241: constraints reuse the drainage report's live value references, so anything any tool shows can be a
  criterion with no per-tool code, and the same reference text works in the report and here.
- D-242: one varied input per run. Two or more at once is an optimization (see the network optimizer); one input keeps
  the answer a single number with a clear governing criterion.
- D-243: the answer reported is the last value that passed, never the midpoint of the final bracket, so it can be typed
  into the design as is.
