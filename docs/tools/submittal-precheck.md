# ★ Plan-review pre-checker

Reads the storm water report and the plan set and re-runs the project's calculations. It then lists the comments a Rapid City reviewer would be likely to make, each written as a review comment and citing the IDCM, Storm Water Quality Manual or criteria section it rests on. The aim is to answer those comments before the submittal goes in. Premium feature `submittal-precheck`.

## Input

| Key | Meaning |
|---|---|
| `report = path` | Storm water report: .pdf (with a text layer), .docx or .txt. It is read on this computer, and nothing is sent anywhere. |
| `plans = path` | Plan set PDF (its text is used to find sheet titles) |
| `[Report text]`, `[Plans text]` | Pasted text, used instead of a file or added to it |
| `detention`, `culverts`, `storm sewer`, `open channels`, `floodplain`, `streets` | `yes`, `no` or `auto` (default). `auto` decides from the project's calculations and from the report text. |
| `model checks = yes` | Re-run every saved design tool and storm network, and list each criterion that is not met |
| `names in report = yes` | Every pipe and structure in the model should be named in the report |
| `show satisfied = no` | `yes` also lists the checklist items the report already covers, with the passage that covers them |

## What it checks

1. **Report content.** Thirty-three items from IDCM §1.15, §4.1–4.10 and the SWQM. Each item applies only when its scope does: for example, the pond freeboard, 72-hour drain, outlet works, spillway and grading items apply only when the project has detention. An item counts as covered when every one of its keyword groups is found in the report. Otherwise it becomes a MISSING comment with the drafted wording. Rule checks on the text:
   - The Rational Method used on more than 160 acres gives a REVISE comment (§4.2.1(1)).
   - Retention without a basin plan gives a VERIFY comment (§4.10.1).
2. **Plan sheets.** The plan set must include a drainage area map, a grading plan, a storm sewer plan and profile, an erosion and sediment control plan, pond details when the project has detention, and details.
3. **Calculations.**
   - Every saved tool is re-run, and the compliance rows (FAILS, OVER CAPACITY, NOT CHECKED) become REVISE or VERIFY comments with their section.
   - Every storm network gets the §4.6 plan review (FAIL and REVIEW items).
   - Every storm network gets the HGL check (§4.6.7).
   - A tool that errors becomes a REVISE comment.
4. **Report against the model.** Pipes and structures in the model that are not named in the report become a VERIFY comment.

The output is a numbered comment table (No., Type, Topic, Comment, Reference, Found in, Response) ready for a comment-response letter. Supporting tables follow:
- the documents read
- the checklist with its evidence
- the plan sheets found
- the calculation findings
- the model elements missing from the report

Comments are ordered REVISE (criteria not met), then MISSING, then VERIFY.

## Decisions

- **D-220** Implemented as one premium design tool. Its output goes to Excel, HTML and PDF like every other tool, so the comment table can be pasted into a response letter.
- **D-221** Documents are read with a small built-in reader. It handles Flate streams, object streams, literal and hex strings, and ToUnicode font maps. No external library or service is used, so drawings and reports never leave the computer. Scanned pages have no text layer and are flagged with W-PC-002. Some PDFs come out partly: on the IDCM PDF, which has 204 pages and image pages, about 75 % of the text is extracted; MasterFlow, Word-exported and SWQM PDFs come out in full.
- **D-222** The checklist is keyword based, not semantic. Each item lists the phrases a reviewer would look for, and a missing phrase means "the reviewer will ask". `show satisfied = yes` shows the passage each covered item matched, so a false OK can be spotted.
- **D-223** Scope is decided from the project's saved tools and networks first, then from the words in the report. It can be overridden per topic.
- **D-224** Calculation findings reuse the drainage report builder's compliance collector and the §4.6 plan review, so the pre-check and the report always agree.
- **D-225** Plan sheets are found by their titles in the plan text.
- **D-226** Each name is matched as a whole token (P-1 does not match P-10).
- **D-227** Sections for calculation findings are inferred from the tool and check names.
