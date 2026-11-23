# Review comment tracker and response letter

Tool id `review-comments` (**premium**). This tool keeps the plan review comments from the City or another agency in
one place: each comment, the response to it, and its status, across review rounds. The tracker is saved with the
project. It also writes the response-to-comments letter.

## Input

- **Header lines:**
  - `project`, `submittal`, `agency`
  - `round`: the current review round
  - `response date`
  - `signed`: closing line of the letter
  - `include plan review`: yes/no
- **`[Comments]` table.** Columns are separated by **tabs**, because comments often contain commas:
  - ID
  - Round
  - Reviewer
  - Sheet
  - Item: a structure or pipe name, a detail, or blank
  - Comment
  - Response
  - Status: `open`, `responded`, `closed` (the reviewer accepted it) or `deferred`

A blank status means `responded` when there is a response, and `open` when there is not.

## Output

- **Comment–response matrix.** Comments are sorted by round, then by ID in natural order (2 before 10).
  - The **current result** column applies when Item names a structure or pipe of the selected network. For a
    structure it shows HGL, EGL and rim − HGL. For a pipe it shows Q, V, the HGL at both ends and the regime.
  - These values come from the last solve, so a response can cite current numbers.
  - Running the tracker again after a change shows whether the fix holds.
- **Open items.** A comment is open when its status is open, or when it has no response unless it is deferred.
  Comments from earlier rounds are marked *carried over*.
- **Counts by reviewer and by round.**
- **Warnings:**
  - repeated IDs
  - unknown statuses
  - comments marked responded or closed with no response
  - comments still open from earlier rounds
  - everything still needing a response before resubmittal
- **Plan review findings.** With `include plan review = yes`, the storm network plan review (IDCM §4.6) runs with its
  saved input. Each FAIL or REVIEW check becomes an open item PR-1, PR-2 and so on, unless a comment already tracks the
  same item and check. The items disappear once the network passes.
- **Response letter (Save… → PDF or Word).**
  - The cover gives project, submittal, agency, round and date.
  - Each reviewer gets a section listing the comments that are not yet closed, with their responses.
  - Earlier closed comments are summarised.
  - The full matrix is attached as an appendix.

## Decisions

- **D-140** The tracker is the tool's input text. It is saved with the project like every tool input, so it needs no
  separate database or file. Rounds are a column rather than separate files, so the whole history stays in one table.
- **D-141** Comments link to the model by element name, and the current values are recomputed on each run rather
  than stored. A response is checked against the design as it is now.
- **D-142** Plan review findings are added as tracked items, not merged into reviewer comments. They are MasterFlow's
  own checks, and they clear automatically once the network passes.

## Validation

- The sample counts correctly: 5 comments, 1 open, 2 responded, 1 closed, 1 deferred.
- The current values shown for MH-2 and P1 equal the network solution.
- Rounds carry over, and a missing response is flagged.
- Natural ID order is kept.
- Plan review FAIL items are added from a network whose HGL is near the rims.
- The letter renders to HTML and Word.
