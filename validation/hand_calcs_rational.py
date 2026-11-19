"""Independent hand calculation of the rational method with flow paths and network routing
(Rapid City IDCM 2012 §4.2, TR-55, Manning). Written from the equations, not from the C# code.
Run: python3 validation/hand_calcs_rational.py > validation/hand_calcs_rational_output.txt
"""
import math

# IDCM Table 4-2 depths (in.)
DUR = [5, 15, 60, 120, 180, 360, 720, 1440]
DEP = {2: [0.36, 0.69, 1.05, 1.20, 1.25, 1.55, 1.70, 2.00],
       10: [0.53, 1.04, 1.86, 1.98, 2.10, 2.35, 2.70, 3.07],
       100: [0.79, 1.57, 2.95, 3.06, 3.15, 3.58, 4.05, 4.55]}

def depth(storm, t):
    for k in range(1, len(DUR)):
        if t <= DUR[k]:
            f = math.log(t / DUR[k-1]) / math.log(DUR[k] / DUR[k-1])
            return DEP[storm][k-1] * (DEP[storm][k] / DEP[storm][k-1]) ** f
    raise ValueError

def inten(storm, t):
    return depth(storm, t) / (t / 60)

# Table 4-1 (C2, C5, C10, C100)
C = {"SFR": (0.40, 0.45, 0.50, 0.60), "Paved": (0.87, 0.88, 0.90, 0.93),
     "Comm": (0.87, 0.87, 0.88, 0.89), "LawnClay": (0.05, 0.10, 0.20, 0.40)}
IDX = {2: 0, 5: 1, 10: 2, 100: 3}

def comp(parts, storm):
    a = sum(x for _, x in parts)
    return sum(C[u][IDX[storm]] * x for u, x in parts) / a

def overland(c5, L, s_pct):          # IDCM Eq 4-2-4
    return 1.87 * (1.1 - c5) * L ** 0.5 * s_pct ** -0.33

print("Basin A1 (CB-3): SFR 2.4 ac + paved 0.6 ac; flow path overland, shallow unpaved, gutter, pipe")
A1 = [("SFR", 2.4), ("Paved", 0.6)]
c5 = comp(A1, 5)
t1 = overland(c5, 100, 2.0)
v2 = 16.1345 * math.sqrt(0.015); t2 = 250 / v2 / 60          # TR-55 unpaved
n, s, sx, T = 0.016, 0.012, 0.02, 6.0
v3 = (0.56 / n * sx ** 1.67 * s ** 0.5 * T ** 2.67) / (sx * T * T / 2); t3 = 400 / v3 / 60
v4 = 1.486 / 0.013 * (15 / 12 / 4) ** (2 / 3) * math.sqrt(0.008); t4 = 150 / v4 / 60
ti, tt = t1, t2 + t3 + t4
cap = (100 + 250 + 400 + 150) / 180 + 10
tcA1 = max(min(ti + tt, cap), 5)
print(f"  C5 = {c5:.6f}  t_overland = {t1:.6f}  V_shallow = {v2:.6f} t = {t2:.6f}")
print(f"  V_gutter = {v3:.6f} t = {t3:.6f}  V_pipe = {v4:.6f} t = {t4:.6f}")
print(f"  ti + tt = {ti + tt:.6f}  cap L/180+10 = {cap:.6f}  tc = {tcA1:.6f}")

print("Basin A2 (CB-4): commercial 1.1 + lawn clay 0.3, overland 80 ft @ 1.5 %, tt 2.5, L 600")
A2 = [("Comm", 1.1), ("LawnClay", 0.3)]
tiA2 = overland(comp(A2, 5), 80, 1.5)
tcA2 = max(min(tiA2 + 2.5, 600 / 180 + 10), 5)
print(f"  ti = {tiA2:.6f}  tc = {tcA2:.6f}")

print("Basin A3 (MH-2): paved 0.4, sheet 60 ft @ 2 % n 0.011 P2 = 2.00, shallow paved 200 ft @ 1 %")
A3 = [("Paved", 0.4)]
tsheet = 60 * 0.007 * (0.011 * 60) ** 0.8 / (2.00 ** 0.5 * 0.02 ** 0.4)
vsp = 20.3282 * math.sqrt(0.01); tsp = 200 / vsp / 60
tcA3 = max(min(tsheet + tsp, 260 / 180 + 10), 5)
print(f"  t_sheet = {tsheet:.6f}  t_shallow = {tsp:.6f}  tc = {tcA3:.6f}")

basins = {"A1": (A1, tcA1, "CB-3"), "A2": (A2, tcA2, "CB-4"), "A3": (A3, tcA3, "MH-2")}
for storm in (10, 2):
    print(f"\n{storm}-yr storm")
    for b, (parts, tc, st) in basins.items():
        a = sum(x for _, x in parts); cc = comp(parts, storm)
        print(f"  {b}: C = {cc:.6f}  CA = {cc*a:.6f}  i = {inten(storm, tc):.6f}  Q = {cc*inten(storm, tc)*a:.6f}")
    # pipes (n 0.013): P2 CB-3->MH-2 D1.5 L150 S=0.9/150 ; P3 CB-4->MH-2 D1.25 L60 S=0.5/60 ; P1 MH-2->OUT D2 L200 S=0.8/200
    def vfull(d, s): return 1.486 / 0.013 * (d / 4) ** (2 / 3) * math.sqrt(s)
    def CA(b): parts = basins[b][0]; return comp(parts, storm) * sum(x for _, x in parts)
    def Qb(b): return CA(b) * inten(storm, basins[b][1])
    q2 = Qb("A1"); q3 = Qb("A2")
    tP2 = 150 / vfull(1.5, 0.9 / 150) / 60; tP3 = 60 / vfull(1.25, 0.5 / 60) / 60
    tcP1 = max(tcA3, tcA1 + tP2, tcA2 + tP3)
    caP1 = CA("A1") + CA("A2") + CA("A3")
    qP1 = max(caP1 * inten(storm, tcP1), q2, q3, Qb("A3"))
    print(f"  P2: Q = {q2:.6f}  V = {vfull(1.5, 0.9/150):.6f}  travel = {tP2:.6f}")
    print(f"  P3: Q = {q3:.6f}  V = {vfull(1.25, 0.5/60):.6f}  travel = {tP3:.6f}")
    print(f"  P1: sumCA = {caP1:.6f}  tc = max({tcA3:.4f}, {tcA1 + tP2:.4f}, {tcA2 + tP3:.4f}) = {tcP1:.6f}  i = {inten(storm, tcP1):.6f}  Q = {qP1:.6f}")
    print(f"      (sum of peaks would be {q2 + q3 + Qb('A3'):.6f})")
