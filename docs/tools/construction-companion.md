# ★ Construction-phase companion

**Tools → Construction-phase companion** (premium). Checks a contractor's submittal or RFI against the design and
drafts the response letter.

## What it checks

| Input | Check |
|---|---|
| `allowed materials` | Every submitted pipe material is on the list from the specifications. A material that is not allowed makes the disposition **REJECTED**. |
| `[Pipe]` | Proposed material, size, class and Manning's n for `all` pipes or named pipes. Later rows override earlier ones, so `all HDPE` then `P1 RCP` leaves P1 as RCP. Blank cells keep the design value. n defaults by material (RCP 0.013, HDPE 0.012, PVC 0.010, CMP 0.024, DIP 0.012, PP 0.012). A smaller size fails; a class is always a NOTE to confirm against the fill height. |
| `[Changes]` | RFI field changes: `<pipe> upstream invert = v`, `<pipe> downstream invert = v`, `<pipe> size = v` (in.) or `<structure> rim = v`. Rows with the same RFI number are applied together. |
| `[Compare]` | Anything else (riprap D50 and thickness, grate load rating, bedding…): Required vs Proposed with a rule `>=`, `<=`, `=` (text compare ignores case) or `one of` (Required lists choices separated by `/`). |

## Hydraulic check

Each case (the submitted pipe, each RFI, and everything together) is the design network with that change applied,
re-solved with the design flows and tailwater, and compared with the design's own results:

* least rim − HGL (outfalls excluded) and the largest HGL rise at any structure;
* pipes newly over full-flow capacity;
* new Plan review (IDCM §4.6) failures that the design did not have.

Only changes from the design count, so a design that already has a known exception does not fail every submittal.

## Disposition

| Worst finding | Disposition |
|---|---|
| a material not in `allowed materials` | REJECTED |
| any FAIL | REVISE AND RESUBMIT |
| NOTEs only | MAKE CORRECTIONS NOTED |
| none | NO EXCEPTIONS TAKEN |

**Save…** writes the response letter (Word or PDF): disposition, numbered comments, accepted items, the standard
"general conformance" statement, the hydraulic table, and the full item table as an appendix.

## Decisions

* **D-300** RFI changes and material substitutions are judged by re-solving the network, not by rule of thumb, so an
  invert raised for rock is accepted only when the HGL criteria still hold.
* **D-301** Only *new* failures count against a submittal; existing design exceptions are reported in the design, not
  charged to the contractor.
* **D-302** Pipe class is never auto-approved: the tool does not know fill heights, so it is always a NOTE.
* **D-303** The letter drafts the response only. The engineer of record reviews and signs it; the tool never sends it.
