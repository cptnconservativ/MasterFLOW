"""Independent reference values for partially full circular pipes (Manning).

Geometry: theta = 2 acos(1 - 2y/D); A = D^2/8 (theta - sin theta); P = D theta / 2; T = D sin(theta/2).
Roots by 300-step bisection; maximum by dense scan + golden refinement. Run: python3 hand_calcs_circular.py
"""
from math import acos, sin, pi, sqrt
K, G = 1.486, 32.174

def geom(D, y):
    th = 2 * acos(1 - 2 * y / D)
    return D * D / 8 * (th - sin(th)), D * th / 2, D * sin(th / 2)

def q(D, y, n, S):
    A, P, _ = geom(D, y)
    return K / n * A * (A / P) ** (2 / 3) * sqrt(S)

def bisect(f, lo, hi, it=300):
    flo = f(lo)
    for _ in range(it):
        m = 0.5 * (lo + hi); fm = f(m)
        if (fm > 0) == (flo > 0): lo, flo = m, fm
        else: hi = m
    return 0.5 * (lo + hi)

def qmax(D, n, S):
    a, b = 0.5 * D, D
    gr = (sqrt(5) - 1) / 2
    for _ in range(300):
        c, d = b - gr * (b - a), a + gr * (b - a)
        if q(D, c, n, S) > q(D, d, n, S): b = d
        else: a = c
    y = 0.5 * (a + b)
    return y, q(D, y, n, S)

def case(name, D, n, S, Qt=None, y=None):
    Af = pi * D * D / 4
    Qf = K / n * Af * (D / 4) ** (2 / 3) * sqrt(S)
    ym, Qm = qmax(D, n, S)
    if y is None:
        y1 = bisect(lambda yy: q(D, yy, n, S) - Qt, 1e-12, ym)
        y2 = bisect(lambda yy: q(D, yy, n, S) - Qt, ym, D) if Qt > Qf else None
    else:
        Qt = q(D, y, n, S); y1, y2 = y, None
    A, P, T = geom(D, y1)
    yc = bisect(lambda yy: geom(D, yy)[0] ** 1.5 / sqrt(geom(D, yy)[2]) - Qt / sqrt(G), 1e-12, D * (1 - 1e-15))
    Sfull = (Qt * n / (K * Af * (D / 4) ** (2 / 3))) ** 2
    V = Qt / A
    print(f"{name}: D={D} Q={Qt:.10f} y1={y1:.10f} y2={y2 if y2 is None else f'{y2:.10f}'} A={A:.10f} P={P:.10f} T={T:.10f} "
          f"V={V:.10f} Fr={V/sqrt(G*A/T):.10f} yc={yc:.10f} Qfull={Qf:.10f} Sfull={Sfull:.10e} Qmax={Qm:.10f} y_Qmax={ym:.10f} "
          f"ratio_Qmax_Qfull={Qm/Qf:.8f} ymax_over_D={ym/D:.8f}")

if __name__ == "__main__":
    case("P1 half full", 2.0, 0.013, 0.005, y=1.0)
    case("P2 36-in pipe Q from y", 3.0, 0.013, 0.01, y=2.73)
    case("P3 36-in pipe dual depth", 3.0, 0.013, 0.01, Qt=71.37)
    case("P4 typical storm sewer yn", 1.5, 0.013, 0.004, Qt=4.0)
    case("P5 nearly empty", 1.5, 0.013, 0.004, Qt=1e-4)
    case("P6 SI-like steep", 0.6, 0.012, 0.05, Qt=1.0)
