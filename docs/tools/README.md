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
| Gutter spread and inlets | IDCM §4.4–4.5, HEC-22 | [gutter-inlets.md](gutter-inlets.md) |
| Inlet capacity and bypass routing | HEC-22, IDCM Table 4-11, MHFD Table 7-6 | [inlet-network.md](inlet-network.md) |
| Street capacity and gutter spread (10-yr and 100-yr) | IDCM §4.4.9–4.4.11, HEC-22 | [street-capacity.md](street-capacity.md) |
| Detention pond routing | IDCM §4.10; modified rational or entered hydrographs | [detention-routing.md](detention-routing.md) |
| Backwater / drawdown profile | Standard step (Chow 1959) | [backwater-profile.md](backwater-profile.md) |
| Storm network plan review | IDCM §4.6 | [plan-review.md](plan-review.md) |
| Quantity takeoff and cost opinion | Network geometry, your unit prices | [quantity-takeoff.md](quantity-takeoff.md) |
| Culvert analysis | FHWA HDS-5 (2012), IDCM §4.7 | [culverts.md](culverts.md) |
| Batch scenarios (premium) | Any tool or the network HGL, side by side | [batch-scenarios.md](batch-scenarios.md) |
| Network optimizer (premium): least-cost sizes and inverts | IDCM §4.6; dynamic programming | [network-optimizer.md](network-optimizer.md) |
| ★ Drainage report builder | IDCM §1.15, §4.1.8; PDF, Word, HTML | [drainage-report.md](drainage-report.md) |
| ★ Hydrograph network routing | NRCS UH, TR-55, Muskingum–Cunge, IDCM §4.3 | [hydrograph-routing.md](hydrograph-routing.md) |

Tools marked **★ Premium** need a MasterFlow Premium license key (see [../premium/licensing.md](../premium/licensing.md)).
