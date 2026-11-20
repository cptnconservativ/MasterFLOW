"""Independent checks for the hydrograph network routing tool (premium).

1. TR-55 (1986) Example 3-1 time of concentration, by the TR-55 equations.
2. NRCS curve-number runoff, closed form.
3. Rapid City 100-yr 2-h frequency storm (IDCM Table 4-2), a 100 ac CN 80 subbasin, tc 30 min, NRCS unit
   hydrograph, 1-min step: peak and volume, by an independent convolution.
4. Reach routing: the same hydrograph through a 10,000 ft rectangular channel (b 20 ft, n 0.035, S 0.002),
   solved here as a diffusion wave (dQ/dt + c dQ/dx = D d2Q/dx2, c = dQ/dA, D = Q/(2 T S)) on a fine grid
   (dx 25 ft, dt 0.5 s). Muskingum-Cunge is built to reproduce this equation, so the peaks should agree within a few %.
"""
import math

# 1. TR-55 Example 3-1 (Worksheet 3): sheet n 0.24, L 100, P2 3.6, s 0.01; shallow unpaved 1400 ft s 0.01;
#    channel n 0.05, R = 27/28.2, s 0.005, L 7300. TR-55 reports 0.30 + 0.24 + 0.99 = 1.53 h.
sheet = 0.007 * (0.24 * 100) ** 0.8 / (3.6 ** 0.5 * 0.01 ** 0.4)
shallow = 1400 / (16.1345 * 0.01 ** 0.5) / 3600
channel = 7300 / (1.49 * (27 / 28.2) ** (2 / 3) * 0.005 ** 0.5 / 0.05) / 3600
print(f"TR-55 Ex 3-1: sheet {sheet:.4f} h, shallow {shallow:.4f} h, channel {channel:.4f} h, tc {sheet+shallow+channel:.4f} h")

# 2. CN runoff
def cn_runoff(p, cn):
    s = 1000 / cn - 10; ia = 0.2 * s
    return 0 if p <= ia else (p - ia) ** 2 / (p - ia + s)
print(f"CN 80, P 5 in: Q = {cn_runoff(5, 80):.4f} in (by hand (5-0.5)^2/(5+2) = {20.25/7:.4f})")

# 3. storm
dur = [5, 15, 60, 120, 180, 360, 720, 1440]
d100 = [0.79, 1.57, 2.95, 3.06, 3.15, 3.58, 4.05, 4.55]
def depth(d):
    for i in range(1, len(dur)):
        if d <= dur[i] + 1e-9:
            f = math.log(d / dur[i-1]) / math.log(dur[i] / dur[i-1])
            return math.exp(math.log(d100[i-1]) + f * math.log(d100[i] / d100[i-1]))
n = 24
cum = [depth(5 * (k + 1)) for k in range(n)]
inc = sorted([cum[0]] + [cum[k] - cum[k-1] for k in range(1, n)], reverse=True)
blocks = [0.0] * n
pk = int(math.floor(0.25 * n)); L, R = pk - 1, pk + 1; blocks[pk] = inc[0]; right = True
for x in inc[1:]:
    if (right and R < n) or L < 0: blocks[R] = x; R += 1
    else: blocks[L] = x; L -= 1
    right = not right
print("blocks (in):", " ".join(f"{b:.3f}" for b in blocks), f" total {sum(blocks):.3f}")
dt = 1.0; T = 480
rain = [0.0]
for k in range(1, T + 1):
    b = min((k - 1) // 5, n - 1) if k <= 120 else None
    rain.append(rain[-1] + (blocks[b] / 5 if b is not None else 0))
exc = [cn_runoff(p, 80) for p in rain]
area = 100.0; lag = 0.6 * 30 / 60; dth = dt / 60; tp = dth / 2 + lag; qp = 484 * area / 640 / tp
tab = [(0,0),(.1,.03),(.2,.1),(.3,.19),(.4,.31),(.5,.47),(.6,.66),(.7,.82),(.8,.93),(.9,.99),(1,1),(1.1,.99),(1.2,.93),(1.3,.86),(1.4,.78),(1.5,.68),(1.6,.56),(1.7,.46),(1.8,.39),(1.9,.33),(2,.28),(2.2,.207),(2.4,.147),(2.6,.107),(2.8,.077),(3,.055),(3.2,.04),(3.4,.029),(3.6,.021),(3.8,.015),(4,.011),(4.5,.005),(5,0)]
def dl(x):
    for i in range(1, len(tab)):
        if x <= tab[i][0]: return tab[i-1][1] + (tab[i][1]-tab[i-1][1])*(x-tab[i-1][0])/(tab[i][0]-tab[i-1][0])
    return 0
m = int(math.ceil(5 * tp / dth)) + 1
uh = [qp * dl((j + 1) * dth / tp) for j in range(m)]
scale = (area * 43560 / 12) / (sum(uh) * dth * 3600); uh = [u * scale for u in uh]
q = [0.0] * (T + 1)
for k in range(1, T + 1):
    pe = exc[k] - exc[k-1]
    for j in range(m):
        if k + j <= T: q[k + j] += pe * uh[j]
kp = max(range(T + 1), key=lambda i: q[i])
vol = sum((q[i] + q[i+1]) / 2 * 60 for i in range(T)) / 43560
print(f"Subbasin: rain {rain[-1]:.4f} in, runoff {exc[-1]:.4f} in, UH peak/in (raw 484) {qp:.3f} cfs, scale {scale:.4f}")
print(f"Subbasin hydrograph: peak {q[kp]:.3f} cfs at {kp} min, volume {vol:.4f} ac-ft (runoff x area = {exc[-1]*area/12:.4f})")

# 4. diffusion wave through a rectangular channel
b, nn, S, Lr = 20.0, 0.035, 0.002, 10000.0
def Qy(y): a = b*y; p = b + 2*y; return 1.486/nn*a*(a/p)**(2/3)*S**0.5
def yQ(Q):
    lo, hi = 0, 50
    for _ in range(60):
        mid = (lo+hi)/2
        if Qy(mid) < Q: lo = mid
        else: hi = mid
    return (lo+hi)/2
def cel(Q):
    y = yQ(max(Q, 1e-3)); h = 1e-4*max(y, .01)
    return (Qy(y+h)-Qy(y-h))/(b*2*h)
qin = lambda t: q[min(int(t // 60), T-1)] + (q[min(int(t//60)+1, T)] - q[min(int(t // 60), T-1)]) * ((t/60) % 1)
# Finite-volume diffusion wave (conservative): cells of 50 ft hold area A; the flux between cells is Manning flow
# driven by the water-surface slope, Q = 1.486/n A R^(2/3) sqrt(S0 - dy/dx); the last face is at normal depth.
# Volume is conserved exactly; explicit, dt 0.5 s (stable: D dt / dx^2 < 0.5).
dx = 50.0; N = int(Lr/dx); Acell = [0.0]*N; dts = 0.5; t = 0.0; out = []
def manning(a, sf):
    if a <= 0 or sf <= 0: return 0.0
    y = a/b; R = a/(b+2*y); return 1.486/nn*a*R**(2/3)*sf**0.5
while t < T*60 - 1e-9:
    t += dts
    y = [a/b for a in Acell]
    flux = [qin(t)]
    for i in range(N-1):
        sf = S - (y[i+1]-y[i])/dx
        a = Acell[i] if sf >= 0 else Acell[i+1]
        flux.append(manning(a, abs(sf)) * (1 if sf >= 0 else -1))
    flux.append(manning(Acell[N-1], S))
    for i in range(N): Acell[i] += dts/dx*(flux[i]-flux[i+1])
    if abs(t/60 - round(t/60)) < 1e-9: out.append((round(t/60), flux[N]))
ko = max(out, key=lambda x: x[1])
vout = sum((out[i][1]+out[i+1][1])/2*60 for i in range(len(out)-1))/43560
print(f"Diffusion wave: outflow peak {ko[1]:.3f} cfs at {ko[0]} min (inflow peak {q[kp]:.3f} at {kp} min), volume out by {T} min {vout:.4f} ac-ft, left in channel {sum(Acell)*dx/43560:.4f} ac-ft")
