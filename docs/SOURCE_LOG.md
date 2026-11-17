# Source log

All sources reviewed for MasterFlow discovery. "Official" = published by the Flow Calc vendor.
Reviewed 2026-11-17 unless noted.

| # | Source | Type | Accessed | Notes |
|---|---|---|---|---|
| S1 | https://www.flowcalc365.com/ | Official (product home, single page with #pricing, #license-manager, #tutorials, #company anchors) | 2026-11-17 | Feature list, licensing summary, support contact, tutorial links |
| S2 | https://www.flowcalc365.com/eula.html | Official (EULA) | 2026-11-17 | Licence terms; prohibits reverse engineering |
| S3 | https://www.flowcalc365.com/privacypolicy.html | Official (privacy) | 2026-11-17 | Data collected for licensing; files stay local |
| S4 | https://www.flowcalc365.com/activation/index.php | Official (licence manager) | 2026-11-17 | Existing-customer key management |
| S5 | https://www.flowcalc365.com/createorder.php | Official (order) | 2026-11-17 | Returned "Invalid License Plan" without a plan parameter; prices not visible |
| S6 | https://apps.microsoft.com/detail/9nnz8j1ccr08 | Official listing on third-party store (Microsoft Store) | 2026-11-17 | Body requires JavaScript; only metadata readable |
| S7 | https://www.youtube.com/@FlowCalc365 | Official channel on third-party platform | 2026-11-17 | Video list not retrievable (stub page; RSS blocked by robots.txt) |
| S8 | Installed Flow Calc app on the user's Windows PC (displays "Flow Calc 2026", version 26.8.23.0, "Licensed") | Direct observation through the normal UI | 2026-11-17 | Built-in Example Project inspected; see FLOWCALC_UI_INVENTORY.md |
| R1 | V. T. Chow, *Open-Channel Hydraulics*, McGraw-Hill, 1959 | Textbook | from knowledge | Geometric elements (Table 2-1), Manning n ranges (Table 5-6), Example 6-2 |
| R2 | R. P. Brent, *Algorithms for Minimization without Derivatives*, 1973 | Numerical methods | from knowledge | Root-finding algorithm |
| R3 | USACE EM 1110-2-1601, *Hydraulic Design of Flood Control Channels* | Federal guidance | from knowledge | Near-critical Froude range 0.86–1.13 |
| R4 | FHWA HDS-4, *Introduction to Highway Hydraulics* | Federal guidance | from knowledge | k = 1.486 convention, Manning applications |

Not accessed: jrhamiltonenterprises.com (publisher site, different domain), individual YouTube videos.
Sources R1–R4 were cited from engineering knowledge, not re-read online in this session; the
specific example (Chow Ex. 6-2, yn ≈ 3.36 ft) was confirmed by an independent calculation in
`validation/hand_calcs.py` (3.3610 ft).
