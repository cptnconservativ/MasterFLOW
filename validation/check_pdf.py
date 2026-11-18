"""Independent read-back of MasterFlow's native PDF samples with pypdf (not SkiaSharp).
Checks: page count, page size, metadata, extractable text containing the engine's values,
"Page n of N" on every page, and that fonts are embedded. Run: python3 -I validation/check_pdf.py"""
import sys, re
from pypdf import PdfReader

def check(path, expect):
    r = PdfReader(path)
    n = len(r.pages)
    print(f"{path}: {n} pages, title={r.metadata.title!r}, creator={r.metadata.creator!r}")
    ok = True
    for i, p in enumerate(r.pages, 1):
        t = p.extract_text()
        if f"Page {i} of {n}" not in t:
            print(f"  FAIL page {i}: no 'Page {i} of {n}'"); ok = False
        fonts = p.get("/Resources", {}).get("/Font", {})
        for name, f in fonts.items():
            f = f.get_object()
            desc = f.get("/DescendantFonts", [None])[0]
            desc = desc.get_object() if desc else f
            fd = desc.get("/FontDescriptor").get_object()
            if not any(k in fd for k in ("/FontFile", "/FontFile2", "/FontFile3")):
                print(f"  FAIL page {i}: font {f.get('/BaseFont')} not embedded"); ok = False
    allt = " ".join(p.extract_text() for p in r.pages)
    allt = re.sub(r"\s+", " ", allt)
    for s in expect:
        found = s in allt
        print(f"  {'ok  ' if found else 'FAIL'} text {s!r}")
        ok &= found
    w, h = float(r.pages[0].mediabox.width), float(r.pages[0].mediabox.height)
    print(f"  page size {w:.0f} x {h:.0f} pt")
    return ok

ok = check("docs/samples/sample-report.pdf",
           ["Trapezoidal ditch", "3.361", "2.148", "400.000", "Creek with overbanks", "6.893", "3.741",
            "Rating table", "828.544", "Section points", "Flow distribution", "Calculation trace", "SAMPLE"])
ok &= check("docs/samples/sample-comparison.pdf", ["Alternative comparison", "3.361", "6.893", "15811.39"])
print("ALL PASS" if ok else "SOME CHECKS FAILED")
sys.exit(0 if ok else 1)
