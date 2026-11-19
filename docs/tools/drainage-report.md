# Drainage report builder (★ Premium)

Builds a complete storm water report from a template. Tool id `drainage-report` (license feature of the same name).
**Save…** writes PDF, Word (.docx) or HTML (and the preview tables to Excel).

## Template

Cover fields first (`key = value`): `title`, `subtitle`, `firm`, `engineer` (default: the project's Prepared by),
`license`, `date` (default today), `table of contents = yes|no`, `omit empty sections = yes|no`. Then a `[Report]` line
and the report itself. Everything under `[Report]` is narrative: lines with commas or `=` stay text.

| Line | Meaning |
|---|---|
| `# Heading`, `## Heading` | Section and subsection; body headings are numbered 1, 1.1 … and make the table of contents |
| text lines | Paragraph (consecutive lines join; a blank line ends it) |
| `- item` | Bullet |
| `{project.name}` `{project.number}` `{project.client}` `{project.preparedBy}` `{project.checkedBy}` `{firm}` `{engineer}` `{license}` `{date}` | Filled in; unknown placeholders are left as written and warned (W-RB-005) |
| `@tool <id> summary` | The tool's subheading, summary lines, up to three key tables (checks, criteria, summaries or results, at most 6 columns) and its warnings |
| `@tool <id> full` / `tables` / `charts` / `table "Title"` | Everything (summary, charts, all tables, messages), all tables, charts only, or one table whose title starts with the text |
| `@network all\|<name> summary` | Pipe summary (Q, capacity, velocity, HGL), least HGL-to-rim clearance and warnings |
| `@network all\|<name> full` | Profiles with the reading band, pipe and structure tables and messages, as in the network report |
| `@calculations summary\|full` | Channel and pipe calculations side by side (first 4, or all in groups of 6) |
| `@compliance` | Every OK / FAILS / NOT CHECKED / OVER CAPACITY cell in every tool table, and each network structure's HGL check. Runs of the same check collapse to one row ("12 items (A to B)") |
| `@certification <statement>` | Statement, 2 in. × 2 in. seal box, signature, engineer, license, date and firm |
| `@inputs` | Every tool's saved input, monospaced |
| `@pagebreak` | New page |
| `@appendices` | Everything after this is the appendix: landscape pages, headings not numbered |

Each tool's **saved input** in the project is re-run once (the input you last left in Design tools). A tool with no
saved input is skipped silently. A tool whose input has errors shows its messages instead of numbers (W-RB-003). A tool the
license does not cover is left out (W-RB-002). With `omit empty sections = yes` (default), a heading with nothing under
it is dropped, so one template serves projects with and without detention, culverts, and so on.

The default template follows IDCM §1.15 (storm water report under a PE's supervision) and §4.1.8 (design inputs,
outputs, assumptions and calculations). Firms keep their own template as text and paste it in.

## Output

- **PDF**: cover (logo from Project → Logo), contents with page numbers, portrait body, landscape appendices, one
  page count through the whole document, watermark and footer from the project report settings.
- **Word (.docx)**: real Title / Heading 1 / Heading 2 styles, a TOC field (Word fills it on opening; if it asks,
  choose Update), tables with repeating header rows, charts and profiles as images, page-number footer, landscape
  section for the appendices. Everything stays editable, so the narrative can be finished in Word.
- **HTML**: one self-contained page with linked contents; charts and profiles as SVG.

## Decisions

- D-075: a text template with directives rather than a Word template with merge fields: it lives in the project
  file, diffs cleanly and needs no Office on the machine. Firms that want their letterhead finish the .docx in Word.
- D-076: the report re-runs saved tool inputs instead of storing results, so a report can never show numbers that
  no longer match the inputs beside them.
- D-077: portrait body, landscape appendix: narrative and summaries read as a report, wide calculation tables
  keep readable columns.
