# Pipe structural design: RCP D-load class and flexible pipe deflection

Tool id `pipe-structural` (free). Checks buried storm pipe for earth and traffic load.

## Input

| Key | Meaning |
|---|---|
| `unit weight` | Soil over the pipe (default 120 pcf / 18.85 kN/m³) |
| `live load` | `HL-93` (AASHTO design truck wheel) or `none` |
| `LLDF` | Live-load distribution factor: 1.15 select granular fill, 1.0 other soils (AASHTO LRFD 3.6.1.2.6) |
| `DL` | Deflection lag factor on the prism earth load (default 1.0) |

`[Rigid]`: Name, ID, Cover (over the top of pipe), Installation (Standard Installation Type 1–4), Wall (blank = ASTM C76
wall B), BfLL (blank = 1.1).

`[Flexible]`: Name, Material (HDPE, PP, PVC, CMP, steel, aluminum), ID, OD, Cover, PS (pipe stiffness, ASTM D2412),
Soil, Compaction, E′ (overrides Howard), Limit (%, default 5).

## Method

Rigid pipe, indirect design (AASHTO LRFD §12.10.4.3, ACPA *Concrete Pipe Design Manual*):

- Earth load WE = VAF·w·[H + Do(4 − π)/8]·Do, VAF = 1.35, 1.40, 1.40, 1.45 for Types 1–4.
- Fluid load WF = 62.4·π·Di²/4 (pipe full).
- Live load: 16-kip dual wheel on a 20 × 10 in. contact area, dynamic allowance IM = 33(1 − 0.125H) %, multiple presence 1.2,
  spread 20/12 + LLDF·H across and 10/12 + LLDF·H along the traffic. The two wheels of an axle (6 ft apart) combine once the
  spread is wider than 6 ft. WL = w·L·min(a, Bc)/Le with Le = L + 1.75·(3/4)·Bc. Neglected above 8 ft of cover when the
  cover exceeds the pipe OD (LRFD 3.6.1.2.6).
- D0.01 = [(WE + WF)/Bfe + WL/BfLL]/Di (Di in ft), F.S. 1.0; class = least ASTM C76 class (I 800, II 1000, III 1350,
  IV 2000, V 3000 lb/ft/ft; C76M 40, 50, 65, 100, 140 N/m/mm) at or above it.
- Bfe from the ACPA Standard Installation table at 12, 24, 36, 72, 144 in., interpolated on the diameter.

Flexible pipe, Modified Iowa formula: Δy/D = (DL·K·Pe + K·PL)/(0.149·PS + 0.061·E′), K = 0.1, Pe = w·H (prism),
PL = the live-load pressure at the crown. E′ from Howard (1977), USBR:

| Embedment | Dumped | Slight (< 85 %) | Moderate (85–95 %) | High (> 95 %) |
|---|---|---|---|---|
| Crushed rock | 1000 | 3000 | 3000 | 3000 |
| Gravel, sand, little or no fines | 200 | 1000 | 2000 | 3000 |
| Silty/clayey sand or gravel (> 12 % fines) | 100 | 400 | 1000 | 2000 |
| Silt, lean clay (LL < 50) | 50 | 200 | 400 | 1000 |
| Fat clay, elastic silt (LL > 50) | 0 | 0 | 0 | 0 |

Hand check: 24 in. RCP, wall B (Do = 2.5 ft), 3 ft cover, Type 2, 120 pcf: WE = 1.40·120·(3 + 0.268)·2.5 = 1,373 lb/ft;
WF = 196 lb/ft; wheel IM = 20.6 %, a = 4.28 ft, b = 5.12 ft, w = 1,056 psf, Le = 8.40 ft, WL = 1,609 lb/ft;
D = (1,569/3.0 + 1,609/1.1)/2 = 993 lb/ft/ft → Class II.

## Decisions

- D-610: BfLL defaults to 1.1, the low end of the ACPA HS20 live-load bedding factor table, so the D-load is on the safe
  side until the table value is entered. BfLL is never taken above Bfe (ACPA).
- D-611: The pipe axis is taken across the traffic with one axle; tandem and multiple-lane loads are not combined.
- D-612: Deflection uses DL = 1.0 with the prism load (the usual practice with prism load); set `DL = 1.5` for
  Spangler's original lag factor.
- D-613: Allowable deflection defaults to 5 % for every flexible material; enter the agency or manufacturer limit.

## Limits

Direct design, buckling, wall thrust and strain of flexible pipe, and construction loads are not checked.
