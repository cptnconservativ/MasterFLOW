# Bridge deck drainage (HEC-21): deck drain spacing

Tool id `deck-drainage` (free, U.S. units). Lays out deck drains along one gutter of a bridge by FHWA HEC-21, *Design of
Bridge Deck Drainage* (1993): how far the deck can go before the gutter reaches the allowable spread, how much each
grate or scupper catches, how far apart the drains must be, and how much flow leaves the end of the deck.

## Input

| Key | Meaning |
|---|---|
| `bridge length` | Length along the gutter, ft |
| `width` | Deck width draining to this gutter (crown to curb), ft |
| `Sx`, `S` | Cross slope and longitudinal slope |
| `n`, `C`, `i` | Manning n (0.016), runoff coefficient (0.9), intensity in/hr (HEC-21: 5-min duration) |
| `T allow` | Allowable spread, ft (usually the shoulder width) |
| `approach Q` | Flow arriving on the deck from the uphill approach, cfs |
| `drain` | `grate` (give `L`, `W`, `Vo`) or `scupper` (give `D`; taken as a D × D opening, Vo 3 ft/s unless given) |
| `clogging` | Percent of the drain length lost to debris (default 50 for scuppers, 0 for grates) |

## Method

- Gutter capacity at the allowable spread: Qallow = (0.56/n)·Sx^1.67·S^0.5·T^2.67; velocity (1.12/n)·S^0.5·Sx^0.67·T^0.67.
- Length to the first drain: L1 = 43,560·(Qallow − Qapproach)/(C·i·W).
- Drain efficiency on grade (HEC-22 Eqs. 4-18 to 4-21, the HEC-12 equations HEC-21 uses): Eo = 1 − (1 − W/T)^2.67,
  Rf = 1 − 0.09·(V − Vo), Rs = 1/(1 + 0.15·V^1.8/(Sx·L^2.3)), E = Rf·Eo + Rs·(1 − Eo), with L shortened by clogging.
- Later drains: L = 43,560·E·Qallow/(C·i·W).
- Drains are placed from the start of the deck until the end; the flow and spread at the end of the deck are reported.
  If L1 is longer than the bridge, no drains are needed and HEC-21's advice to carry the flow off the bridge is shown.

## Check

Sample: 26 ft drained width, Sx 0.02, S 0.01, n 0.016, C 0.9, i 6 in/hr, T 8 ft. Qallow = 1.312 cfs, L1 = 407 ft;
2 ft × 1.5 ft grate 50 % clogged: E = 0.446, spacing 182 ft; two grates on a 600 ft deck and 0.76 cfs off the end.
The tests reproduce every step by hand.

## Decisions

- **D-430** HEC-21's scupper charts were not available as text; a round scupper is treated as a D × D grate with a low
  splash-over velocity (3 ft/s), which gives the low on-grade efficiency HEC-21 reports for small scuppers. Give `Vo`
  to change it.
- **D-431** Uniform cross slope and constant grade along the deck. A sag vertical curve on the bridge is noted, and its
  low-point drain is sized with the sag ponding or gutter inlet tools.
- **D-432** Clogging shortens the effective drain length, the same way the gutter inlet tool applies IDCM clogging.
- **D-433** HEC-21's worked examples were not available as text in this environment; the tests check each equation.

- D-435 (2026-11-25): HEC-21 Examples 1 and 3 are reproduced in `ManualWorkedExampleTests`. HEC-21 takes the drain
  efficiency as the frontal ratio Eo alone (0.245 in Example 3); MasterFlow adds the HEC-22 side-flow term, so its
  efficiency is 0.265 and the spacing 212 ft against the manual's 196 ft. Gutter capacity and the distance to the first
  drain match.

## Messages

| Code | Meaning |
|---|---|
| E-DD-001 | SI project |
| E-DD-002 … 009 | Missing or invalid deck, slope, rainfall, spread, clogging or drain values |
| E-DD-010 | Drain spacing under 1 ft |
| W-DD-001 | Approach flow already fills the gutter |
| W-DD-002 | Drains closer than 20 ft |
| W-DD-003 | Drain efficiency below 30 % |
| I-DD-001 | Scupper Vo assumed |
| I-DD-002 | No deck drains needed |
| I-DD-003 | Sag vertical curve reminder |
