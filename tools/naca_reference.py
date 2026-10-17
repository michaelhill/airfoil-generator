"""Reference NACA 4/5-digit implementation (mirrors the FeatureScript math) and
comparison against UIUC coordinate files.  Usage: python3 -I naca_reference.py <uiuc dir>"""
import sys, re, glob, os
import numpy as np

K5 = {1: (0.0580, 361.4), 2: (0.1260, 51.64), 3: (0.2025, 15.957), 4: (0.2900, 6.643), 5: (0.3910, 3.230)}

def thickness(x, t, closed=True):
    a4 = -0.1036 if closed else -0.1015
    return 5*t*(0.2969*np.sqrt(x) - 0.1260*x - 0.3516*x**2 + 0.2843*x**3 + a4*x**4)

def camber4(x, m, p):
    if m == 0 or p == 0: return np.zeros_like(x), np.zeros_like(x)
    f = x < p
    yc = np.where(f, m/p**2*(2*p*x - x**2), m/(1-p)**2*((1-2*p) + 2*p*x - x**2))
    dy = np.where(f, 2*m/p**2*(p - x), 2*m/(1-p)**2*(p - x))
    return yc, dy

def camber5(x, cl, pidx):
    r, k1 = K5[pidx]; k1 *= cl/0.3
    f = x < r
    yc = np.where(f, k1/6*(x**3 - 3*r*x**2 + r*r*(3-r)*x), k1*r**3/6*(1-x))
    dy = np.where(f, k1/6*(3*x**2 - 6*r*x + r*r*(3-r)), -k1*r**3/6)
    return yc, dy

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

def parse(name):
    d = re.search(r'(\d{4,5})', name).group(1)
    if len(d) == 4:
        m, p, t = int(d[0])/100, int(d[1])/10, int(d[2:])/100
        return (lambda x: camber4(x, m, p)), t
    return (lambda x: camber5(x, 0.15*int(d[0]), int(d[1]))), int(d[3:])/100

if __name__ == '__main__':
    for f in sorted(glob.glob(os.path.join(sys.argv[1], '*.dat'))):
        base = os.path.basename(f)
        if base.startswith('t_n0012') or base.startswith('t_n0009'): continue
        base = base.replace('t_', '')
        try: cam, t = parse(base)
        except Exception: continue
        up, lo = load(f); worst = {}
        for closed in (False, True):
            U, L = airfoil(2000, cam, t, closed)
            P = np.concatenate([np.column_stack(U)[::-1], np.column_stack(L)[1:]])
            worst[closed] = max(dist(P, q) for q in np.vstack([up, lo]))
        print(f"{base:14s} n={len(up)+len(lo):3d}  max dist/c  open-TE={worst[False]:.5f}  closed-TE={worst[True]:.5f}")
