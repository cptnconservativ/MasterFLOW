"""Independent check that MasterFlow's .xlsx output opens in a third-party reader (openpyxl)
and that numbers survive at full precision. Run after the test suite writes docs/samples/sample-workbook.xlsx."""
import sys
from openpyxl import load_workbook

path = sys.argv[1] if len(sys.argv) > 1 else "docs/samples/sample-workbook.xlsx"
wb = load_workbook(path)
print("sheets:", wb.sheetnames)
ws = wb["Comparison"]
rows = {r[0]: r for r in ws.iter_rows(values_only=True) if r and r[0]}
print("Normal depth row:", rows["Normal depth"])
yn = rows["Normal depth"][3]
assert isinstance(yn, float) and abs(yn - 3.3610) < 1e-3, yn   # Chow Ex. 6-2, yn = 3.36 ft
rating = wb[wb.sheetnames[-1]]
data = list(rating.iter_rows(values_only=True))
print("rating rows:", len(data) - 1, "first:", data[1])
assert all(isinstance(v, (int, float)) for v in data[1])
print("OK")
