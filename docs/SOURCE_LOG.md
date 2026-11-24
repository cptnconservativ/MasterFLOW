# Source log

Engineering sources used for MasterFlow.
Reviewed 2026-11-17 unless noted.

| # | Source | Type | Accessed | Notes |
|---|---|---|---|---|
| R1 | V. T. Chow, *Open-Channel Hydraulics*, McGraw-Hill, 1959 | Textbook | from knowledge | Geometric elements (Table 2-1), Manning n ranges (Table 5-6), Example 6-2 |
| R2 | R. P. Brent, *Algorithms for Minimization without Derivatives*, 1973 | Numerical methods | from knowledge | Root-finding algorithm |
| R3 | USACE EM 1110-2-1601, *Hydraulic Design of Flood Control Channels* | Federal guidance | from knowledge | Near-critical Froude range 0.86–1.13 |
| R4 | FHWA HDS-4, *Introduction to Highway Hydraulics* | Federal guidance | from knowledge | k = 1.486 convention, Manning applications |
| R5 | ASTM C507-22, *Standard Specification for Reinforced Concrete Elliptical Culvert, Storm Drain, and Sewer Pipe*, public preview: https://cdn.standards.iteh.ai/samples/113175/927b4b8985d24cdabf4d1b48f74490b1/ASTM-C507-22.pdf | Standard (preview) | 2026-11-17 | Fig. 5 geometry: rise, span, R1, R2 and waterway area for 24 HE sizes; VE availability |
| R6 | Rinker Materials, elliptical pipe brochure: https://www.rinkerpipe.com/wp-content/uploads/EllipticalBrochure.pdf | Manufacturer | 2026-11-17 | Cross-check of sizes and areas |
| R7 | ACPA fill height table, elliptical and arch, 10th ed.: https://resources.concretepipe.org/hubfs/Fill%20Height%20Table%20-%20Elliptical%20and%20Arch%20-%2010th%20edition%20-1.pdf | Industry association | 2026-11-17 | Cross-check of rise × span designations |

Sources R1–R4 were cited from engineering knowledge, not re-read online in this session; the
specific example (Chow Ex. 6-2, yn ≈ 3.36 ft) was confirmed by an independent calculation in
`validation/hand_calcs.py` (3.3610 ft).
