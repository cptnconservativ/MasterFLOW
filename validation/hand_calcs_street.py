"""Independent hand check of the street capacity tool (StreetCapacityTool / GutterSection).

Same published equations, written separately from the C# code:
  IDCM 2012 Eq. 4-4-1  Q = (0.56/n) Sx^1.67 S^0.5 T^2.67  (exponents as printed, D-054)
  HEC-22 Eq. 4-4       composite gutter: Q = Qs/(1-Eo), Eo = 1/(1 + (Sw/Sx)/((1 + (Sw/Sx)/(T/W - 1))^2.67 - 1))
  100-yr: Manning (1.486/n) A R^(2/3) S^0.5 on the street section, subareas split where n changes.
Run: python3 validation/hand_calcs_street.py > validation/hand_calcs_street_output.txt
"""
import math

def q_uniform(t, n, sx, s):
    return 0.56 / n * sx**1.67 * s**0.5 * t**2.67

def eo(t, w, sx, sw):
    r = sw / sx
    return 1 / (1 + r / ((1 + r / (t / w - 1))**2.67 - 1))

def q_composite(t, n, sx, sw, w, s):
    if t <= w:
        return 0.56 / n * sw**1.67 * s**0.5 * t**2.67
    return q_uniform(t - w, n, sx, s) / (1 - eo(t, w, sx, sw))

def invert(f, q, hi=200.0):
    lo = 0.0
    for _ in range(200):
        m = (lo + hi) / 2
        if f(m) < q: lo = m
        else: hi = m
    return (lo + hi) / 2

# --- HEC-22 Example 4-2 style composite gutter: W = 2 ft, Sw = 0.0833, Sx = 0.02, S = 0.01, n = 0.016, T = 8 ft
print("Composite gutter, T = 8 ft (HEC-22 Ex. 4-2 data)")
e = eo(8, 2, 0.02, 0.0833)
q = q_composite(8, 0.016, 0.02, 0.0833, 2, 0.01)
print(f"  Eo = {e:.6f}   Qs = {q_uniform(6, 0.016, 0.02, 0.01):.6f}   Q = {q:.6f} cfs   (HEC-22 prints Eo 0.69, Q 1.94 with 5/3, 8/3)")

# --- Tool sample: collector, 36 ft, curb 0.5, Sx 0.02, n 0.016, W 2, Sw 0.0625, parkway 10 ft @ 0.02, n 0.035
n, sx, sw, w, b, h, pw, ps, pn = 0.016, 0.02, 0.0625, 2.0, 36.0, 0.5, 10.0, 0.02, 0.035
a = w * (sw - sx)
def depth(t): return t * sw if t <= w else t * sx + a
def area(t): return 0.5 * sw * t * t if t <= w else 0.5 * sx * t * t + 0.5 * a * w
t_allow = (b - 12) / 2
t_curb = (h - a) / sx
t_gov = min(t_allow, t_curb, b / 2)
print(f"\nSample street: a = {a:.6f} ft; T allowed {t_allow}, curb-full T {t_curb:.6f}, governing {t_gov:.6f}")
for name, s, q10 in [("1", 0.012, 3.2), ("2", 0.006, 6.5), ("3", 0.005, 9.0)]:
    t = invert(lambda tt: q_composite(tt, n, sx, sw, w, s), q10)
    print(f"  reach {name}: T = {t:.6f} ft  d = {depth(t):.6f} ft  V = {q10 / area(t):.6f} fps  Eo = {eo(t, w, sx, sw):.6f}  Q allowed = {q_composite(t_gov, n, sx, sw, w, s):.6f} cfs")

# 100-yr: section points, flow line = 0
crown = w * sw + (b / 2 - w) * sx
row = h + pw * ps
pts = [(0, row, pn), (pw, h, n), (pw, 0, n), (pw + w, w * sw, n), (pw + b / 2, crown, n),
       (pw + b - w, w * sw, n), (pw + b, 0, n), (pw + b, h, pn), (2 * pw + b, row, None)]

def seg(x1, z1, x2, z2, y):
    """area, wetted perimeter of one segment below water level y"""
    if x1 == x2:
        lo, hi = min(z1, z2), max(z1, z2)
        return 0.0, max(0.0, min(y - lo, hi - lo))
    if z1 >= y and z2 >= y: return 0.0, 0.0
    if z1 < y and z2 < y:
        return (x2 - x1) * (y - (z1 + z2) / 2), math.hypot(x2 - x1, z2 - z1)
    # partly wet
    frac = (y - min(z1, z2)) / abs(z2 - z1)
    dx, dz = (x2 - x1) * frac, abs(z2 - z1) * frac
    return 0.5 * dx * dz, math.hypot(dx, dz)

def q100(y, s):
    groups = {}
    order = []
    for i in range(len(pts) - 1):
        x1, z1, nn = pts[i]; x2, z2, _ = pts[i + 1]
        # contiguous runs of equal n form a subarea
        key = len(order) if not order or order[-1][0] != nn else len(order) - 1
        if not order or order[-1][0] != nn: order.append([nn, 0.0, 0.0])
        A, P = seg(x1, z1, x2, z2, y)
        order[key][1] += A; order[key][2] += P
    # walls above the end points
    if y > pts[0][1]: order[0][2] += y - pts[0][1]
    if y > pts[-1][1]: order[-1][2] += y - pts[-1][1]
    return sum(1.486 / nn * A * (A / P)**(2 / 3) * s**0.5 for nn, A, P in order if A > 0 and P > 0)

print(f"\n100-yr: crown {crown:.6f} ft, ROW line {row:.6f} ft")
for name, s, qm in [("1", 0.012, 28), ("2", 0.006, 52), ("3", 0.005, 95)]:
    y = invert(lambda yy: q100(yy, s), qm, hi=10)
    print(f"  reach {name}: y = {y:.6f} ft ({y * 12:.3f} in.)  Q at 12 in. = {q100(1.0, s):.6f}  Q at ROW = {q100(row, s):.6f}")
