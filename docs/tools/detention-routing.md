# Detention pond routing (IDCM 2012 §4.10)

| Item | Method |
|---|---|
| Stage–storage | Frustum (conic) method between stage rows, V = h/3 (A₁ + A₂ + √(A₁A₂)) |
| Orifice | Full: Q = C A √(2g h), h to the centroid. Below the crown: Q = (2/3) C √(2g) B h^1.5, B = A / opening height (circular: πD/4), which meets the full-orifice value within 6 % at the crown |
| Weir | Q = C L h^1.5 (default C 3.0) |
| Outlet pipe (barrel) | Limits the sum of the orifices and weirs. Capacity at a pond level is the flow whose FHWA HDS-5 controlling headwater equals the level above the barrel's inlet invert: the larger of inlet control (concrete pipe, square edge with headwall, HDS-5 chart 1: unsubmerged Form 1, transition, submerged) and outlet control with a free outfall, HWo = ho + (1 + Ke + Kf)V²/2g − S·L, ho = (dc + D)/2. Same engine as the [culvert tool](culverts.md). This replaced an earlier self-derived curve below the inlet crown (Qcrown·(h/D)^1.5). The rating is continuous and rises with stage, which storage-indication routing needs. |
| Inflow | Modified rational (§4.10.7): Q = C i(Td) A, i from Table 4-2 for each duration Td from tc to 180 min; trapezoid rising over tc, flat to Td, falling over tc |
| Routing | Storage indication (level pool), 1-min steps; the duration giving the highest water level is critical |
| Checks | Peak outflow ≤ pre-development for the 2-, 10- and 100-yr storms (§4.10.4); 1 ft freeboard above the 100-yr level (§4.10.3(2)); drain within 72 h after the 100-yr storm (§4.10.5); outlet pipe ≥ 18 in. (§4.10.5); trickle channel capacity 2 % of the 100-yr inflow (§4.10.4(3)) |

Errors stop the storm in question: a pond that fills above the top stage is reported as overtopping,
never extrapolated.

Decision (D-055): the modified rational method was chosen because §4.10.7 names it and it needs only
Table 4-2 and the rational inputs MasterFlow already has. HEC-HMS remains the tool for basins where the IDCM
requires it (§4.2.1). Tests check storage and outlet flows in closed form and routing by mass balance
(outflow volume within 0.5 % of inflow).
