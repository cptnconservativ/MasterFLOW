# Sanitary sewer structural check at designed depths

Tool id `sanitary-deep-structural` (★ Premium; U.S. units). Runs the sanitary sewer design (`sanitary-network`) and
checks each sewer at its designed size and cover with the methods of the pipe structural tool (`pipe-structural`).
Deep trunk sewers (15 ft of cover and more by default) are listed in the summary.

## Input

The structural keys, then the sanitary sewer design input:

| Key | Default | Meaning |
|---|---|---|
| `material` | auto | `auto` (PVC to 36 in., reinforced concrete above), `PVC`, `HDPE` or `RCP` |
| `deflection limit` | 5 % | Ten States §33.85 |
| `embedment`, `compaction` | gravel, moderate | Howard (1977) E′ (2,000 psi); `E′` replaces it |
| `unit weight`, `live load`, `LLDF`, `DL` | 120 pcf, HL-93, 1.15, 1.0 | as `pipe-structural` |
| `installation`, `BfLL` | Type 2, 1.1 | reinforced concrete Standard Installation and live-load bedding factor |
| `deep cover` | 15 ft | sewers at or below this cover are listed as deep |

`[Materials]` overrides single sewers: Pipe, Material, PS (psi), Installation, Wall (in.).

## Method

- **Cover** at each manhole = rim − (invert + ID + wall), to the outside top of the pipe. The ground between manholes
  is the straight line between the rims, so the ends govern. Earth load is greatest at the deep end and live load at
  the shallow end; both are checked and the worse governs.
- **PVC** (ASTM D3034 for 6–15 in., F679 for 18–36 in.; ODs from those standards): SDR 35 / PS 46 is tried first,
  then SDR 26 / PS 115. **HDPE**: AASHTO M294 minimum stiffness by size. Modified Iowa deflection
  Δy/D = (DL·K·Pe + K·PL)/(0.149·PS + 0.061·E′), K = 0.1, with the prism earth pressure and the HL-93 wheel pressure.
- **Reinforced concrete** (Rinker precast, ASTM C76 wall B = ID/12 + 1 in.): indirect design
  D0.01 = [(WE + WF)/Bfe + WL/BfLL]/Di and the least ASTM C76 class.
- The live load is neglected above 8 ft of cover when the cover exceeds the pipe OD (AASHTO LRFD 3.6.1.2.6).
- The governing pipes are written as input for the **Pipe structural design** tool (`pipe-structural input.txt`), so
  a single pipe can be studied there.

## Decisions

- **D-733** Cover is measured from the manhole rims (the only ground the design knows) to the outside top of pipe;
  the deep end governs earth load and the shallow end live load. The design's own cover check stays as it is.
- **D-734** `material = auto` uses PVC up to 36 in. (the ASTM F679 range) and reinforced concrete above. PVC starts at
  SDR 35 and steps up to SDR 26 / PS 115 only where SDR 35 exceeds the limit (I-SD-001), the usual Rapid City
  practice of specifying SDR 26 for deep sewers.
- **D-735** The flexible pipe limit is 5 %, the Ten States §33.85 acceptance deflection; the IDCM sets none.
- **D-736** Reinforced concrete defaults to Rinker precast wall B on a Type 2 installation; BfLL stays at the
  conservative 1.1 of D-610 until the ACPA table value is entered.
- **D-737** The design's hydraulic warnings are not repeated here (I-SD-005 says how many there are); run the sanitary
  design tool for them.

## Messages

| Code | Meaning |
|---|---|
| E-SD-001 … 006 | Live load, material, parameter, installation, embedment or `[Materials]` row unreadable |
| W-SD-001 | Flexible pipe deflects over the limit even with the stiffest option |
| W-SD-002 | Reinforced concrete D-load above Class V |
| W-SD-003 | Under 1 ft of cover |
| W-SD-004 | Size not listed for PVC/HDPE: checked as reinforced concrete |
| W-SD-005 | Pipe not buried at one end |
| W-SD-006 | No rims: sewer not checked |
| I-SD-001 … 005 | SDR 26 needed, very deep flexible pipe, cover basis, pipe-structural input written, design warnings |

## Validation

- 8 in. PVC SDR 35 at 20 ft, gravel moderate (E′ 2,000): Δy/D = 100·0.1·16.667/(0.149·46 + 0.061·2,000) = 1.29 %.
- The same pipe in silt (E′ 400): SDR 35 gives 5.33 %, so SDR 26 is chosen (4.01 %); at 25 ft SDR 26 gives 5.01 % and
  is flagged.
- 48 in. RCP at 22 ft gives the same D-load and class as the pipe structural tool; the helper reproduces that tool's
  hand check (24 in., 3 ft, Type 2: 993 lb/ft/ft, Class II).
- The written `pipe-structural input.txt` runs cleanly in the pipe structural tool.
