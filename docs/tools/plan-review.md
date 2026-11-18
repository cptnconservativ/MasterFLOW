# Storm network plan review (IDCM 2012 §4.6 checklist)

Reviews the selected, solved storm network and lists one row per finding: result (FAIL, REVIEW, OK), the
check, the IDCM section, the pipe or structure, the value and the limit.

| Check | IDCM | Rule |
|---|---|---|
| Minimum pipe size | §4.6.2(2) | 12 in.; 18 in. when the system has bends (`bends = yes`, or detected from structure coordinates when the deflection at a structure exceeds `bend angle`) |
| Material and n | §4.6.2(2) | RCP above 18 in. with n ≥ 0.013; PVC n ≥ 0.010 |
| Manhole/inlet spacing | §4.6.3 | ≤ 400 ft for pipes ≤ 30 in.; ≤ 600 ft above |
| Larger pipe into smaller | §4.6.6(2) | Never |
| Crown matching | §4.6.6(3) | At a size increase, the outgoing crown not above the incoming crown; matching the 0.8D points is reported as REVIEW (allowed for minimal fall) |
| Outlet invert | §4.6.6(3) | Outgoing invert not above an incoming invert |
| Velocity progression | §4.6.6(1) | A drop of more than 10 % from the incoming pipe is flagged for review |
| Slope below critical | §4.6.6(4) | Steep pipes flagged for review |
| Minimum velocity | §4.6.2(3)a | 2.5 fps at the 2-yr (minor storm) flow. With no 2-yr flow for a pipe the check is REVIEW (not checked), and the design-flow velocity is listed on a separate information row; it is never reported as the 2-yr result |
| Maximum velocity | §4.6.2(3)b, Table 4-13 | 20 fps on collectors and mains; inlet laterals (pipes with nothing upstream) have no limit |
| Cover | §4.6.2(2) | Rim − crown at each end ≥ `minimum cover` (the manufacturer's value; default 1.5 ft). A structure with no rim gives REVIEW (not checked) |
| HGL | §4.6.7 | At least 1 ft below the rim. No rim gives REVIEW (not checked) |

Decision (D-057): the review reads only the solved network, so it never re-computes hydraulics differently
from the HGL report. Cover uses rims because ground between structures is unknown until LandXML profiles (H5)
are imported. Checks that need judgment (velocity drop, steep slope, 0.8D matching) are REVIEW, not FAIL.
