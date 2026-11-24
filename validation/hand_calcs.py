"""Independent reference calculations for MasterFlow validation.

Written separately from the C# engine (different language, closed forms where they exist, and plain
bisection elsewhere) so that the engine is not validated against itself. Run: python3 hand_calcs.py
"""
from math import sqrt

US = dict(k=1.486, g=32.174)
SI = dict(k=1.0, g=9.80665)

def trap(b, zl, zr, y):
    A = y * (b + 0.5 * (zl + zr) * y)
    P = b + y * (sqrt(1 + zl * zl) + sqrt(1 + zr * zr))
    T = b + (zl + zr) * y
    return A, P, T

def manning_q(b, zl, zr, y, n, S, k):
    A, P, _ = trap(b, zl, zr, y)
    return k / n * A * (A / P) ** (2 / 3) * sqrt(S)

def bisect(f, lo, hi, it=300):
    flo = f(lo)
    for _ in range(it):
        mid = 0.5 * (lo + hi)
        fm = f(mid)
        if (fm > 0) == (flo > 0):
            lo, flo = mid, fm
        else:
            hi = mid
    return 0.5 * (lo + hi)

def normal_depth(b, zl, zr, Q, n, S, k):
    return bisect(lambda y: manning_q(b, zl, zr, y, n, S, k) - Q, 1e-12, 1e4)

def critical_depth(b, zl, zr, Q, g):
    def f(y):
        A, _, T = trap(b, zl, zr, y)
        return A ** 3 / T - Q * Q / g
    return bisect(f, 1e-12, 1e4)

def report(name, b, zl, zr, n, S, units, Q=None, y=None):
    k, g = units['k'], units['g']
    if y is None:
        y = normal_depth(b, zl, zr, Q, n, S, k)
    if Q is None:
        Q = manning_q(b, zl, zr, y, n, S, k)
    A, P, T = trap(b, zl, zr, y)
    V = Q / A
    yc = critical_depth(b, zl, zr, Q, g)
    Ac, Pc, Tc = trap(b, zl, zr, yc)
    Sc = (Q * n / (k * Ac * (Ac / Pc) ** (2 / 3))) ** 2
    Fr = V / sqrt(g * A / T)
    print(f"{name}: y={y:.10f} Q={Q:.10f} A={A:.10f} P={P:.10f} R={A/P:.10f} T={T:.10f} "
          f"V={V:.10f} Fr={Fr:.10f} E={y + V*V/(2*g):.10f} yc={yc:.10f} Sc={Sc:.10e}")

if __name__ == "__main__":
    # Closed-form checks
    q = 50.0 / 10.0
    print("RECT closed-form yc (b=10 ft, Q=50 cfs):", (q * q / US['g']) ** (1 / 3))
    z, Q = 3.0, 20.0
    print("TRI closed-form yc (z=3, Q=20 cfs):", (2 * Q * Q / (US['g'] * z * z)) ** 0.2)
    n, S = 0.015, 0.002
    yn = (Q * n / (US['k'] * z * sqrt(S)) * (2 * sqrt(1 + z * z) / z) ** (2 / 3)) ** 0.375
    print("TRI closed-form yn (z=3, Q=20, n=0.015, S=0.002):", yn)

    report("CASE R1 rect basic Q", 10, 0, 0, 0.013, 0.001, US, y=2.0)
    report("CASE R2 rect yn", 10, 0, 0, 0.013, 0.001, US, Q=50.0)
    report("CASE T1 trap b=10 z=2 supercritical", 10, 2, 2, 0.013, 0.01, US, y=1.5)
    report("CASE T2 trap Chow Ex.6-2", 20, 2, 2, 0.025, 0.0016, US, Q=400.0)
    report("CASE T3 trap asym", 4, 3, 1.5, 0.030, 0.005, US, Q=120.0)
    report("CASE V1 tri yn", 0, 3, 3, 0.015, 0.002, US, Q=20.0)
    report("CASE V2 tri asym", 0, 4, 1, 0.016, 0.02, US, Q=5.0)
    report("CASE S1 rect SI", 3, 0, 0, 0.015, 0.001, SI, y=1.0)
    report("CASE S2 trap SI yn", 2, 1.5, 1.5, 0.022, 0.0005, SI, Q=8.0)
    report("CASE B1 rect tiny Q", 10, 0, 0, 0.013, 0.001, US, Q=1e-4)
    report("CASE B2 trap huge Q", 50, 3, 3, 0.035, 0.0002, US, Q=1.0e5)
    report("CASE B3 steep", 6, 0, 0, 0.013, 0.20, US, Q=300.0)
