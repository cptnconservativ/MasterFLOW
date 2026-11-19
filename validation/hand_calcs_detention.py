"""Independent check of detention pond routing (MasterFlow DetentionTool / Pond).

Does NOT reuse the engine's formulas for storage or routing:
  * Storage is integrated numerically from the surface area A(h) (Simpson's rule, 2000 panels), not from
    the closed-form prismoidal volume.
  * Routing is done two ways:
      (1) the textbook storage-indication table method: tabulate 2S/dt + O at 0.001 ft, then interpolate
          the table each step (Chow, Maidment & Mays 1988, §8.2), dt = 1 min;
      (2) the level-pool ODE dS/dt = I(t) - O(h) solved by 4th-order Runge-Kutta in stage, dt = 0.5 s.
    The two should agree with each other and with MasterFlow to well under 1 %.
U.S. customary units, g = 32.174 ft/s².

Case: rectangular basin 100 ft x 50 ft at elevation 100.0, side slopes 4H:1V, top 106.0.
Outlets: 6 in. orifice at 100.0 (C 0.6); 3 ft circular riser crest 103.0 (weir C 3.0, orifice C 0.6);
emergency spillway crest 105.0, bottom 20 ft, side slopes 4H:1V, C 2.6. No outlet pipe.
Inflow: triangular hydrograph 0 cfs at 0 min, 120 cfs at 30 min, 0 at 90 min.
"""
from math import pi, sqrt, tan
G = 32.174
L, W, Z, BOT, TOP = 100.0, 50.0, 4.0, 100.0, 106.0


def area(h):
    return (L + 2 * Z * h) * (W + 2 * Z * h)


def storage(e):
    h = max(0.0, e - BOT)
    if h == 0:
        return 0.0
    n = 2000
    dx = h / n
    s = area(0) + area(h) + sum((4 if i % 2 else 2) * area(i * dx) for i in range(1, n))
    return s * dx / 3


def orifice(e, inv, d, c):
    h = e - inv
    if h <= 0:
        return 0.0
    a = pi * d * d / 4
    if h < d:   # weir flow below the crown: (2/3) C sqrt(2g) B h^1.5, B = A/D
        return 2 / 3 * c * sqrt(2 * G) * (a / d) * h ** 1.5
    return c * a * sqrt(2 * G * (h - d / 2))


def riser(e, crest, d, cw, co):
    h = e - crest
    if h <= 0:
        return 0.0
    return min(cw * pi * d * h ** 1.5, co * pi * d * d / 4 * sqrt(2 * G * h))


def spillway(e, crest, b, z, c):
    h = e - crest
    if h <= 0:
        return 0.0
    return c * (b * h ** 1.5 + 0.8 * z * h ** 2.5)


def outflow(e):
    return orifice(e, 100.0, 0.5, 0.6) + riser(e, 103.0, 3.0, 3.0, 0.6) + spillway(e, 105.0, 20.0, 4.0, 2.6)


def inflow(tmin):
    if tmin <= 0 or tmin >= 90:
        return 0.0
    return 120 * tmin / 30 if tmin <= 30 else 120 * (90 - tmin) / 60


# ---------- (1) storage-indication table method ----------
DT = 60.0
elev = [BOT + 0.001 * i for i in range(int((TOP - BOT) / 0.001) + 1)]
# cumulative storage on the fine grid by the trapezoid rule on A (fine enough to match Simpson's to 1e-6)
S = [0.0]
for i in range(1, len(elev)):
    S.append(S[-1] + (area(elev[i - 1] - BOT) + area(elev[i] - BOT)) / 2 * 0.001)
O = [outflow(e) for e in elev]
IND = [2 * s / DT + o for s, o in zip(S, O)]


def interp(x, xs, ys):
    lo, hi = 0, len(xs) - 1
    while hi - lo > 1:
        m = (lo + hi) // 2
        if xs[m] <= x:
            lo = m
        else:
            hi = m
    t = (x - xs[lo]) / (xs[hi] - xs[lo])
    return ys[lo] + t * (ys[hi] - ys[lo])


s_prev, o_prev = 0.0, 0.0
peak_o, peak_e, t_peak = 0.0, BOT, 0.0
for k in range(1, 600):
    i1, i2 = inflow((k - 1)), inflow(k)
    rhs = i1 + i2 + 2 * s_prev / DT - o_prev
    e = interp(rhs, IND, elev)
    s_prev, o_prev = interp(rhs, IND, S), interp(rhs, IND, O)
    if o_prev > peak_o:
        peak_o, t_peak = o_prev, k
    peak_e = max(peak_e, e)
print("Storage-indication table method (dt = 1 min):")
print(f"  peak outflow {peak_o:.3f} cfs at {t_peak} min, peak WSE {peak_e:.4f} ft, storage {storage(peak_e):.1f} ft3")
si = (peak_o, peak_e, t_peak)

# ---------- (2) Runge-Kutta on the level-pool ODE in stage: dh/dt = (I - O) / A ----------
h, t, dt = 0.0, 0.0, 0.5
peak_o2, peak_e2, t_peak2 = 0.0, BOT, 0.0
f = lambda tt, hh: (inflow(tt / 60) - outflow(BOT + max(hh, 0))) / area(max(hh, 0))
while t < 600 * 60:
    k1 = f(t, h); k2 = f(t + dt / 2, h + dt / 2 * k1); k3 = f(t + dt / 2, h + dt / 2 * k2); k4 = f(t + dt, h + dt * k3)
    h = max(0.0, h + dt / 6 * (k1 + 2 * k2 + 2 * k3 + k4)); t += dt
    o = outflow(BOT + h)
    if o > peak_o2:
        peak_o2, t_peak2 = o, t / 60
    peak_e2 = max(peak_e2, BOT + h)
print("Runge-Kutta level-pool ODE (dt = 0.5 s):")
print(f"  peak outflow {peak_o2:.3f} cfs at {t_peak2:.1f} min, peak WSE {peak_e2:.4f} ft")

print("Closed-form spot values:")
print(f"  storage at 103.0 (Simpson) = {storage(103.0):.3f} ft3")
print(f"  outflow at 104.0 = {outflow(104.0):.4f} cfs (orifice {orifice(104.0, 100, 0.5, 0.6):.4f}, riser {riser(104.0, 103, 3, 3, 0.6):.4f})")
print(f"  outflow at 105.5 = {outflow(105.5):.4f} cfs (riser {riser(105.5, 103, 3, 3, 0.6):.4f}, spillway {spillway(105.5, 105, 20, 4, 2.6):.4f})")
print(f"  v-notch 90 deg, C 2.5, h 0.8 ft = {2.5 * tan(pi / 4) * 0.8 ** 2.5:.4f} cfs")
