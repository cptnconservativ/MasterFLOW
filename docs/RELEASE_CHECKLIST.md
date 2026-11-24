# Release checklist

Work through this list for every release. Tick each line in the release's tracking issue or notes;
do not skip a line silently. If a line cannot be done, write down why.

## 1. Before the build

- [ ] All roadmap items for the release are ✅ in [ROADMAP.md](../ROADMAP.md) and the
      [feature matrix](FEATURE_MATRIX.md).
- [ ] New decisions are recorded in [DECISIONS.md](DECISIONS.md); new or changed equations in
      [HYDRAULIC_METHODS.md](HYDRAULIC_METHODS.md).
- [ ] The [user guide](USER_GUIDE.md) describes every new or changed behaviour.
- [ ] Version set once, in `<Version>` in `Directory.Build.props` (everything else reads it; a test
      checks this).
- [ ] README status line updated.
- [ ] Working tree committed; the release commit is pushed to `main`.

## 2. Build and verify (Linux or Windows)

Run `scripts/publish-win.sh`. It stops on the first failure. It:

1. runs the whole test suite (Debug);
2. runs the performance tests in Release (5000-row tables and 5000-point sections under 1 s; a PDF with
   a 5000-row table under 5 s);
3. re-runs the independent read-back checks of the sample PDF and Excel files (pypdf, openpyxl);
4. publishes a self-contained single-file `MasterFlow.exe` for win-x64, zips it, builds the per-user
   installer `MasterFlow-x.y.z-setup.exe` from the same exe (needs NSIS: `apt install nsis`), and writes
   SHA-256 files for both.

Then:

- [ ] [VALIDATION_RESULTS.md](VALIDATION_RESULTS.md): test counts, date and any changed reference values
      updated. Discrepancies are explained, not hidden by loosening a tolerance.
- [ ] Independent hand-calculation scripts in `validation/` re-run if the engine changed, and their
      `_output.txt` files committed.

## 3. Smoke test on Windows (clean user account if possible)

- [ ] Unzip and start `MasterFlow.exe` without administrator rights; no network prompt appears.
- [ ] The sample project opens; each shape solves; editing an input updates results at once.
- [ ] Save, close, reopen: everything comes back, including folders, ticks, report settings and logo.
- [ ] Open a project saved by the previous release.
- [ ] Report… saves a PDF that opens in Acrobat or Edge; Batch report… and Compare report… as well;
      the HTML file type still works.
- [ ] Excel… opens in Excel without a repair prompt.
- [ ] Rating table: generate, copy, CSV.
- [ ] From template ▾ and Save as template work; the template survives a restart.
- [ ] Kill the process with unsaved changes (Task Manager); restart: recovery is offered and works.
- [ ] Log folder opens and today's log shows the start, saves and exports.
- [ ] Narrator reads the names of the input fields (spot check).
- [ ] At 150 % and 200 % display scaling nothing is cut off that blocks work.

## 4. Sign and package (needs the firm's certificate; roadmap G2)

- [ ] Sign `MasterFlow.exe` with the code-signing certificate and a timestamp server
      (`signtool sign /fd SHA256 /tr <timestamp URL> /td SHA256 MasterFlow.exe`), then
      `signtool verify /pa MasterFlow.exe`.
- [ ] Re-zip the signed exe and regenerate the SHA-256.
- [ ] Sign the installer the same way (sign `MasterFlow.exe` first, then rebuild the installer from it,
      then sign `MasterFlow-x.y.z-setup.exe`).
- [ ] Installer on a clean account: installs without an admin prompt to `%LOCALAPPDATA%\Programs\MasterFlow`;
      Start menu entry works; double-clicking a `.mfproj` opens it; *Apps & features* shows MasterFlow
      with the right version; uninstall removes the program and association but keeps
      `%APPDATA%\MasterFlow` and the user's projects; installing over an older version upgrades it.

## 5. Publish

- [ ] Tag the release commit `vX.Y.Z` and push the tag.
- [ ] Release notes: what changed for the engineer, any changed results (with the reason), known
      limitations, SHA-256 of the zip.
- [ ] Upload the zip (and installer) where users get it; keep the previous release available.
