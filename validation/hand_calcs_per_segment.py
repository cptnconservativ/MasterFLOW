"""Independent hand calculation: per-segment conveyance on sample 6 (creek with overbanks).

Every segment between consecutive points is its own subarea (vertical dividers at the points);
the n of a segment is the n of its left point. K = sum (k/n_i) A_i R_i^(2/3), k = 1.486.
Normal depth for Q = 500 cfs, S = 0.001 by bisection. Written separately from the C# engine.
"""
import math

PTS = [(0, 108, .06), (40, 105, .06), (50, 105, .035), (55, 100, .035),
       (65, 100, .035), (70, 105, .06), (80, 105, .06), (120, 108, None)]
K_US, Q, S, INVERT = 1.486, 500.0, 0.001, 100.0


def wet(x1, z1, x2, z2, w):
    d1, d2 = w - z1, w - z2
    if d1 <= 0 and d2 <= 0:
        return 0.0, 0.0
    if d1 > 0 and d2 > 0:
        return (d1 + d2) / 2 * (x2 - x1), math.hypot(x2 - x1, z2 - z1)
    f = d1 / (d1 - d2) if d1 > 0 else d2 / (d2 - d1)
    return max(d1, d2) * (x2 - x1) * f / 2, math.hypot(x2 - x1, z2 - z1) * f


def section(y):
    w = INVERT + y
    K = A = P = 0.0
    count = 0
    for (x1, z1, n), (x2, z2, _) in zip(PTS, PTS[1:]):
        a, p = wet(x1, z1, x2, z2, w)
        if a > 0:
            K += K_US / n * a * (a / p) ** (2 / 3)
            count += 1
        A += a
        P += p
    return K, A, P, count


lo, hi = 0.5, 7.99
for _ in range(300):
    mid = (lo + hi) / 2
    if section(mid)[0] * math.sqrt(S) < Q:
        lo = mid
    else:
        hi = mid
K, A, P, count = section(lo)
n_eff = K_US * A * (A / P) ** (2 / 3) / K
print(f"PS1 per-segment creek: yn={lo:.10f} A={A:.10f} P={P:.10f} n_eff={n_eff:.10f} subareas={count}")
