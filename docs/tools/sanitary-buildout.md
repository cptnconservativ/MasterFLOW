# Sanitary sewer build-out projection and phasing (premium)

Tool id `sanitary-buildout` (★ Premium, feature `sanitary-buildout`; U.S. units). Projects the sanitary flows of a
network year by year from today's loads to build-out, and for each planned sewer gives the year its peak-flow depth
reaches the d/D limit (0.70, Rapid City IDCM 3.12.4) and the year it surcharges, the size the build-out flow needs, and
whether to build that size now or build the planned size and relieve it later.

## Input

A [Sanitary sewer network design](sanitary-network.md) input (same keys, `[Manholes]` and `[Pipes]`, or the selected
network with `redesign = no`), with the **planned** diameters and inverts given, plus:

| Key | Default | Meaning |
|---|---|---|
| `base year` | this year | the year of the `[Loads]` |
| `build-out year` | base + 30 | last year projected |
| `decision horizon` | 20 years | a sewer reaching the limit within this many years of the base year is built at the build-out size now |
| `report every` | 5 years | spacing of the columns of the d/D table |
| `max d/D` | 0.70 | IDCM 3.12.4 item 2 |

- `[Loads]` today's loads, as in the sanitary sewer design.
- `[Growth]` Manhole, Land use, Area (ac), DU/ac, Population, Flow (gpm), PF, Start year, End year: load added at that
  manhole (basin), rising linearly from none in the Start year to all of it in the End year. A blank End year adds it
  all in the Start year. Columns are found by their header words.

## Method

1. For every whole year from the base year to build-out, the loads are today's plus each growth row times its
   fraction (D-725), and the sanitary sewer design runs on them with the planned sizes and inverts. Peaking and
   reduction factors are worked out again each year from the population and acres served (D-728).
2. Each sewer's d/D is the normal depth of that year's peak flow (with I/I) on its planned slope (D-727). The
   first year above the limit and the first year the flow exceeds the open-channel capacity (0.938 D) are reported.
3. The build-out size is the sanitary design's size for the build-out flow on the same inverts (the smallest listed
   size with d/D within the limit, not below any upstream pipe).
4. Recommendation (D-726): adequate through build-out; over the limit already; reaching the limit within the decision
   horizon, so build the build-out size now; or reaching it later, so build as planned and relieve or parallel by that
   year.

Tables: phasing; d/D by year; flow at the outlet by year; the growth schedule. A chart shows d/D against year for up
to eight sewers (the highest at build-out) with the 0.70 line.

## Decisions

- **D-725** Growth is linear between the Start and End years in Area, Population and Flow; DU/ac and PF are kept.
  Whole years only. Rapid City publishes no growth rates for basins; they come from the master plan or the engineer.
- **D-726** The build-now-or-relieve rule is a single decision horizon (20 years by default), not a cost comparison:
  the IDCM has no rule. Set it to the utility's planning period, or to the payback period of the oversizing.
- **D-727** d/D is checked at normal depth, as the sanitary design does. A trunk near the limit should also be run
  through [Sanitary trunk sewer HGL](sanitary-trunk-hgl.md) with that year's loads for the backwater.
- **D-728** Because the IDCM reduction factors step down as the population served grows (e.g. 0.905 to 0.86 above
  2,000 people), a year's peak flow can drop slightly after a step. The year reported is the first year above the
  limit.

## Messages

| Code | Meaning |
|---|---|
| E-BO-001 | SI project |
| E-BO-002 | Years, horizon or report interval invalid |
| E-BO-003 | Growth row without a Start year, or End before Start |
| E-BO-004 | No `[Growth]` rows |
| E-BO-005 | A planned diameter or invert is blank (or the network is set to be redesigned) |
| E-BO-006 | The sanitary sewer design failed for a year (its errors follow) |
| W-BO-001 | The planned size reaches the limit within the decision horizon |
| W-BO-002 | The planned size is over the limit already |
| W-BO-003 | The build-out sizes could not be designed |
| I-BO-001 | Decision rule in use |

## Tests

`SanitaryBuildoutTests`: the sample gives all three recommendations and a chart; SI refused; a linear ramp and a step
add flow in proportion; the year d/D passes 0.70 matches the hand capacity at 0.70 D; the decision horizon switches
between build now and relieve later; adequate and over-now cases; the bad-input errors; the section helpers.
