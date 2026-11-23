# Institutional memory search (★ Premium)

Tool id `memory-search` (license feature of the same name). Ask in plain words how something was handled before
("how did we handle a sag inlet on a 6% street last time?") and it searches the firm's past work in the folders you
name.

## Input

| Key | Meaning |
|---|---|
| `folders` | Folders to search with their subfolders; several separated by `;` |
| `question` | The question, in plain words |
| `top` | Results shown (default 15) |
| `kinds` | `all` (default) or any of `project`, `report`, `review`, `calc`, `other` |
| `since` | Only files modified in or after this year |

## What is searched

- **MasterFlow projects (.mfproj)**: the project information, every design tool's saved input (with its comments),
  every storm network and every channel calculation, each as its own passage.
- **PDF and Word (.docx)** documents, read by MasterFlow's built-in text reader (no Office or Acrobat needed; scanned
  pages without a text layer cannot be searched), and **.txt / .md** files. Pages are split into passages of about
  120–200 words at paragraph breaks.
- The **kind** comes from the file name, then the first page: review (review, comments, redline, response,
  resubmittal), calc, report, other. The **project** is the nearest folder named like a job ("24-031 …").

Extracted text is cached in `%LOCALAPPDATA%\MasterFlow\memory-index`, keyed by path, size and date, so only new or
changed files are read again. On the project-files folder (55 files, including the 33 MB SWQM) the first search took
about 18 s and later ones under 2 s.

## Ranking

BM25 (k1 = 1.2, b = 0.75) over the passages, with:
- civil engineering synonyms (sag = sump = low point; inlet = catch basin = CB = grate = curb opening; street =
  roadway = gutter; pond = detention = basin; …) at 0.6 weight; within one idea of the question the best word
  counts fully and other synonyms a quarter;
- numbers in their other forms (6 % also matches 6.0 and 0.06);
- a coverage factor (passages answering more of the question's ideas rank higher) and a proximity bonus when the
  ideas sit within a dozen words.

## Output

- **Results**: rank, score, project, kind, file, page or tool, date, the best passage with the matched words in
  [brackets], and the full path.
- **By file**: the files with matches, how many passages and the best score.

## Decisions

- D-290: local keyword search (BM25 + synonyms) rather than an AI or cloud service: project files and review letters
  stay on the firm's computers, it needs no subscription or internet, and every result shows exactly why it matched.
- D-291: MasterFlow projects are searched through their saved tool inputs, so the engineer's own notes and the actual
  design values (6.0 % slope, 50 % clogging) are found, not just the report text.
- D-292: the text cache is keyed by path, size and modified date, so a large archive is read once.
