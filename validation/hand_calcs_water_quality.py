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


# ============================================================================================================
# Rapid City Storm Water Quality Manual (2009), the tool's default criteria (sample input, U.S. units).
# WQCV = 1.16 a (0.91 I^3 - 1.191 I^2 + 0.78 I) (SWQM 2.2.2); volume on the effective area (2.4.2 design credits).
# ============================================================================================================
print("\n---- Rapid City SWQM 2009")

def wqcv_rc(i, a):
    return 1.16 * a * (0.91 * i**3 - 1.191 * i**2 + 0.78 * i)

# Manual's worked examples (2.2.2 and 2.4.2)
print(f"SWQM example north: I 0.318, 40 h -> {wqcv_rc(0.318, 1.0):.4f} in (manual 0.182); 17.0 ac x 0.182/12 = {17.0*0.182/12:.3f} ac-ft (manual 0.258, from the unrounded 0.182)")
print(f"SWQM example south: I 0.365, 24 h -> {wqcv_rc(0.365, 0.9):.4f} in (manual 0.178); 6.05 ac x 0.178/12 = {6.05*0.178/12:.3f} ac-ft (manual 0.090)")
print(f"SWQM credits: north (17.0 - 1.79) x 0.182/12 = {(17.0-1.79)*0.182/12:.3f} ac-ft (manual 0.231); south (6.05 - 0.51) x 0.178/12 = {(6.05-0.51)*0.178/12:.3f} (manual 0.083)")

def conic_storage(stg, e):
    v = 0.0
    for (e1, a1), (e2, a2) in zip(stg, stg[1:]):
        if e <= e1:
            break
        h = min(e, e2) - e1
        s1, s2 = math.sqrt(a1), math.sqrt(a2)
        ae = (s1 + (s2 - s1) * h / (e2 - e1)) ** 2
        v += h / 3 * (a1 + ae + math.sqrt(a1 * ae))
    return v

def plate_design(stg, z0, vol_top, vol_drain, hours, spacing=4/12, cd=0.6):
    """Rows from z0; level holds vol_top above z0; hole area so vol_drain leaves in `hours` (explicit stepping, secant)."""
    s0 = conic_storage(stg, z0)
    S = lambda e: conic_storage(stg, e) - s0 if e > z0 else 0.0
    def lev(v):
        lo, hi = z0, stg[-1][0]
        for _ in range(50):
            m = 0.5 * (lo + hi)
            if S(m) < v: lo = m
            else: hi = m
        return 0.5 * (lo + hi)
    top = lev(vol_top)
    rws = []
    k = 0
    while z0 + k * spacing < top - 1e-9:
        rws.append(z0 + k * spacing); k += 1
    Q = lambda e, a: sum(cd * a * math.sqrt(2 * g * (e - z)) for z in rws if e > z)
    def drain(a, vd, dt=20.0):
        s, t, target = vol_top, 0.0, vol_top - vd
        while s > target:
            q1 = Q(lev(s), a); s2 = s - dt * q1; q2 = Q(lev(max(s2, 0)), a)
            sn = s - dt * 0.5 * (q1 + q2)
            if sn <= target:
                t += dt * (s - target) / (s - sn); break
            s = sn; t += dt
        return t / 3600
    # Bisection on log(a) (robust: drain time falls as a grows), then a final fine-step check.
    lo, hi = math.log(1e-5), math.log(1.0)
    for _ in range(40):
        m = 0.5 * (lo + hi)
        if drain(math.exp(m), vol_drain) > hours: lo = m
        else: hi = m
    a1 = math.exp(0.5 * (lo + hi))
    return top, len(rws), a1, drain(a1, vol_drain, dt=5), drain(a1, 0.5 * vol_drain, dt=5)

# EDB Pond 1: A1 6.2 ac 65 %, A2 3.4 ac 45 % with a 0.5 ac credit; 40 h (a = 1.0); storage 120 % WQCV; the WQCV leaves in 40 h.
imp = (6.2 * 0.65 + 3.4 * 0.45) / 9.6
w = wqcv_rc(imp, 1.0)
v = w / 12 * (9.6 - 0.5) * 43560
edb = [(100.0, 2000.0), (101.0, 3500.0), (102.0, 5200.0), (103.0, 7200.0), (104.0, 9500.0)]
top, nrows, a, t40, t50 = plate_design(edb, 100.0, 1.2 * v, v, 40.0)
print(f"EDB Pond 1: I = {imp:.6f}, WQCV = {w:.6f} in = {v:.3f} cf, design = {1.2*v:.3f} cf, WQCV level = {top:.4f} ft, rows = {nrows}, "
      f"hole area per row = {a*144:.4f} sq in, WQCV drains in {t40:.3f} h, half the WQCV in {t50:.3f} h (must be >= 12)")

# Constructed wetland W1: 8.0 ac 40 %, 24 h (a = 0.9); surcharge = WQCV above the pool at 100.0; 99 % of it in 24 h.
w = wqcv_rc(0.40, 0.9)
v = w / 12 * 8.0 * 43560
wet = [(97.0, 3000.0), (98.0, 5000.0), (99.0, 7000.0), (100.0, 9000.0), (101.0, 11000.0), (102.0, 13000.0)]
top, nrows, a, t24, t50 = plate_design(wet, 100.0, v, 0.99 * v, 24.0)
print(f"Wetland: WQCV = {w:.6f} in = {v:.3f} cf, pool volume = {conic_storage(wet, 100.0):.3f} cf ({100*conic_storage(wet,100.0)/v:.1f} % of WQCV), "
      f"surcharge level = {top:.4f} ft, rows = {nrows}, hole area per row = {a*144:.4f} sq in, 99 % in {t24:.3f} h")

# Storage-sized BMPs
v = wqcv_rc(0.90, 0.8) / 12 * 1.1 * 43560
print(f"Bioretention B1 (12 h assumed, 12 in. ponding): V = {v:.3f} cf, area = {v/1.0:.3f} sf, guideline 5-7 % x 0.85 x 1.1 ac = {0.05*0.85*1.1*43560:.1f} - {0.07*0.85*1.1*43560:.1f} sf")
v = wqcv_rc(0.80, 1.0) / 12 * 0.8 * 43560
print(f"Sand filter C1 (40 h, 3 ft): V = {v:.3f} cf, As = V/3 = {v/3:.3f} sf")
v = wqcv_rc(0.85, 0.8) / 12 * 0.6 * 43560
print(f"Porous landscape detention L1 (12 h, 9 in. average): V = {v:.3f} cf, area = {v/0.75:.3f} sf")
