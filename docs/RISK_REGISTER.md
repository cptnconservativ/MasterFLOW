# Risk register

| ID | Risk | Likelihood | Impact | Mitigation | Owner | Status |
|---|---|---|---|---|---|---|
| R-01 | An existing MasterFlow repository exists elsewhere and diverges | M | M | Ask Kyle; connect GitHub; reconcile before Phase C | Kyle/Claude | Open |
| R-02 | Code is lost because no remote repository exists | M | H | Copy of repo in the project files folder; push to GitHub once connected | Kyle | Open |
| R-03 | Not yet run on Windows hardware (DPI, dialogs, fonts) | M | M | Run on Kyle's PC; Windows CI | Claude | Open |
| R-04 | Pipe flow near crown has two normal depths; wrong root chosen | M | H | Report both roots; error when Q > Qmax | Claude | Planned (C2) |
| R-05 | Composite roughness method differs from HEC-RAS | M | H | Implement and document both methods; HEC-RAS comparison set | Claude | Planned (D3) |
| R-06 | PDF library licence (QuestPDF revenue threshold) | M | L | Decide in Phase F; HTML→PDF works now | Kyle | Open |
| R-07 | Remote walkthrough of Flow Calc limited (typing not registered) | H | L | Use public info and examples; Kyle can run controlled cases | Claude | Open |
| R-08 | Users mis-enter slope as ft/ft into the % field | M | M | "%" label; very-mild-slope warning; consider showing both | Claude | Mitigated |
| R-09 | Manning n choice dominates error | H | H | n guidance table with sources; report states n is engineer's choice | Kyle | Mitigated |
| R-10 | Similarity claims (look and feel) | L | M | Original design, wording, icons; no copied assets | Claude | Mitigated |
