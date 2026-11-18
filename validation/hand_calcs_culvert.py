"""Independent reference values for FHWA HDS-5 (2012) culvert computations.

Written separately from the C# engine, straight from the published equations:
  Inlet control, unsubmerged (Ku Q/(A D^0.5) <= 3.5):
    Form 1  HW/D = Hc/D + K (Ku Q/(A D^0.5))^M + Ks S,   Hc = dc + Vc^2/2g
    Form 2  HW/D = K (Ku Q/(A D^0.5))^M
  Inlet control, submerged (>= 4.0):  HW/D = c (Ku Q/(A D^0.5))^2 + Y + Ks S
  Outlet control: HW = ho + (1 + Ke + Kf) V^2/2g - S L,  Kf = 2 g n^2 L / (k^2 R^(4/3)),
                  ho = TW if TW >= D else max(TW, (dc + D)/2)
  Roadway weir: Q = Cd L h^1.5
Critical depth by 300-step bisection on Q^2/g = A^3/T. Run: python3 hand_calcs_culvert.py
"""
from math import acos, sin, pi, sqrt
K, G = 1.486, 32.174

def circ(D, y):
    th = 2 * acos(1 - 2 * y / D)
    return D * D / 8 * (th - sin(th)), D * sin(th / 2)

def bisect(f, lo, hi, it=300):
    flo = f(lo)
    for _ in range(it):
        m = 0.5 * (lo + hi); fm = f(m)
        if (fm > 0) == (flo > 0): lo, flo = m, fm
        else: hi = m
    return 0.5 * (lo + hi)

def dc_circ(D, Q):
    return bisect(lambda y: circ(D, y)[0] ** 3 / circ(D, y)[1] - Q * Q / G, 1e-9 * D, D * (1 - 1e-12))

def inlet_circ(D, Q, S, K_, M, c, Y, Ks=-0.5, Ku=1.0):
    A = pi * D * D / 4
    X = Ku * Q / (A * sqrt(D))
    dc = dc_circ(D, Q)
    Ac, Tc = circ(D, dc)
    Hc = dc + (Q / Ac) ** 2 / (2 * G)
    unsub = D * (Hc / D + K_ * X ** M + Ks * S)
    sub = D * (c * X * X + Y + Ks * S)
    return X, dc, Hc, unsub, sub

out = []
def p(*a): s = " ".join(str(x) for x in a); print(s); out.append(s)

p("A. 48-in concrete pipe, square edge with headwall (K 0.0098, M 2.0, c 0.0398, Y 0.67, Ks -0.5)")
p("   n 0.012, L 200 ft, inverts 100.00 / 99.00 (S 0.005), Ke 0.5")
D, S, L, n, Ke = 4.0, 0.005, 200.0, 0.012, 0.5
for Q in (30.0, 120.0):
    X, dc, Hc, unsub, sub = inlet_circ(D, Q, S, 0.0098, 2.0, 0.0398, 0.67)
    p(f"   Q {Q:6.1f}: X {X:.6f}  dc {dc:.6f}  Hc {Hc:.6f}  HWi unsub {unsub:.6f}  HWi sub {sub:.6f}")
# transition at Q = 90 (X = 3.581)
A = pi * D * D / 4
q35, q40 = 3.5 * A * sqrt(D), 4.0 * A * sqrt(D)
h35 = inlet_circ(D, q35, S, 0.0098, 2.0, 0.0398, 0.67)[3]
h40 = inlet_circ(D, q40, S, 0.0098, 2.0, 0.0398, 0.67)[4]
X90 = 90 / (A * sqrt(D))
p(f"   Q   90.0: X {X90:.6f}  HW(3.5) {h35:.6f}  HW(4.0) {h40:.6f}  HWi transition {h35 + (X90 - 3.5) / 0.5 * (h40 - h35):.6f}")
R = D / 4
Kf = 2 * G * n * n * L / (K * K * R ** (4 / 3))
p(f"   Kf = 2gn^2L/(k^2 R^4/3) = {Kf:.6f}   (29 n^2 L / R^1.33 = {29 * n * n * L / R ** 1.33:.6f})")
for Q, TW in ((120.0, 5.0), (120.0, 2.0)):
    V = Q / A; hv = V * V / (2 * G)
    dc = dc_circ(D, Q)
    ho = TW if TW >= D else max(TW, (dc + D) / 2)
    H = (1 + Ke + Kf) * hv
    p(f"   Q {Q:6.1f}, TW {TW}: V {V:.6f}  hv {hv:.6f}  H {H:.6f}  ho {ho:.6f}  HWo {ho + H - S * L:.6f}")

p("")
p("B. 6 ft x 4 ft concrete box, S 0.01")
B, D2, S2, Q = 6.0, 4.0, 0.01, 100.0
A2 = B * D2; X = Q / (A2 * sqrt(D2))
p(f"   Form 2, 90-deg headwall 3/4-in chamfers (K 0.515, M 0.667): Q {Q}: X {X:.6f}  HWi {D2 * 0.515 * X ** 0.667:.6f}")
dcb = ((Q / B) ** 2 / G) ** (1 / 3)
Hc = 1.5 * dcb
p(f"   Form 1, 30-75 deg wingwalls (K 0.026, M 1.0): dc {dcb:.6f}  Hc {Hc:.6f}  HWi {D2 * (Hc / D2 + 0.026 * X ** 1.0 - 0.5 * S2):.6f}")
Q3 = 250.0; X3 = Q3 / (A2 * sqrt(D2))
p(f"   Submerged, 30-75 deg wingwalls (c 0.0347, Y 0.81): Q {Q3}: X {X3:.6f}  HWi {D2 * (0.0347 * X3 ** 2 + 0.81 - 0.5 * S2):.6f}")

p("")
p("C. Roadway: level crest 100 ft long at 107.50, Cd 3.0, headwater 108.12: Q = Cd L h^1.5 =", f"{3.0 * 100 * 0.62 ** 1.5:.6f}")
p("   Villemonte kt for ht/h = 0.8:", f"{(1 - 0.8 ** 1.5) ** 0.385:.6f}")

p("")
p("D. Same 48-in culvert in SI (D 1.2192 m, Q 120 cfs = 3.398022 m3/s, Ku 1.811): submerged HWi")
Dm = 4 * 0.3048; Qm = 120 * 0.3048 ** 3
Am = pi * Dm * Dm / 4
Xm = 1.811 * Qm / (Am * sqrt(Dm))
p(f"   X {Xm:.6f}  HWi {Dm * (0.0398 * Xm * Xm + 0.67 - 0.5 * S):.6f} m = {Dm * (0.0398 * Xm * Xm + 0.67 - 0.5 * S) / 0.3048:.6f} ft")

open(__file__.replace(".py", "_output.txt"), "w").write("\n".join(out) + "\n")
