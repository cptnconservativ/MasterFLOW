#!/usr/bin/env bash
# Builds the user manual PDF and DOCX from docs/USER_GUIDE.md.
# Usage: tools/manual/build.sh <out folder> [reference.docx]
# Needs pandoc, Node with Playwright (Chromium) and, for DOCX styling, a reference .docx
# (the previous MasterFlow-User-Manual.docx works as one).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; repo="$(cd "$here/../.." && pwd)"
out="$(mkdir -p "$1" && cd "$1" && pwd)"; ref="${2:-}"
cd "$repo/docs"
pandoc USER_GUIDE.md -s --toc --toc-depth=2 --number-sections --embed-resources \
  --css "$here/manual.css" --metadata pagetitle="MasterFlow User Manual" -o "$out/manual.html"
node "$here/print-pdf.js" "$out/manual.html" "$out/MasterFlow-User-Manual.pdf"
docx_args=(USER_GUIDE.md --toc --toc-depth=2 --number-sections -o "$out/MasterFlow-User-Manual.docx")
[ -n "$ref" ] && docx_args+=(--reference-doc "$ref")
pandoc "${docx_args[@]}"
rm "$out/manual.html"
echo "Wrote $out/MasterFlow-User-Manual.pdf and .docx"
