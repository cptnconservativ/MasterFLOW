# Pond stage–storage from a TIN surface

Tool id `pond-storage` (free). Builds the stage–area–volume table of a pond straight from its graded surface, so the
storage used for routing is the storage that is graded, not a contour estimate.

## Input

| Key / section | Meaning |
|---|---|
| `surface file` | Path of a LandXML 1.x file exported from Civil 3D (*Output ▸ Export to LandXML*, surfaces checked). |
| `surface` | Surface name in that file (case-insensitive). The first TIN surface is used when left out. |
| `[TIN points]`, `[TIN faces]` | Instead of a file: points `Id, X (easting), Y (northing), Z` and triangles of three point Ids. |
| `[Boundary]` | Optional polygon `X, Y`. Only triangles whose plan centroid is inside count (pond grading limits; leaves out other low areas). |
| `interval` | Stage interval (0.5 ft or 0.25 m by default). |
| `bottom`, `top` | Optional stage range; default the lowest and highest point inside the boundary. |

LandXML points are read as `northing easting elevation` (the LandXML order). Faces marked invisible (`i="1"`, outside
the surface boundary) are left out and reported. DTDs are refused when the file is read.

## Output

- **Stage–area–volume**: elevation, depth, water surface area (ft² and ac), volume (ft³ and ac-ft), the conic-method
  volume on the same areas and its difference, and a note at the rim (spill) elevation.
- **Ready to paste**: a `[Stage storage]` block of elevation–volume for the detention routing tool, and an
  elevation–area block for the outlet designer.
- Stage–storage chart (TIN and conic, with the rim line) and stage–area chart.

## Method

The water surface is level. Each triangle is clipped to the part below the water surface (Sutherland–Hodgman on the
plane z ≤ WSE). Because z is linear on a triangle, ∫(WSE − z) dA over the clipped polygon equals its plan area times
(WSE − z at its plan centroid). The volume and area are therefore exact for the TIN, with no contour interval error.

The rim is the outer edge of the triangles used (edges that belong to one triangle only). Its lowest vertex is the
spill elevation; above it water leaves the graded area, so stages above it are flagged (W-PS-003).

## Decisions

- **D-125** Exact TIN clipping, not contour areas with the conic or average-end-area formula. Civil 3D's own
  stage–storage report uses contour areas; the conic comparison column shows how far that differs (usually well under
  1 %, more for irregular ponds or coarse intervals).
- **D-126** Level-pool: every part of the surface below a stage counts. Separate pockets that do not drain to the
  outlet must be left out with `[Boundary]`; the tool does not trace connectivity.
- **D-127** Boundary by triangle centroid rather than clipping triangles to the polygon. Draw the boundary along the
  pond's top of berm (outside the stage range of interest) so the triangles it splits do not matter.

## Validation

- A 4H:1V frustum TIN (bottom 100 × 50 ft at 100.0, 6 ft deep) matches the prismoidal formula
  V = LWh + (L+W)zh² + (4/3)z²h³ at every 0.25 ft stage to 1e-6 ft³; at 6 ft both give 56,208 ft³ (1.290 ac-ft) by hand.
- A single sloped triangle matches the hand integral: 31.25 ft³ and 37.5 ft² below the half-height level.
- The volume block pastes into the detention routing tool and gives the same 1.290 ac-ft.
- LandXML read with two surfaces, selection by name, an invisible face and a unit check.
