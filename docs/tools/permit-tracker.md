# Permit and approval tracker (★ Premium)

Tool id `permit-tracker` (license feature of the same name). Lists the permits and approvals a project needs from a few
facts, schedules them by the critical path method and, as dates come in, tracks them to the construction start.

## Project facts

| Key | Adds |
|---|---|
| (always) | Storm water report and drainage plan (IDCM §1.15); grading permit; construction start milestone |
| `subdivision = yes` | Preliminary subdivision plan; development engineering plans; final plat |
| `public improvements = yes` | Development engineering plans |
| `public water or sewer = yes` | SD DANR water and sewer main plan approval |
| `disturbed area` ≥ 1 ac | SWPPP; SD DANR construction general permit (NOI) |
| `in floodplain = yes` | Floodplain development permit; with `map revision = yes`, a FEMA CLOMR |
| `waters or wetlands = yes` | USACE Section 404 permit; SD DANR Section 401 certification |
| `state highway access = yes` | SDDOT access / right-of-way permit |
| `inside city = no` | Pennington County takes the City's place |

`start` is the day submittal work begins; `today` (default: the computer's date) is the status date.

The catalog is a typical Rapid City / South Dakota set with typical durations, not a legal determination: confirm
the requirements, durations and fees with each agency. Under `[Permits]` (columns Id, Name, Agency, Depends on, Prep,
Review, Cycles, Resubmit, Fee, Submitted, Approved, Note), a row with a catalog Id changes that item (blank cells
keep the catalog value) and a new Id adds an item that construction start then waits for.

## Schedule

Each item takes Prep + Cycles × Review + (Cycles − 1) × Resubmit calendar days after the items it depends on. An
Approved date fixes its finish; a Submitted date starts its review from that day (if the forecast approval has passed,
the finish moves to today and W-PT-002 says to follow up). Nothing not yet submitted starts before today. The forward
and backward passes give each item's start, finish and float; float of items that lead to construction start is
measured to the start (the final plat, which follows construction of public improvements, is scheduled after it).

## Output

- **Permits and approvals**: agency, why needed, dependencies, durations, start, finish, float, critical, status
  (APPROVED, IN REVIEW, READY, WAITING, NOT STARTED), fee, reference.
- **Critical path** to construction start and the earliest start date.
- **Next actions**: items ready to submit now (critical first) and reviews past their forecast.
- A schedule chart (one bar per item, the critical path solid, today marked).

Review comments are handled by the review comment tracker; the plan-review pre-checker anticipates them before
submittal.

## Decisions

- D-280: a rules catalog keyed to a few yes/no facts rather than a questionnaire per agency: a site feasibility run (or
  the engineer) can answer the facts in a minute, and the list is editable where a project differs.
- D-281: calendar days and review cycles (prep, review, resubmit) instead of single durations, because resubmittal
  rounds are what move permit schedules.
- D-282: no fee amounts in the catalog: fee schedules change yearly, so fees are entered from the current schedule and
  totalled, rather than shown from a stale table.
