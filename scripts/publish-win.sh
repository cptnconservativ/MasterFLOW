#!/usr/bin/env bash
# Builds the Windows release of MasterFlow: tests (Debug + Release performance), independent
# validation scripts, self-contained single-file publish, zip, per-user installer (NSIS) and SHA-256s.
# Usage: scripts/publish-win.sh [output-dir]     (run from anywhere; needs .NET 10 SDK, python3, zip)
# Signing (roadmap G2) is a separate step on a Windows machine holding the certificate; see
# docs/RELEASE_CHECKLIST.md.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-$ROOT/artifacts}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props")"
echo "MasterFlow $VERSION"

if [ -n "$(git -C "$ROOT" status --porcelain)" ]; then
  echo "Working tree has uncommitted changes; commit or stash first." >&2; exit 1
fi

dotnet test "$ROOT/MasterFlow.slnx" -v q
dotnet test "$ROOT/tests/MasterFlow.IntegrationTests" -c Release --filter PerformanceTests -v q

( cd "$ROOT" && for s in validation/*.py; do
    case "$s" in *check_pdf.py|*check_xlsx.py) ;; *) continue ;; esac
    python3 -I "$s" > "${s%.py}_output.txt" || { echo "validation failed: $s" >&2; exit 1; }
  done )

PUB="$(mktemp -d)"
dotnet publish "$ROOT/src/MasterFlow.Desktop" -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUB" -v q
mkdir -p "$OUT"
ZIP="$OUT/MasterFlow-$VERSION-win-x64.zip"
rm -f "$ZIP"
( cd "$PUB" && zip -q -9 "$ZIP" MasterFlow.exe )
( cd "$OUT" && sha256sum "$(basename "$ZIP")" > "$(basename "$ZIP").sha256" )
echo "Wrote $ZIP"
cat "$ZIP.sha256"

# Per-user installer (roadmap G1), built from the same exe, when NSIS is installed (apt install nsis).
if command -v makensis >/dev/null; then
  SETUP="$OUT/MasterFlow-$VERSION-setup.exe"
  makensis -V2 -DVERSION="$VERSION" -DSOURCE="$PUB" -DOUTFILE="$SETUP" "$ROOT/installer/MasterFlow.nsi"
  ( cd "$OUT" && sha256sum "$(basename "$SETUP")" > "$(basename "$SETUP").sha256" )
  echo "Wrote $SETUP"
  cat "$SETUP.sha256"
else
  echo "makensis not found: installer skipped"
fi
rm -rf "$PUB"
