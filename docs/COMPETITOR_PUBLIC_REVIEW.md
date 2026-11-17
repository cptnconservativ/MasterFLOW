# Flow Calc 365 — public information review

Reviewed 2026-11-17. Source IDs refer to [SOURCE_LOG.md](SOURCE_LOG.md). Text is paraphrased.
**Official claim** = stated by the vendor. **Observation** = seen in the installed app (S8).
**Inference** = our reasoning, not confirmed.

## Product positioning (official, S1)
- Open-channel flow calculations with Manning's equation; steady uniform flow.
- Irregular channel geometry, circular pipes and elliptical pipes.
- Aimed at civil engineers producing calculations and professional reports.

## Feature claims (official, S1)
Flow vs. depth rating tables · irregular channels · circular pipes · elliptical pipes · professional
reports · save reports as PDF · watermarks on reports · wizards · batch printing · export
calculations · expression builder · Windows 10/11 compatible.

## Topics the brief asked about

| Topic | Public information | Observation in app (S8) |
|---|---|---|
| Channel types | Irregular, circular pipe, elliptical pipe | Channel type buttons: Irregular, Circular, Elliptical |
| Regular shapes | Not listed | Rectangular, trapezoidal and triangular are *wizards that generate irregular station/elevation points* |
| Pipe shapes | Circular, elliptical | Circular (diameter) and elliptical (span/rise); "nominal pipe conversion (25 mm / 1 inch)" note |
| Solve-for variables | Not stated | n, slope, normal depth, discharge (one checked as the unknown) |
| Irregular workflow | Not described | Station/elevation/n grid with per-point description; add/delete/clear; copy/paste/import/export with selectable delimiters |
| Rating tables | Flow vs. depth | Rating tables are separate project items |
| Quick calculator | Not mentioned | Not found |
| Channel comparison | Not mentioned | Not found |
| Templates/wizards | "Useful wizards" | Standard-channel wizard + 7 factory street/roadside templates |
| Project organisation | Not described | Tree with folders, channels, rating tables, recycle bin, sort names, re-sort |
| Reports / PDF / watermark / batch | Claimed | Print Channel button; batch-printing toggle per item (report window not inspected, see limitations) |
| Expression/title builder | "Expression builder" | Item name templates with tokens such as %%Q2, %%ND2, %%S2, %%DIA0 rendered into the title |
| Units | Not stated | Imperial / Metric toggle |
| Licensing | Subscription, 1-month or 1-year, non-recurring, non-refundable; 1–3 licence bundles; licence transfer via online manager; free trial | App shows "Licensed" |
| Offline behaviour | Not stated; EULA mentions activation and hardware locking | Not tested |
| Windows versions | Windows 10/11 | Microsoft Store package |
| Help / support | Email support (named P.E.), YouTube tutorials | Info (i) button present |
| Published limitations | Steady uniform flow only; EULA disclaims accuracy | — |
| Civil 3D | Video "AutoCAD Civil 3D Integration with Flow Calc" | Not inspected |

## Contradictions to note
- The home page says distribution is exclusively through the Microsoft Store, but licences are sold on
  the vendor site; the Store listing says an account is required while the trial says "no registration".
  (Not material to MasterFlow; recorded for completeness.)

## Implications for MasterFlow
1. Parity floor: irregular sections, circular and elliptical pipes, four solve-for modes, rating tables,
   project tree, templates, title expressions, reports with watermark, batch printing, PDF, Imperial/Metric.
2. Differentiators available: first-class regular shapes (not only via generated points), quick
   calculator, alternative comparison, calculation trace and validation documentation, local-first
   with no licence activation, structured warnings, versioned open file format.
