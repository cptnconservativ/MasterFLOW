"""Independent hydraulics for ASTM C507 HE 36-in pipe and a true ellipse, using a fine polyline
(40,000 vertices per side) for area and wetted perimeter (no closed forms shared with the engine)."""
from math import sqrt, pi, cos, sin
K = 1.486

def he_halfwidth_factory(rise, span, r):
    a, b = span / 2, rise / 2
    R = ((a - r) ** 2 + b * b - r * r) / (2 * (b - r))
    yt = r * (R - b) / (R - r)
    def w(y):  # y from invert
        u = y - b
        if abs(u) <= yt: return (a - r) + sqrt(max(0, r * r - u * u))
        cy = (R - b) if u < 0 else (b - R)
        return sqrt(max(0, R * R - (u - cy) ** 2))
    return w, R

def ellipse_halfwidth(rise, span):
    a, b = span / 2, rise / 2
    return lambda y: a * sqrt(max(0, 1 - ((y - b) / b) ** 2))

def props(w, rise, y, n=40000):
    # sample with cosine spacing in [0, y]
    pts = []
    for i in range(n + 1):
        t = 0.5 - 0.5 * cos(pi * i / n)
        yy = y * t
        pts.append((w(yy), yy))
    A = 0.0; P = 0.0
    for (w0, y0), (w1, y1) in zip(pts, pts[1:]):
        A += (w0 + w1) * (y1 - y0)          # both halves: 2 * trapezoid/2
        P += 2 * sqrt((w1 - w0) ** 2 + (y1 - y0) ** 2)
    return A, P

def q(w, rise, y, n_, S):
    A, P = props(w, rise, y)
    return K / n_ * A * (A / P) ** (2 / 3) * sqrt(S)

def bisect(f, lo, hi, it=60):
    flo = f(lo)
    for _ in range(it):
        m = 0.5 * (lo + hi); fm = f(m)
        if (fm > 0) == (flo > 0): lo, flo = m, fm
        else: hi = m
    return 0.5 * (lo + hi)

if __name__ == "__main__":
    rise, span, r = 28.75 / 12, 45.5 / 12, 12.25 / 12
    w, R = he_halfwidth_factory(rise, span, r)
    Afull, Pfull = props(w, rise, rise)
    print(f"HE36 derived R2 = {R*12:.6f} in; Afull = {Afull:.8f} ft2; Pfull = {Pfull:.8f} ft")
    print(f"HE36 Q at y = 1.2 ft (n=0.013, S=0.005): {q(w, rise, 1.2, 0.013, 0.005):.8f} cfs")
    yn = bisect(lambda y: q(w, rise, y, 0.013, 0.005) - 20.0, 1e-6, 0.85 * rise)
    print(f"HE36 yn for Q = 20 cfs (n=0.013, S=0.005): {yn:.8f} ft")
    we = ellipse_halfwidth(2.0, 3.0)
    Ae, Pe = props(we, 2.0, 2.0)
    a, b = 1.5, 1.0
    h = ((a - b) / (a + b)) ** 2
    ram = pi * (a + b) * (1 + 3 * h / (10 + sqrt(4 - 3 * h)))
    print(f"Ellipse 2x3 ft: A = {Ae:.8f} (exact {pi*a*b:.8f}); P = {Pe:.8f} (Ramanujan II {ram:.10f})")
    print(f"Ellipse 2x3 ft Q at y = 0.8 (n=0.013, S=0.005): {q(we, 2.0, 0.8, 0.013, 0.005):.8f} cfs")
