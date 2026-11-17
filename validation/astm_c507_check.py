"""Independent check of ASTM C507-22 Fig. 5 elliptical pipe geometry.

Builds the four-centred shape from rise, span and R1 (R2 derived from tangency) and integrates the
area numerically (midpoint rule on half-width, 200k strips), then compares with the tabulated
full-flow area. Also reports the tangency-derived R2 vs the tabulated R2.
"""
from math import sqrt, pi
from fractions import Fraction as Fr

def f(s):
    s = s.strip(); parts = s.split()
    return float(sum(Fr(p) for p in parts))

ROWS = """15|1.29|12|19|5 1/4|16 1/4
18|1.83|14 1/4|22 3/4|6|20
24|3.28|19 1/4|30 1/4|8 1/4|26 1/4
27|4.12|21 1/2|34|9 1/4|29 1/4
30|5.10|24|37 3/4|10 1/4|32 3/4
33|6.33|26 3/4|42|11 1/2|36 1/4
36|7.36|28 3/4|45 1/2|12 1/4|39 1/4
39|8.78|31 1/2|49 1/2|13 1/2|42 3/4
42|10.2|34|53 1/4|14 1/2|46
48|12.9|38 1/4|60|16 1/2|51 1/2
54|16.7|43 1/2|68|18 3/4|58 1/2
60|20.5|48 1/4|75 1/2|20 3/4|65
66|24.8|53|83|22 3/4|71 1/2
72|29.4|57 3/4|90 1/2|24 3/4|78
78|34.6|62 3/4|98|27|84 1/2
84|40.1|67 1/2|105 1/2|29|90 3/4
90|46.1|72 1/2|113|31|97 1/4
96|52.4|77 1/4|120 1/2|33 1/4|103 3/4
102|59.1|82|128|35 1/4|110
108|66.4|87|135 1/2|37 1/2|116 1/4
114|73.9|91 3/4|143|39 1/2|122 3/4
120|82.1|96 3/4|150 3/4|41 1/2|129 1/4
132|99.2|106 1/2|165 1/2|45 3/4|142
144|118|116|180 3/4|50|154 3/4"""

def half_width_he(y, a, b, r, R):
    # centre at origin, horizontal major axis; y relative to centre
    yt = r * (R - b) / (R - r)
    if abs(y) <= yt:
        return (a - r) + sqrt(max(0.0, r * r - y * y))
    cy = (R - b) if y < 0 else (b - R)
    return sqrt(max(0.0, R * R - (y - cy) ** 2))

def area(a, b, r, R, n=200000):
    h = 2 * b / n
    return sum(2 * half_width_he(-b + (i + 0.5) * h, a, b, r, R) for i in range(n)) * h

print("size  table_ft2  area_R2derived  err%   area_R2table  err%   R2table  R2derived")
for line in ROWS.splitlines():
    size, A, rise, span, r1, r2 = line.split("|")
    A = float(A); rise, span, r1, r2 = f(rise), f(span), f(r1), f(r2)
    a, b = span / 2, rise / 2
    Rd = ((a - r1) ** 2 + b * b - r1 * r1) / (2 * (b - r1))
    Ad = area(a, b, r1, Rd) / 144
    At = area(a, b, r1, r2) / 144
    print(f"{size:>4} {A:9.2f} {Ad:14.4f} {100*(Ad/A-1):6.2f} {At:13.4f} {100*(At/A-1):6.2f} {r2:8.3f} {Rd:9.3f}")
