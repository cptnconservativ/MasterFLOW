# Field-to-model: as-built survey, photos and HGL certification (★ Premium)

Tool id `field-to-model` (license feature of the same name). Closes the loop from the field back to the model: the
as-built rims and tape measure-downs update the storm network, phone photos are placed on it by their GPS, and the
HGL is re-run on the as-built network to support the engineer's as-built certification.

## Field workflow

1. At each structure: shoot the rim (RTK or level loop) and tape from the rim to the flow line of every pipe.
2. Take a photo of each structure with the phone's location on. Name the photo after the structure ("MH-2 north.jpg")
   or leave the camera name; GPS places it either way.
3. Enter one row per measurement and point `photos =` at the folder.

## Input

| Key | Meaning |
|---|---|
| `network` | The storm network (blank: the selected one) |
| `coordinate system` | `sd-south` or `sd-north` (NAD83 State Plane, US survey ft; metres for an SI network), or `none` |
| `photos` | Folder of .jpg photos; GPS position and time are read from each photo's EXIF block |
| `match radius` | A position (row or photo) within this distance of a structure is matched to it (25 ft) |
| `invert tolerance`, `rim tolerance` | Differences from design larger than these are flagged (0.10 ft) |

`[As-built]` table, columns by header (any order): `Structure`, `Rim`, `Pipe`, `Measure-down` (rim to invert), or
`Invert` (a surveyed invert shot), `Northing`/`Easting` or `Latitude`/`Longitude`, `Photo`, `Note`. A row with no
Structure is matched to the nearest structure by its position. A structure's rim is given once; the measure-downs at
that structure use it (or the design rim, with W-FM-010, when no rim was shot).

## Output

- **As-built HGL check by structure**: design and as-built rim, design and as-built HGL, rim − HGL against the
  network's HGL criterion (IDCM §4.6.7: 1 ft), GPS offset from the model position, photo count, OK/FAILS.
- **Pipe inverts**: design, as-built, difference, how it was obtained, OK / HIGH / LOW / NOT MEASURED.
- **Pipe slopes and capacity**: design and as-built slope and full-flow capacity; ADVERSE, FLATTER (less than half the
  design slope), CHANGED, OVER CAPACITY (as-built capacity below the design flow when the design was not).
- **Photo log**: file, time, latitude/longitude, state plane position, structure matched, distance, matched by (file
  name or GPS).
- Profile chart of the longest flow path: design and as-built inverts and HGL, as-built rims.
- Summary: what was measured, what differs, and the **certification statement**, either "as constructed … the HGL is at
  least 1.00 ft below the rim at all N structures" or the structures where it is not, plus anything else to check
  (adverse or over-capacity pipes, other as-built criteria warnings) and whether the as-built is complete.
- **Apply** puts the as-built rims and inverts into the network (the design stays in the project's history / file).

Warnings: W-FM-004 a position with no structure within the radius; W-FM-008 a pipe that does not connect to the
structure; W-FM-010 measure-down from the design rim; W-FM-011 a field position farther than the radius from the
model (built in a different place); I-FM-001 photos without GPS.

## Decisions

- D-250: measurements are entered as a table (from the field book or the surveyor's file) and photos only place and
  document structures. Phone GPS (5–15 ft) is good enough to say which structure a photo shows and to catch one built
  in the wrong place, not to survey; elevations come from the rim shots and the tape.
- D-251: positions are projected to NAD83 State Plane with the Lambert conformal conic formulas (Snyder 1987) rather
  than a GIS library, so the tool runs offline; SD South and North cover Rapid City and the Black Hills. Check the first
  job against one surveyed control point.
- D-252: the as-built HGL uses the design flows and tailwater, so the certification answers "does what was built carry
  what was designed", and the HGL change column isolates the effect of construction.
- D-253: unmeasured ends keep their design values and the summary says the as-built is not complete, rather than
  refusing to run, so a partial field visit still shows what it found.
