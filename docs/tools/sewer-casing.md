# Sewer casing and trenchless crossing

Tool id `sewer-casing` (free, U.S. units). Sizes the casing pipe for a sewer main bored or jacked under a street,
railroad or channel by Rapid City IDCM (2012) §3.12.11, lays out the casing chocks, and runs two installation checks
the IDCM leaves to the design: whether the bore can hold a gravity sewer's grade inside the casing, and the jacking
force against the casing's axial capacity. With `protection = cap` it sizes the §3.12.12 concrete pipe cap for a
stream crossing instead.

## Input

Keys: `carrier` (description), `carrier OD` and `carrier ID` (in., barrel), `bell OD` (in., largest OD at the
restrained joint, from the joint maker), `joint length` (20 ft), `casing length` (ft), `casing material` (`steel` or
`RCP`), `casing yield` (35,000 psi), `casing OD` / `casing ID` / `casing wall` (in., optional; leave blank to size),
`clearance` (6 in.), `max chock spacing` (10 ft), `chock offset` (1 ft from the joint), `min chock height` (0.75 in.),
`upstream invert` (ft, carrier invert at the upstream casing end), `carrier slope` (%), `bore accuracy` (% of length,
default 1.0), `skin friction` (200 psf), `face pressure` (0 psf), `soil resistivity` (ohm-cm), `restrained joints`
(yes). Pipe cap: `protection = cap`, `carrier OD`, `crossing length`.

## Method

- Required casing ID = bell OD + 6 in. (§3.12.11 item 3).
- Steel: the smallest standard OD (8.625 to 72 in.) whose ID with the §3.12.11 item 2 minimum wall (or a larger wall
  you give) clears that. RCP: the next jacking pipe ID, ASTM C76 wall B (D/12 + 1 in.) for the OD.
- Chocks: one each side of every joint (at the chock offset), one at mid-length, and more in even numbers so no gap
  exceeds 10 ft (§3.12.11 item 4). Total = chocks per length × carrier lengths in the casing.
- Grade: with the carrier centered, the room above and below the bell is (casing ID − bell OD)/2; less the lowest chock
  runner, that is how far the casing can stray before custom-height chocks can no longer hold the carrier on grade.
  The expected deviation is the bore accuracy × length; the longest bore that holds grade is room / accuracy. When it
  is short, the casing that would hold grade is named (W-CS-005).
- Jacking force = skin friction × π·OD·L + face pressure × face area, against ½·Fy·As for steel (factor of safety 2 on
  yield). RCP: compare with the jacking pipe maker's allowable load.
- Casing inverts = carrier invert − carrier wall − (casing ID − carrier OD)/2, at both ends from the carrier slope.
- Pipe cap (§3.12.12): width 3·D, from the spring line to 6 in. above the crown, reinforced, 6 in. minimum; concrete
  per ft = width × height − half the pipe area.

Worked check (sample): bell 13.60 in. + 6 = 19.60 in. A 20 in. casing at 0.344 in. has 19.31 in. ID (too small); 22 in.
at 0.375 in. has 21.25 in. Room (21.25 − 13.60)/2 = 3.83 in., less 0.75 in. = 3.08 in.; at 0.2 % that holds 3.075 /
12 / 0.002 = 128 ft (the bore is 120 ft). Jacking 200 × π × 22/12 × 120 = 138,200 lb = 69 tons against 0.5 × 35,000 ×
25.5 in.² = 223 tons. Chocks 3 per 20 ft length × 6 = 18.

## Decisions

- **D-740** The wall table is used as printed in §3.12.11. It gives 0.500 in. for 28 and 30 in. casings, where the
  water main table (§3.9.14) gives 0.469 in.; the sewer value is used and the difference is noted (I-CS-003). Sizes
  between rows take the next larger row. The table stops at 42 in.: larger casings use the 42 in. value as a floor and
  are flagged (W-CS-004) for a structural design and the crossing owner (AREMA for railroads).
- **D-741** Grade tolerance. The IDCM does not address it. Default bore accuracy 1 % of length, a commonly quoted
  figure for unguided auger boring; guided methods (pilot tube, guided auger boring) do much better and are entered
  as a smaller value. Minimum chock runner 0.75 in. Both are inputs.
- **D-742** Jacking. Skin friction 200 psf default (published values for steel casing range roughly 100 to 500 psf
  depending on soil and lubrication; site specific), no face pressure for auger boring. Capacity = ½ × yield × steel
  area. Earth and live load on the casing are not checked here (use `pipe-structural`).
- **D-743** Corrosion: §3.12.11 item 6 makes corrosion protection part of every casing design, so it is always listed;
  the soil resistivity, if given, is classed (< 1,000 very corrosive, 1,000–2,000 corrosive, 2,000–5,000 moderately,
  5,000–10,000 mildly, above that slightly or not corrosive).
- **D-744** The §3.12.12 pipe cap is sized as the alternative to a casing at stream crossings; scour below the cap is
  left to the scour tools.

## Messages

| Code | Meaning |
|---|---|
| E-CS-001 | SI project |
| E-CS-002 | A required value is missing |
| E-CS-003 | A value is out of range |
| W-CS-001 | Casing ID less than bell OD + 6 in. |
| W-CS-002 | Casing wall below the IDCM minimum |
| W-CS-003 | Steel yield below 35,000 psi |
| W-CS-004 | Casing above the table (42 in.) or the RCP list |
| W-CS-005 | The bore cannot hold the sewer grade inside the casing (the casing that would is named) |
| W-CS-006 | Jacking force above the casing capacity |
| W-CS-007 | Chock spacing above 10 ft |
| W-CS-008 | Joints not restrained |
| I-CS-001 … 005 | IDCM chock, seal and corrosion items; soil resistivity; 28/30 in. wall note; loads not checked; pipe cap notes |
