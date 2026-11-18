"""Independent check of MasterFlow's storm sewer HGL/EGL (StormNetworkSolver).

Written separately from the C# engine: circular-pipe geometry from closed forms, bisection instead of
Brent, a 10x finer standard step, and the Rapid City IDCM (2012) Figure 4-6/4-7 structure losses typed in
from the manual. Run:  python3 validation/storm_hgl.py > validation/storm_hgl_output.txt
Tolerances (set before comparing): HGL/EGL 0.002 ft; velocities 0.001 ft/s.
"""
import math

K, G = 1.486, 32.174

def geom(D, y):
    if y >= D:
        return math.pi * D * D / 4, math.pi * D, 0.0
    y = max(y, 1e-12 * D)
    th = 2 * math.acos(1 - 2 * y / D)
    A = D * D / 8 * (th - math.sin(th))
    P = D * th / 2
    T = D * math.sin(th / 2)
    return A, P, T

def bisect(f, lo, hi, it=200):
    flo = f(lo)
    for _ in range(it):
        mid = 0.5 * (lo + hi); fm = f(mid)
        if (fm > 0) == (flo > 0): lo, flo = mid, fm
        else: hi = mid
    return 0.5 * (lo + hi)

class Pipe:
    def __init__(s, name, frm, to, D, L, inv_up, inv_dn, n, Q):
        s.name, s.frm, s.to, s.D, s.L, s.up, s.dn, s.n, s.Q = name, frm, to, D, L, inv_up, inv_dn, n, Q
        s.S0 = (inv_up - inv_dn) / L
        s.yc = bisect(lambda y: Q * Q * geom(D, y)[2] / (G * geom(D, y)[0] ** 3) - 1, 1e-9 * D, D * (1 - 1e-9))
    def A(s, y): return geom(s.D, y)[0]
    def E(s, y): return y + (s.Q / s.A(y)) ** 2 / (2 * G)
    def Sf(s, y):
        A, P, _ = geom(s.D, y)
        return (s.Q * s.n / (K * A * (A / P) ** (2 / 3))) ** 2
    def z(s, x): return s.up - s.S0 * x
    def solve(s, ws_dn, steps=None):
        """Subcritical standard step upstream (sufficient for the validation networks, all mild/backwater)."""
        n = steps or max(400, int(math.ceil(s.L / (0.025 * s.D))))
        dx = s.L / n
        y = max(ws_dn - s.z(s.L), s.yc)
        y_dn = y
        for i in range(n, 0, -1):
            x, xu = i * dx, (i - 1) * dx
            target = s.z(x) + s.E(y); sf = s.Sf(y)
            f = lambda yy: s.z(xu) + s.E(yy) - target - 0.5 * (sf + s.Sf(yy)) * dx
            assert f(s.yc) <= 0, "supercritical: not covered by this script"
            hi = max(y, s.D) + 10
            y = bisect(f, s.yc, hi)
        return dict(hgl_up=s.z(0) + y, egl_up=s.z(0) + s.E(y), v_up=s.Q / s.A(y),
                    hgl_dn=s.z(s.L) + y_dn, egl_dn=s.z(s.L) + s.E(y_dn), v_dn=s.Q / s.A(y_dn))

def run(title, pipes, tw_rule, tw=None, cases=None, rims=None):
    print(f"== {title}")
    by_to = {}
    for p in pipes: by_to.setdefault(p.to, []).append(p)
    out_of = {p.frm: p for p in pipes}
    outfall = [s for s in by_to if s not in out_of][0]
    res, struct = {}, {}
    def upstream(name, outp):
        r = res[outp.name]; hv2 = r['v_up'] ** 2 / (2 * G)
        ins = by_to.get(name, [])
        if not ins:
            h = 1.25 * hv2; struct[name] = (r['hgl_up'] + h, h); return
        c = cases[name]
        main = max(ins, key=lambda p: p.Q)
        v1 = main.Q / main.A(main.D)
        for _ in range(200):
            h = max(0.0, hv2 - c * v1 ** 2 / (2 * G))
            hgl = r['hgl_up'] + h
            rm = main.solve(hgl)
            if abs(rm['v_dn'] - v1) < 1e-12: break
            v1 = rm['v_dn']
        struct[name] = (hgl, h)
        res[main.name] = rm
        for p in ins:
            if p is not main: res[p.name] = p.solve(hgl)
        for p in ins: upstream(p.frm, p)
    for p in by_to[outfall]:
        ws = {'elev': tw, 'half': p.dn + 0.5 * (p.yc + p.D)}[tw_rule]
        res[p.name] = p.solve(ws)
        upstream(p.frm, p)
    for p in pipes:
        r = res[p.name]
        print(f"pipe {p.name}: yc={p.yc:.6f} HGLup={r['hgl_up']:.6f} HGLdn={r['hgl_dn']:.6f} EGLup={r['egl_up']:.6f} EGLdn={r['egl_dn']:.6f} Vup={r['v_up']:.6f} Vdn={r['v_dn']:.6f}")
    for s, (h, hj) in struct.items():
        clear = f" clearance={rims[s] - h:.6f}" if rims and s in rims else ""
        print(f"struct {s}: HGL={h:.6f} hj={hj:.6f}{clear}")

# Network A: submerged outfall, every pipe full (exact: HGL rises by Sf_full * L).
A = [Pipe("P1", "MH-2", "OUT", 2.0, 200, 100.80, 100.00, 0.013, 20),
     Pipe("P2", "CB-3", "MH-2", 1.5, 150, 102.00, 101.10, 0.013, 12),
     Pipe("P3", "CB-4", "MH-2", 1.25, 60, 101.80, 101.30, 0.013, 8)]
run("A: tailwater 102.50, pressure flow", A, 'elev', tw=102.50, cases={"MH-2": 0.25},
    rims={"MH-2": 106.0, "CB-3": 106.5, "CB-4": 104.0})

# Network B: same layout, lower flows, (yc + D)/2 at the outfall: part-full backwater profiles.
B = [Pipe("P1", "MH-2", "OUT", 2.0, 200, 100.40, 100.00, 0.013, 8),
     Pipe("P2", "CB-3", "MH-2", 1.5, 150, 101.45, 101.10, 0.013, 5),
     Pipe("P3", "CB-4", "MH-2", 1.25, 60, 101.50, 101.30, 0.013, 3)]
run("B: (yc+D)/2 tailwater, part-full", B, 'half', cases={"MH-2": 0.25},
    rims={"MH-2": 106.0, "CB-3": 106.5, "CB-4": 104.0})
