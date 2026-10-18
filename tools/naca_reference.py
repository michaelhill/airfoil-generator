"""Reference NACA implementation (mirrors the FeatureScript math: 4-digit, 4-digit modified, 5-digit standard and
reflexed, 16-series, 6- and 6A-series) and comparison against coordinate files.

Usage:
  python3 -I tools/naca_reference.py <dir of UIUC .dat files>
      max distance from each file's points to the computed section (file name gives the designation, e.g.
      naca2412.dat, naca23012.dat, naca64a010.dat, n63412.dat; see uiuc_designation()).
  python3 -I tools/naca_reference.py --naca456 <dir of naca456 sample .out files>
      thickness and mean line against naca456's own output tables (first table of each file).

The 6-series epsilon / psi tables are read from airfoil_wing.fs, so the two implementations share one copy.
"""
import sys, re, glob, os, math, pathlib
import numpy as np

FS = (pathlib.Path(__file__).resolve().parent.parent / "airfoil_wing.fs").read_text()


def fs_table(name):
    body = re.search(r"const " + name + r" = \[(.*?)\];", FS, re.S).group(1)
    rows = re.findall(r"\[([^\[\]]*)\]", body)
    return np.array([[float(v) for v in re.findall(r"-?\d+\.?\d*(?:e-?\d+)?", r)] for r in rows]) if rows \
        else np.array([float(v) for v in re.findall(r"-?\d+\.?\d*(?:e-?\d+)?", body)])


SIX_FAMILIES = ["63", "64", "65", "66", "67", "63A", "64A", "65A"]
SIX_EPS, SIX_PSI, SIX_DEPS, SIX_DPSI = (fs_table(n) for n in ("SIX_EPS", "SIX_PSI", "SIX_DEPS", "SIX_DPSI"))
SIX_SCALE = fs_table("SIX_SCALE")
FOUR_MOD_D1 = fs_table("FOUR_MOD_D1")

K5 = {1: (0.0580, 361.4), 2: (0.1260, 51.64), 3: (0.2025, 15.957), 4: (0.2900, 6.643), 5: (0.3910, 3.230)}
K5R = {2: (0.1300, 51.990, 0.000764), 3: (0.2170, 15.793, 0.00677), 4: (0.3180, 6.520, 0.0303), 5: (0.4410, 3.191, 0.1355)}

def thickness(x, t, closed=True):
    a4 = -0.1036 if closed else -0.1015
    return 5*t*(0.2969*np.sqrt(x) - 0.1260*x - 0.3516*x**2 + 0.2843*x**3 + a4*x**4)

def camber4(x, m, p):
    if m == 0 or p == 0: return np.zeros_like(x), np.zeros_like(x)
    f = x < p
    yc = np.where(f, m/p**2*(2*p*x - x**2), m/(1-p)**2*((1-2*p) + 2*p*x - x**2))
    dy = np.where(f, 2*m/p**2*(p - x), 2*m/(1-p)**2*(p - x))
    return yc, dy

def camber5(x, cl, pidx, reflex=False):
    if reflex:
        r, k1, k21 = K5R[pidx]; k1 *= cl/0.3; tail = k21*(1-r)**3 + r**3
        f = x < r
        yc = np.where(f, k1/6*((x-r)**3 - tail*x + r**3), k1/6*(k21*(x-r)**3 - tail*x + r**3))
        dy = np.where(f, k1/6*(3*(x-r)**2 - tail), k1/6*(3*k21*(x-r)**2 - tail))
        return yc, dy
    r, k1 = K5[pidx]; k1 *= cl/0.3
    f = x < r
    yc = np.where(f, k1/6*(x**3 - 3*r*x**2 + r*r*(3-r)*x), k1*r**3/6*(1-x))
    dy = np.where(f, k1/6*(3*x**2 - 6*r*x + r*r*(3-r)), -k1*r**3/6)
    return yc, dy

def hermite(y0, y1, d0, d1, h, t):
    return (2*t**3 - 3*t**2 + 1)*y0 + (t**3 - 2*t**2 + t)*h*d0 + (-2*t**3 + 3*t**2)*y1 + (t**3 - t**2)*h*d1


def six_thickness(family, tc):
    """[x], [half thickness] of a 6/6A-series family (index into SIX_FAMILIES) by conformal mapping."""
    scale = np.polyval(SIX_SCALE[family][::-1], tc)
    knot = math.pi / 20
    phi = np.linspace(0, math.pi, 201)
    k = np.minimum((phi / knot).astype(int), 19); t = phi / knot - k
    eps = scale * hermite(SIX_EPS[family][k], SIX_EPS[family][k + 1], SIX_DEPS[family][k], SIX_DEPS[family][k + 1], knot, t)
    psi = scale * hermite(SIX_PSI[family][k], SIX_PSI[family][k + 1], SIX_DPSI[family][k], SIX_DPSI[family][k + 1], knot, t)
    r = np.exp(psi); ang = phi - eps
    re_, im = (r + 1/r) * np.cos(ang), (r - 1/r) * np.sin(ang)
    span = re_[0] - re_[-1]
    return (re_[0] - re_) / span, im / span


def four_modified_thickness(x, tc, le_index, m):
    d1 = np.polyval(FOUR_MOD_D1[::-1], m)
    rle = 1.1019 / 36 * (tc * le_index) ** 2
    a0 = (0.2 / tc) * math.sqrt(2 * rle)
    a3 = 0.1 / m**3 + (d1 * (1 - m) - 0.294) / (m * (1 - m)**2) - 0.375 * a0 / m**2.5
    a2 = -0.1 / m**2 + 0.5 * a0 / m**1.5 - 2 * a3 * m
    a1 = -0.5 * a0 / math.sqrt(m) - 2 * a2 * m - 3 * a3 * m * m
    d3 = (3 * d1 - 0.588 / (1 - m)) / (3 * (1 - m)**2)
    d2 = -1.5 * (1 - m) * d3 - 0.5 * d1 / (1 - m)
    x = np.asarray(x, float); u = 1 - x
    fore = a0 * np.sqrt(np.maximum(x, 0)) + x * (a1 + x * (a2 + x * a3))
    aft = 0.002 + u * (d1 + u * (d2 + u * d3))
    return 5 * tc * np.where(x < m, fore, aft)


def six_mean_line(a, cl, x):
    """[height], [slope] of the 6-series a= mean line; a < 0 means the modified a=0.8 line of the 6A-series."""
    x = np.asarray(x, float)
    if a < 0:
        y, s = six_mean_line(0.8, cl, x); te = -0.24521 * cl
        return np.where(x > 0.86, te * (x - 1), 0.97948 * y), np.where(x > 0.86, te, 0.97948 * s)
    y = np.zeros_like(x); s = np.zeros_like(x)
    ok = (x > 1e-7) & (1 - x > 1e-7); xx = x[ok]; omx = 1 - xx
    if 1 - a < 1e-7:
        y[ok] = -(omx*np.log(omx) + xx*np.log(xx)) * cl / (4*math.pi)
        s[ok] = (np.log(omx) - np.log(xx)) * cl / (4*math.pi)
        return y, s
    oma = 1 - a
    g = -0.25 if a < 1e-7 else -(a*a*(0.5*math.log(a) - 0.25) + 0.25) / oma
    h = -0.5 if a < 1e-7 else g + (0.5*oma*oma*math.log(oma) - 0.25*oma*oma) / oma
    amx = a - xx; lam = np.log(np.maximum(abs(amx), 1e-300))
    t1 = np.where(abs(amx) > 1e-7, amx*amx*(2*lam - 1), 0); t1p = np.where(abs(amx) > 1e-7, -amx*lam, 0)
    t2 = omx*omx*(1 - 2*np.log(omx)); t2p = omx*np.log(omx)
    sc = cl / (2*math.pi*(a + 1))
    y[ok] = sc * (0.25*(t1 + t2)/oma - xx*np.log(xx) + g - h*xx)
    s[ok] = sc * ((t1p + t2p)/oma - 1 - np.log(xx) - h)
    return y, s


def pchip_at(x, y, xs):
    h = np.diff(x); d = np.diff(y) / h; n = len(x); s = np.zeros(n)
    for k in range(1, n - 1):
        if d[k-1] * d[k] > 0:
            w1, w2 = 2*h[k] + h[k-1], h[k] + 2*h[k-1]; s[k] = (w1 + w2) / (w1/d[k-1] + w2/d[k])
    def end(h0, h1, d0, d1):
        v = ((2*h0 + h1)*d0 - h0*d1) / (h0 + h1)
        return 0.0 if v*d0 <= 0 else (3*d0 if d0*d1 <= 0 and abs(v) > 3*abs(d0) else v)
    s[0] = end(h[0], h[1], d[0], d[1]); s[-1] = end(h[-1], h[-2], d[-1], d[-2])
    k = np.clip(np.searchsorted(x, xs) - 1, 0, n - 2); hh = x[k+1] - x[k]
    return hermite(y[k], y[k+1], s[k], s[k+1], hh, (xs - x[k]) / hh)


def parse_designation(text):
    """Mirror of parseNaca: returns (thickness(x, closed) -> half thickness, camber(x) -> (y, slope))."""
    a_match = re.search(r"[aA]=([0-9.]+)", text)
    a = float(a_match.group(1)) if a_match else None
    core = re.sub(r"[aA]=[0-9.]+", "", text)
    core = re.sub(r"(?<=\d)[aA]", "A", re.sub(r"^\s*NACA", "", core, flags=re.I))
    core = re.sub(r"[^0-9A-]", "", core)
    def six(fam, cl, tc, default_a):
        xt, yt = six_thickness(SIX_FAMILIES.index(fam), tc)
        return (lambda x, closed: pchip_at(xt, yt, x)), (lambda x: six_mean_line(default_a if a is None else a, cl, x))
    m = re.fullmatch(r"(\d{4})-(\d)(\d)", core)
    if m:
        d = m.group(1); tc = int(d[2:]) / 100
        return (lambda x, closed: four_modified_thickness(x, tc, int(m.group(2)), int(m.group(3)) / 10)), \
            (lambda x: camber4(x, int(d[0]) / 100, int(d[1]) / 10))
    m = re.fullmatch(r"16-(\d)(\d\d)", core)
    if m:
        cl, tc = int(m.group(1)) / 10, int(m.group(2)) / 100
        return (lambda x, closed: four_modified_thickness(x, tc, 4, 0.5)), (lambda x: six_mean_line(1.0 if a is None else a, cl, x))
    m = re.fullmatch(r"(6[3-7])(A?)\d*-(\d)(\d\d)", core) or re.fullmatch(r"(6[3-5])(A)(\d)(\d\d)", core)
    if m:
        return six(m.group(1) + m.group(2), int(m.group(3)) / 10, int(m.group(4)) / 100, -1 if m.group(2) else 1.0)
    m = re.fullmatch(r"(6[3-7])\d(\d)(\d\d)", core)
    if m:
        return six(m.group(1), int(m.group(2)) / 10, int(m.group(3)) / 100, 1.0)
    if re.fullmatch(r"\d{4}", core):
        mm, p, t = int(core[0])/100, int(core[1])/10, int(core[2:])/100
        return (lambda x, closed: thickness(x, t, closed)), (lambda x: camber4(x, mm, p))
    if re.fullmatch(r"\d{5}", core):
        cl, pidx, refl, t = 0.15*int(core[0]), int(core[1]), core[2] == "1", int(core[3:])/100
        return (lambda x, closed: thickness(x, t, closed)), (lambda x: camber5(x, cl, pidx, refl))
    raise ValueError("unknown designation " + text)


def section(text, n, closed=True):
    """Upper and lower surface points of a designation, cosine spaced, as the FeatureScript builds them."""
    thick, camber = parse_designation(text)
    x = 0.5 * (1 - np.cos(np.linspace(0, np.pi, n)))
    yt = np.asarray(thick(x, closed), float)
    if not re.fullmatch(r"\s*(NACA)?\s*\d{4,5}\s*", text, re.I):   # 6-series / modified: pinch or open like the FS
        edge = yt[-1]
        shift = -edge if closed else (0.0025 / 2 - edge if 2 * edge < 0.0025 else 0)
        yt = yt + shift * x
    yc, dy = camber(x); th = np.arctan(dy)
    return (x - yt*np.sin(th), yc + yt*np.cos(th)), (x + yt*np.sin(th), yc - yt*np.cos(th))


def airfoil(n, camber, t, closed=True):
    beta = np.linspace(0, np.pi, n); x = 0.5*(1-np.cos(beta))
    yt = thickness(x, t, closed); yc, dy = camber(x); th = np.arctan(dy)
    up = (x - yt*np.sin(th), yc + yt*np.cos(th)); lo = (x + yt*np.sin(th), yc - yt*np.cos(th))
    return up, lo

def load(path):
    rows = []
    for l in open(path).read().splitlines()[1:]:
        v = re.findall(r'-?\d*\.\d+|-?\d+', l)
        if len(v) == 2: rows.append((float(v[0]), float(v[1])))
    a = np.array([r for r in rows if abs(r[0]) <= 1.5 and abs(r[1]) <= 1.5])
    i = np.argmin(a[:, 0]); return a[:i+1], a[i:]   # upper TE->LE, lower LE->TE

def dist(P, q):
    # distance from point q to polyline P
    a, b = P[:-1], P[1:]; ab = b - a
    tt = np.clip(np.sum((q - a)*ab, 1)/np.maximum(np.sum(ab*ab, 1), 1e-18), 0, 1)
    return np.min(np.linalg.norm(a + tt[:, None]*ab - q, axis=1))

def uiuc_designation(name):
    """Designation from a UIUC file name: naca2412, naca23012, naca63206 (= 63-206), naca652415 (= 652-415),
    naca64a010, naca16012 (= 16-012), naca001064 (= 0010-64). Returns None when unknown."""
    stem = re.sub(r"\.dat$", "", name.lower()).replace("t_", "")
    m = re.fullmatch(r"(?:naca|n)(\w+)", stem)
    if not m:
        return None
    d = m.group(1)
    if re.fullmatch(r"6[3-5]a\d{3}", d):
        return d[:2] + "A" + d[3:]
    if re.fullmatch(r"\d{6}", d):
        return d[:3] + "-" + d[3:] if re.match(r"6[3-7]", d) else d[:4] + "-" + d[4:]
    if re.fullmatch(r"\d{5}", d):
        if d.startswith("16"):
            return "16-" + d[2:]
        if re.match(r"6[3-7]", d):
            return d[:2] + "-" + d[2:]
        return d
    if re.fullmatch(r"\d{4}", d):
        return d
    return None


def compare_naca456(directory):
    """Thickness and mean line against the first table of each naca456 sample output."""
    for path in sorted(glob.glob(os.path.join(directory, "*.out"))):
        rows, tc, cl, header = [], None, 0.0, ""
        for line in open(path):
            if "t/c=" in line and tc is None:
                tc = float(line.split("t/c=")[1].split()[0]); header = line
            if "cl=" in line:
                cl = float(line.split("cl=")[1].split()[0])
            p = line.split()
            if len(p) in (4, 6) and p[0].isdigit():
                try:
                    rows.append([float(v) for v in p[1:]] + [0.0, 0.0][:6 - len(p)])
                except ValueError:
                    pass
            elif rows:
                break
        if not rows or tc is None:
            continue
        rows = np.array(rows); x = rows[:, 0]
        fam = re.search(r"family=\s*(\S+)", header)
        if fam and fam.group(1).upper() in SIX_FAMILIES:
            xt, yt = six_thickness(SIX_FAMILIES.index(fam.group(1).upper()), tc)
            yt_x = pchip_at(xt, yt, x)
            yc = six_mean_line(-1 if "A" in fam.group(1).upper() else 1.0, cl, x)[0]
        else:
            continue
        print(f"{os.path.basename(path):12s} t/c {tc:.3f} cl {cl:.1f}  thickness err {np.max(abs(yt_x - rows[:, 1])):.6f}"
              f"  mean line err {np.max(abs(yc - rows[:, 3])) if rows[:, 3].any() else 0:.6f}")

if __name__ == '__main__':
    if sys.argv[1] == "--naca456":
        compare_naca456(sys.argv[2])
        sys.exit()
    for f in sorted(glob.glob(os.path.join(sys.argv[1], '*.dat'))):
        base = os.path.basename(f)
        if base.startswith('t_n0012') or base.startswith('t_n0009'): continue
        designation = uiuc_designation(base)
        if designation is None: continue
        up, lo = load(f); worst = {}
        for closed in (False, True):
            U, L = section(designation, 2000, closed)
            P = np.concatenate([np.column_stack(U)[::-1], np.column_stack(L)[1:]])
            worst[closed] = max(dist(P, q) for q in np.vstack([up, lo]))
        print(f"{base:16s} {designation:9s} n={len(up)+len(lo):3d}  max dist/c  open-TE={worst[False]:.5f}  closed-TE={worst[True]:.5f}")
