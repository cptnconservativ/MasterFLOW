"""Independent hand calculation for the HEC-22 inlet tool (InletNetworkTool).
Written from the published equations (HEC-22 3rd ed. Sec. 4.3-4.4; IDCM Eq. 4-4-1 exponents; MHFD Table 7-6
splash-over constants), not from the C# code. Run: python3 validation/hand_calcs_inlets.py"""
import math
G = 32.174

def spread_uniform(q, n, sx, s):            # IDCM Eq. 4-4-1 solved for T
    return (q * n / (0.56 * sx**1.67 * s**0.5)) ** (1 / 2.67)

def eo_uniform(w, t):                       # HEC-22 Eq. 4-16
    return 1.0 if w >= t else 1 - (1 - w / t) ** 2.67

def eo_composite(t, w, sx, sw):             # HEC-22 Eq. 4-4
    r = sw / sx
    return 1 / (1 + r / ((1 + r / (t / w - 1)) ** 2.67 - 1))

def q_composite(t, n, sx, sw, w, s):
    qs = 0.56 / n * sx**1.67 * s**0.5 * (t - w) ** 2.67
    return qs / (1 - eo_composite(t, w, sx, sw))

def spread_composite(q, n, sx, sw, w, s):
    lo, hi = w + 1e-9, 200.0
    for _ in range(300):
        m = (lo + hi) / 2
        if q_composite(m, n, sx, sw, w, s) < q: lo = m
        else: hi = m
    return (lo + hi) / 2

def curb_grade(q, s, n, se, l):             # HEC-22 Eqs. 4-22, 4-23
    lt = 0.6 * q**0.42 * s**0.3 * (1 / (n * se)) ** 0.6
    e = 1.0 if l >= lt else 1 - (1 - l / lt) ** 1.8
    return lt, e

def vo(a, b, c, d, l): return a + b * l - c * l**2 + d * l**3

print("Case 1  uniform Sx 0.02, n 0.016")
# CB-A: curb opening on grade, Q 4, S 0.015, L 10, depression a = 2 in over W = 2 ft
q, s, n, sx = 4.0, 0.015, 0.016, 0.02
t = spread_uniform(q, n, sx, s); eo = eo_uniform(2, t); se = sx + (2 / 12 / 2) * eo
lt, e = curb_grade(q, s, n, se, 10); qi = e * q; byA = q - qi
print(f"  CB-A T={t:.4f} Eo={eo:.4f} Se={se:.5f} LT={lt:.4f} E={e:.5f} Qi={qi:.5f} bypass={byA:.5f}")
# CB-B: P-50x100 grate on grade, local 2.5 + bypass, S 0.012, L 3 (40 % clog -> 1.8), W 2
q = 2.5 + byA; s = 0.012; le = 3 * 0.6
t = spread_uniform(q, n, sx, s); A = sx * t * t / 2; v = q / A
v0 = vo(0.74, 2.44, 0.27, 0.02, le); eo = eo_uniform(2, t)
rf = 1.0 if v <= v0 else max(0, 1 - 0.09 * (v - v0)); rs = 1 / (1 + 0.15 * v**1.8 / (sx * le**2.3))
e = rf * eo + rs * (1 - eo); qi = e * q; byB = q - qi
print(f"  CB-B Q={q:.5f} T={t:.4f} V={v:.4f} Vo={v0:.4f} Eo={eo:.4f} Rf={rf:.4f} Rs={rs:.5f} E={e:.5f} Qi={qi:.5f} bypass={byB:.5f}")
# CB-C: sag grate, Q = 1.5 + bypass, L 3 W 2 A 3.2 clogged 40 %: Le 1.8, A 1.92, P = 1.8 + 4 = 5.8
q = 1.5 + byB; P = 1.8 + 2 * 2; Ae = 3.2 * 0.6
dw = (q / (3.0 * P)) ** (2 / 3); do = (q / (0.67 * Ae)) ** 2 / (2 * G); dg = max(dw, do)   # capacity = min(weir, orifice)
d = dg + sx * 2 / 2                         # depth at curb = average grate depth + Sx*W/2
print(f"  CB-C Q={q:.5f} grate depth={dg:.5f} ({'weir' if dw>=do else 'orifice'}) depth at curb={d:.5f} T={d/sx:.4f}")

print("Case 2  composite gutter W 2, Sw 0.0833, Sx 0.02, n 0.016; curb opening on grade Q 4, S 0.015, L 10, no local depression")
w, sw = 2.0, 0.0833; q, s = 4.0, 0.015
t = spread_composite(q, n, sx, sw, w, s); eo = eo_composite(t, w, sx, sw); a = w * (sw - sx)
se = sx + (a / w) * eo; lt, e = curb_grade(q, s, n, se, 10)
print(f"  T={t:.5f} Eo={eo:.5f} a={a:.4f} Se={se:.5f} LT={lt:.4f} E={e:.5f} Qi={e*q:.5f} depth={t*sx+a:.5f}")

print("Case 3  sag curb opening, uniform gutter, undepressed, L 5, h 0.5")
def curb_sag(d, L, h):
    weir = lambda dd: 3.0 * L * dd**1.5
    orif = lambda dd: 0.67 * h * L * math.sqrt(2 * G * (dd - h / 2))
    if d <= h: return weir(d)
    if d >= 1.4 * h: return orif(d)
    return weir(h) + (orif(1.4 * h) - weir(h)) * (d - h) / (0.4 * h)
def solve(q, f):
    lo, hi = 0, 5
    for _ in range(200):
        m = (lo + hi) / 2
        if f(m) < q: lo = m
        else: hi = m
    return hi
for q in (3.0, 6.0, 12.0):
    d = solve(q, lambda dd: curb_sag(dd, 5, 0.5))
    print(f"  Q={q}: d={d:.5f}")

print("Case 4  sag combination, uniform, grate L 3 W 2 A 3.2 (33 % clog), curb opening L 3 h 0.5")
Le, Ae = 3 * 0.67, 3.2 * 0.67; P = Le + 4
def comb(dc):
    dg = max(0, dc - sx * 2 / 2)
    gw, go = 3.0 * P * dg**1.5, 0.67 * Ae * math.sqrt(2 * G * dg)
    if gw <= go: return gw
    co = 0.67 * 0.5 * 3 * math.sqrt(2 * G * (dc - 0.25)) if dc > 0.5 else 0
    return go + co
for q in (2.0, 12.0):
    d = solve(q, comb)
    print(f"  Q={q}: depth at curb={d:.5f}")
