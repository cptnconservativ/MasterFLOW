# Quantity takeoff and opinion of probable cost

Takes the selected storm network and produces three tables:

1. **Opinion of probable cost**: one line per item with quantity, your unit price and amount, then subtotal,
   contingency and total. Items with no price are listed without cost, left out of the total, and named in a warning.
2. **Pipe and trench schedule**: per pipe, the pay item, length, outside diameter, trench width, depth at each
   end, excavation, bedding and backfill.
3. **Structure schedule**: per structure, kind (manhole, inlet, outlet), rim, lowest invert, depth and the depth
   over the base structure depth.

| Quantity | Rule |
|---|---|
| Pipe item | `Pipe <size> in <material>` (SI: mm). PVC where the size is 18 in. / 450 mm or smaller **and** n < 0.013 (IDCM §4.6.2(2) allows PVC only there); RCP otherwise. Elliptical pipes are `Pipe <rise> x <span> in RCP elliptical`. |
| Wall | `wall = auto` uses D/12 + 1 in. (an approximation of RCP Wall B); enter a number to override. |
| Trench | Vertical walls. Width = OD + `side clearance`. Depth at each end = rim − (invert − wall − `bedding depth`); the two ends are averaged. Ground is the straight line between the two rims. |
| Volumes | Excavation = width × average depth × length; bedding = width × bedding depth × length; backfill = excavation − bedding − pipe volume. CY in U.S. units, m³ in SI. |
| Structures | Kind from the structure's Kind text (outlet / inlet, CB, catch, grate, curb / otherwise manhole). Depth = rim − lowest connected invert + `structure base depth`. `<Kind> extra depth` = depth over `base structure depth`, per VF (m); outlets get none. |
| Prices | `[Unit prices]` table: Item, Unit, Price. Item names are matched without regard to case or extra spaces; a unit that differs from the quantity's unit is warned (W-QT-003). |

Messages: E-QT-001 no network; W-QT-001 a rim is missing (trench skipped); W-QT-002 structure depth unknown;
W-QT-003 unit mismatch; W-QT-004 unpriced items; I-QT-001 the assumptions used.

**The sample prices are examples only.** They are not Rapid City or any agency's bid prices; replace them with
your own bid tabulations.

Decision (D-058): the takeoff is planning-level and says so. It uses rims for ground because a ground profile
between structures is not available until LandXML surface profiles are imported; when they are, the trench depth
can integrate along the profile instead of averaging the two ends. Pavement removal and replacement, dewatering,
rock and traffic control are not estimated: they depend on site data the network does not hold, so they are left
for the engineer to add rather than guessed.
