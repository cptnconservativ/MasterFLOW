# Cost estimate and bid tabulation (premium)

Tool id `cost-estimate`. This tool builds the engineer's estimate and the bid tab for the selected storm network.
The free [quantity takeoff](quantity-takeoff.md) stays as it is. This tool uses the same takeoff, which was moved into
`QuantityTool.Takeoff` so both tools share one implementation, and adds the following.

* **Pavement removal and replacement**: (trench width + 2 × `pavement overcut`) × length, in SY (m² in SI).
  `pavement` takes `all`, `none` or a list of pipe names. An unknown name is reported (W-CE-001).
* **Dewatering**: the pipe length, in LF (m), wherever the trench bottom (invert − wall − bedding) is below
  `groundwater elevation`.
* **Bid items** (`[Bid items]`): the columns are No, Item, Unit and Quantity, then any number of price columns. The
  first price column is the estimate; the others are bidders.
  * A blank quantity is taken from the takeoff by item name, using the same names as the quantity takeoff (for
    example `Pipe 18 in RCP`, `Manhole extra depth`, `Excavation`). It is converted when the bid unit differs, for
    example CY to CF or LF to m (I-CE-002).
  * A unit that does not convert is an error (E-CE-006).
  * Typed quantities are used as entered, for example lump sums.
  * A takeoff item missing from the bid list is reported (W-CE-003).
  * A bid item with no quantity is reported (W-CE-002).
* **Estimate only**:
  * Escalation by a cost index: `price index from` and `price index to`, for example ENR CCI then and now.
  * `mobilization` as a percentage of the bid-item subtotal.
  * `contingency` on the construction cost.
  * `engineering` on the construction cost plus contingency.
* **Bids**: each bid total is the sum of its bid items. Each bid is ranked and compared with the estimate's
  construction cost (subtotal plus mobilization). A bid with an unpriced item is incomplete and is not ranked
  (W-CE-004).
* **Unbalanced bid review**: flags every bidder unit price more than `unbalanced threshold` (50 %) from the average
  of all priced columns for that item, with its effect (quantity × difference).
* **Cost by category** for the estimate: pipe, structures, earthwork and dewatering, surface restoration, and other.

Messages:
* Errors: E-CE-001 no network; E-CE-002 a negative or zero setting; E-CE-003 cost index; E-CE-004 bid item table;
  E-CE-005 negative quantity or price; E-CE-006 unit does not convert.
* Warnings and information: W-CE-001 to W-CE-005, as listed above; I-CE-001 assumptions; I-CE-002 unit converted;
  I-CE-003 escalation.

**The sample prices are examples only.**

Decisions:
* D-090: quantities come from one shared takeoff, so the free and premium tools cannot disagree.
* D-091: markups apply to the estimate only. Bidders' mobilization is one of their bid items, which is how bid tabs
  are usually laid out.
* D-092: the unbalanced test compares each price with the average of every column, including the estimate. With only
  two columns, a large difference flags the bidder.
* D-093: not included yet: rock excavation, trench boxes, utility conflicts and tiered unit prices by quantity. They
  need site data or price curves.
