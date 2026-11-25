# Water quality flow splitter (diversion structure)

Tool id `flow-splitter` (free). Designs the diversion structure ahead of an off-line water quality BMP (sand filter,
bioretention, proprietary device): a low outlet sends the water quality flow to the BMP and an overflow weir bypasses the
larger storms.

## Input

| Key | Meaning |
|---|---|
| `Q design`, `Q wq` | Storm the structure must pass; the water quality flow (Rapid City SWQM 2009) |
| `orifice invert` | Invert of the outlet to the BMP |
| `orifice diameter` or `crest` | Give one: the other is solved |
| `weir length`, `weir C` | Bypass weir (default C = 3.0 ft-s, 1.84 SI) |
| `Cd` | Orifice coefficient (default 0.61) |
| `max WSE` | Optional water surface limit at the structure |
| `max treatment flow` | Optional most the BMP can take |

## Method

- Orifice to the BMP: Q = Cd·A·√(2g·h), head to the centroid, free outfall.
- Crest from the orifice: crest = centroid + (Q wq/(Cd·A))²/2g. Orifice from the crest: bisection on D (the flow with the
  water at the crest rises with D up to 1.6 × the crest height, so the largest orifice under the crest bounds it).
- Design storm: the water surface where orifice + weir (Cw·L·H^1.5) carry Q design, by bisection. The orifice flow at
  that level is what still reaches the BMP.
- Weir length for `max WSE`: L = (Q design − orifice flow at max WSE)/(Cw·(max WSE − crest)^1.5).

## Decisions

- D-670: The BMP outfall is taken as free; tailwater from the BMP raises the splitter and is checked separately.
- D-671: A crest below the top of the orifice is warned (part-full orifice; the equation overstates it).

## Limits

Single orifice and single weir; baffles, multiple outlets and the bypass pipe's own hydraulics are not modelled.
