# Design tools

**Design tools…** (main window) opens one window for every drainage design tool. Each tool:

- takes text input: `key = value` lines, then tables under `[Section]` headings (tab- or comma-separated;
  the first line of a table is its header; `#` starts a comment). **Sample** fills in a worked example,
  using the selected storm network's structure names where it can;
- shows its result as aligned text, with every error, warning and assumption listed under Messages.
  A part that cannot be computed is reported, never estimated;
- copies (text plus tab-separated tables), saves an HTML report (print to PDF) or an Excel workbook;
- may offer **Apply to network**, which writes its result into the selected storm network and re-solves it.
  Nothing changes in the network until you press it.

The last input of each tool is saved with the project. Tools are discovered automatically (any class
implementing `IDesignTool` in MasterFlow.Application), so each tool lives in its own file with its own
tests; every tool's sample input is run by the test suite and must finish without errors.

| Tool | Source | Doc |
|---|---|---|
| Rational method | Rapid City IDCM 2012 §4.2 | [rational-method.md](rational-method.md) |
