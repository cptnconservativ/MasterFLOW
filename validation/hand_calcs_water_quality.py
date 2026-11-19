"""Independent hand calculations for the water quality tool (MasterFlow WaterQualityTool, sample input, U.S. units).

1. WQCV, MHFD USDCM Vol. 3 Eq. 3-1: WQCV = a (0.91 i^3 - 1.19 i^2 + 0.78 i), watershed inches.
2. EDB orifice plate: rows every 4 in. from the bottom, Cd 0.6; the hole area per row found by explicit time
   stepping of dS/dt = -Q (small steps, no use of the 1/a scaling), so 99 % of the design volume drains in 40 h.
3. Darcy filter area and infiltration bottom area.
"""
import math

g = 32.174

def wqcv_in(i, a):
    return a * (0.91 * i**3 - 1.19 * i**2 + 0.78 * i)

# ---- 1. Pond 1: A1 6.2 ac 65 %, A2 3.4 ac 45 %
area = 6.2 + 3.4
imp = (6.2 * 0.65 + 3.4 * 0.45) / area
w = wqcv_in(imp, 1.0)
vol = w / 12 * area * 43560
print(f"Pond 1 composite i = {imp:.6f}, WQCV = {w:.6f} in = {vol:.3f} cf; design (x1.2) = {1.2*vol:.3f} cf")
print(f"Rain garden (B1 1.1 ac 90 %, a 0.8): {wqcv_in(0.90, 0.8):.6f} in = {wqcv_in(0.90, 0.8)/12*1.1*43560:.3f} cf")

# ---- 2. Stage-storage (conic) for the sample pond
stages = [(100.0, 4000.0), (101.0, 6500.0), (102.0, 9200.0), (103.0, 12000.0), (104.0, 15000.0)]
def storage(e):
    v = 0.0
    for (e1, a1), (e2, a2) in zip(stages, stages[1:]):
        if e <= e1:
            break
        h = min(e, e2) - e1
        H = e2 - e1
        s1, s2 = math.sqrt(a1), math.sqrt(a2)
        ae = (s1 + (s2 - s1) * h / H) ** 2
        v += h / 3 * (a1 + ae + math.sqrt(a1 * ae))
    return v
def level(v):
    lo, hi = stages[0][0], stages[-1][0]
    for _ in range(100):
        m = 0.5 * (lo + hi)
        if storage(m) < v: lo = m
        else: hi = m
    return 0.5 * (lo + hi)

design = 1.2 * vol
wq = level(design)
rows = []
k = 0
while 100.0 + k * 4 / 12 < wq - 1e-9:
    rows.append(100.0 + k * 4 / 12); k += 1
def q(e, a):
    return sum(0.6 * a * math.sqrt(2 * g * (e - z)) for z in rows if e > z)
def drain_hours(a, frac=0.99, dt=2.0):
    s, t = design, 0.0
    target = design * (1 - frac)
    while s > target:
        # Heun's method on storage, dt seconds
        e = level(s); q1 = q(e, a)
        s2 = s - dt * q1; q2 = q(level(max(s2, 0)), a)
        s_new = s - dt * 0.5 * (q1 + q2)
        if s_new <= target:
            t += dt * (s - target) / (s - s_new); break
        s = s_new; t += dt
    return t / 3600
# Secant search on a (sq ft) for 40 h
a0, a1 = 0.005, 0.02
t0, t1 = drain_hours(a0, dt=20) - 40, drain_hours(a1, dt=20) - 40
for _ in range(20):
    a2 = a1 - t1 * (a1 - a0) / (t1 - t0)
    a0, t0, a1 = a1, t1, a2
    t1 = drain_hours(a1, dt=20) - 40
    if abs(t1) < 1e-4: break
print(f"WQCV level = {wq:.4f} ft, rows = {len(rows)}, hole area per row = {a1*144:.4f} sq in "
      f"(diameter {math.sqrt(4*a1/math.pi)*12:.4f} in), 99 % drain = {drain_hours(a1, dt=5):.3f} h, 50 % drain = {drain_hours(a1, frac=0.5, dt=5):.3f} h")

# ---- 3. Filters and infiltration
def darcy(v, df, k, h, tf_days): return v * df / (k * (h + df) * tf_days)
rg = wqcv_in(0.90, 0.8) / 12 * 1.1 * 43560
print(f"Rain garden: Darcy area = {darcy(rg, 1.5, 0.5, 0.5, 2.0):.3f} sf, storage area = {rg/1.0:.3f} sf")
sfv = wqcv_in(0.80, 0.8) / 12 * 0.8 * 43560
print(f"Sand filter: V = {sfv:.3f} cf, Darcy area = {darcy(sfv, 1.5, 3.5, 1.0, 40/24):.3f} sf, storage area = {sfv/2.0:.3f} sf")
tv = wqcv_in(0.95, 0.8) / 12 * 0.5 * 43560
f = 1.0 / 2 / 12
print(f"Trench: V = {tv:.3f} cf, bottom = {tv/(0.4*3):.3f} sf, drain = {3*0.4/f:.2f} h, max depth = {f*72/0.4:.3f} ft")
