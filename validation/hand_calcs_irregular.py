"""Independent check of irregular (station/elevation) sections.

Does NOT reuse the engine's segment formulas: area is integrated as the water depth over station
(midpoint rule on a fine grid), wetted perimeter is the length of the ground polyline below the water
surface measured on a fine resampling, and roots are found by dense sampling plus bisection.
Conveyance subdivision follows HEC-RAS (subareas split by vertical lines where n changes).
US customary: k = 1.486, g = 32.174.
"""
from math import sqrt
K, G = 1.486, 32.174
STEPS = 200_000

def resample(pts, steps):
    """Fine polyline (x, z, n) with uniform parameter along each segment; vertical segments included."""
    out = []
    for i in range(len(pts) - 1):
        (x1, z1, n1), (x2, z2, _) = pts[i], pts[i + 1]
        m = max(2, steps // (len(pts) - 1))
        for j in range(m):
            t = j / m
            out.append((x1 + (x2 - x1) * t, z1 + (z2 - z1) * t, n1))
    out.append((pts[-1][0], pts[-1][1], pts[-1][2]))
    return out

def props(pts, E, steps=STEPS):
    """Per-n subarea (A, P) dict and totals at water-surface elevation E (absolute)."""
    fine = resample(pts, steps)
    sub = {}
    order = []
    for i in range(len(fine) - 1):
        x1, z1, n = fine[i]; x2, z2, _ = fine[i + 1]
        key = (n, group_id(pts, x1, z1, x2, z2))
        if key not in sub: sub[key] = [0.0, 0.0]; order.append(key)
        dx = x2 - x1
        zm = 0.5 * (z1 + z2)
        if dx > 0 and zm < E: sub[key][0] += dx * (E - zm)        # midpoint area strip
        lo, hi = min(z1, z2), max(z1, z2)
        L = sqrt(dx * dx + (z2 - z1) ** 2)
        if hi <= E: sub[key][1] += L
        elif lo < E: sub[key][1] += L * (E - lo) / (hi - lo)
    # end walls
    if E > pts[0][1]: sub[order[0]][1] += E - pts[0][1]
    if E > pts[-1][1]: sub[order[-1]][1] += E - pts[-1][1]
    return [(k[0], v[0], v[1]) for k, v in ((k, sub[k]) for k in order)]

_groups = None
def set_groups(pts):
    """Subareas = runs of consecutive segments with equal n (segment n = n of its left point)."""
    global _groups
    _groups = []
    gid = 0
    for i in range(len(pts) - 1):
        if i > 0 and pts[i][2] != pts[i - 1][2]: gid += 1
        _groups.append((pts[i][0], pts[i + 1][0], pts[i][1], pts[i + 1][1], gid))

def group_id(pts, x1, z1, x2, z2):
    xm, zm = 0.5 * (x1 + x2), 0.5 * (z1 + z2)
    for (a, b, za, zb, g) in _groups:
        if a <= xm <= b and (a != b or min(za, zb) <= zm <= max(za, zb)): return g
    return _groups[-1][4]

def conveyance(pts, E, method="conveyance"):
    s = props(pts, E)
    A = sum(a for _, a, _ in s); P = sum(p for _, _, p in s)
    if method == "equal":
        nc = (sum(p * n ** 1.5 for n, _, p in s) / P) ** (2 / 3)
        return K / nc * A * (A / P) ** (2 / 3), A, P
    return sum(K / n * a * (a / p) ** (2 / 3) for n, a, p in s if a > 0), A, P

def top_width(pts, E, steps=STEPS):
    fine = resample(pts, steps)
    return sum(x2 - x1 for (x1, z1, _), (x2, z2, _) in zip(fine, fine[1:]) if 0.5 * (z1 + z2) < E)

def roots(f, lo, hi, n=4000):
    out = []
    xs = [lo + (hi - lo) * i / n for i in range(n + 1)]
    fs = [f(x) for x in xs]
    for i in range(n):
        if fs[i] == 0: out.append(xs[i])
        elif fs[i] * fs[i + 1] < 0:
            a, b, fa = xs[i], xs[i + 1], fs[i]
            for _ in range(60):
                m = 0.5 * (a + b); fm = f(m)
                if fa * fm <= 0: b = m
                else: a, fa = m, fm
            out.append(0.5 * (a + b))
    return out

def case(name, pts, S, method="conveyance", y=None, Q=None, search=12.0):
    set_groups(pts)
    zmin = min(p[1] for p in pts)
    print(f"--- {name}")
    if y is not None:
        Kt, A, P = conveyance(pts, zmin + y, method)
        print(f"y = {y}: A = {A:.8f}, P = {P:.8f}, T = {top_width(pts, zmin + y):.8f}, Q = {Kt * sqrt(S):.8f}")
    if Q is not None:
        global STEPS
        old = STEPS; STEPS = 20_000
        r = roots(lambda d: conveyance(pts, zmin + d, method)[0] * sqrt(S) - Q, 1e-6, search, 400)
        STEPS = old
        # polish each root with the fine resolution
        pol = []
        for r0 in r:
            a, b = r0 - 0.02, r0 + 0.02
            fa = conveyance(pts, zmin + a, method)[0] * sqrt(S) - Q
            for _ in range(40):
                m = 0.5 * (a + b); fm = conveyance(pts, zmin + m, method)[0] * sqrt(S) - Q
                if fa * fm <= 0: b = m
                else: a, fa = m, fm
            pol.append(0.5 * (a + b))
        good = [v for v in pol if abs(conveyance(pts, zmin + v, method)[0] * sqrt(S) - Q) <= 1e-3 * Q]
        steps = [v for v in pol if v not in good]
        print(f"Q = {Q}: normal depth roots = {[round(v, 8) for v in good]}"
              + (f"; Q(y) steps across Q (not a root) at {[round(v, 6) for v in steps]}" if steps else ""))

N1 = 0.013
fc1 = [(0, 5, N1), (10, 0, N1), (20, 0, N1), (30, 5, N1)]
case("I1 trapezoid b=10 z=2 as points", fc1, 0.01, y=1.5)

cmp = [(0, 8, .06), (40, 5, .06), (50, 5, .035), (55, 0, .035), (65, 0, .035), (70, 5, .035), (80, 5, .06), (120, 8, .06)]
case("I2 compound, conveyance subdivision", cmp, 0.001, y=6.0)
case("I2 compound, conveyance subdivision, solve yn", cmp, 0.001, Q=1500.0)
case("I3 compound, equal velocity", cmp, 0.001, method="equal", y=6.0)

one = [(x, z, .035) for x, z, _ in cmp]
case("I4 compound, single n (non-monotonic Q)", one, 0.001, y=5.05)
case("I4 compound, single n, two roots (Q drops when the flat overbanks wet at y = 5)", one, 0.001, Q=180.0, search=7.0)

pool = [(0, 3, .03), (5, 0, .03), (10, 2, .03), (15, 0.5, .03), (20, 3, .03)]
case("I5 two pools", pool, 0.002, y=1.5)

wall = [(0, 2, .025), (4, 0, .025), (8, 0, .025), (12, 3, .025)]
case("I6 water above left end (wall extended)", wall, 0.004, y=2.5)
