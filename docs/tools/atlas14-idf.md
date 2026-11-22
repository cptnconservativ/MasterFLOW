# NOAA Atlas 14 rainfall (IDF lookup) (premium)

Tool id `atlas14-idf`. It gets the NOAA Atlas 14 point precipitation frequency table for a location and turns it
into depth and intensity tables, IDF curves, a comparison with Rapid City IDCM Table 4-2, and input lines that
other MasterFlow tools accept.

## Getting the table

* **Online:** set `latitude`, `longitude` and `fetch = yes`. MasterFlow requests
  `https://hdsc.nws.noaa.gov/cgi-bin/hdsc/new/fe_text_mean.csv?lat=…&lon=…&data=depth&units=english&series=pds|ams`
  from NOAA's Precipitation Frequency Data Server (PFDS). The request carries the coordinates and nothing else.
  The answer is saved in `%APPDATA%\MasterFlow\atlas14\<lat>_<lon>_<series>.csv`. Later runs read that copy and
  send nothing, so a project's report keeps the same numbers. `refresh = yes` fetches again. This is the only
  MasterFlow feature that uses the internet, and it does so only when `fetch = yes`.
* **Offline:** open https://hdsc.nws.noaa.gov/pfds/, pick the point, choose **Download: CSV**, and paste the
  text under `[PFDS]`. The paste is saved with the project. The sample uses this route, with the Rapid City table
  fetched on 2026-11-21 (44.0805, −103.2310; Atlas 14 Vol. 8 v2, partial duration).

## Outputs

| Table / chart | Content |
|---|---|
| Precipitation depth | Every PFDS duration (5 min to 60 days), with a column for each storm in `storms` |
| Intensity | Depth ÷ duration, 5 min to 24 h |
| 90 % confidence interval | When the CSV holds the lower and upper blocks |
| IDF curves | Log–log, 5 min to 24 h |
| Comparison with IDCM Table 4-2 | 2-, 10- and 100-yr at the Table 4-2 durations, with the percent difference (`compare = none` skips it) |
| Input lines for other tools | An `[IDF]` block (`values = depth`) for the **Rational method** (2, 5, 10 and 100-yr, the storms with Table 4-1 C values), and `P2 = …` lines for **TR-55** and **Hydrograph routing** (`rainfall = type-ii`) |

Between tabulated durations, depths are interpolated linearly in log depth against log duration. This is the
same rule the rational tool applies to Table 4-2.

## Rapid City

IDCM §4.2.4 and §4.3.2 base City design on Table 4-2, which comes from NOAA's 1977 IDF data. For Rapid City,
Atlas 14 is up to 38 % higher for the 100-yr storm at 3–24 h and about 12 % lower for the 10-yr 1-h depth. The tool
reports the differences without changing which table governs: use Atlas 14 for SDDOT, county or FEMA work, or
to show sensitivity.

## Validation

* Parsing reproduces the PFDS table exactly (19 durations × 10 ARIs). The 24-h depths are 2.28 in. (2-yr) and
  6.12 in. (100-yr).
* The intensity, interpolation and comparison percentages are checked by hand in `Atlas14Tests`.
* The rational `[IDF]` lines run in the Rational method tool without errors. The fetch-then-cache path is tested
  with a stand-in fetcher, so tests never touch the network.

Decisions:

* D-110: premium. It is an online data service, like the other premium conveniences. It can move to free with
  one line in `PremiumFeatures`.
* D-111: fetching is opt-in (`fetch = yes`), and the response is cached and reused. MasterFlow's local-only
  rule (D-033) holds for everything else, and a project re-runs offline with identical numbers.
* D-112: the tool hands rainfall to other tools as input lines, so it does not rewrite their inputs. The
  engineer chooses where Atlas 14 replaces Table 4-2.
