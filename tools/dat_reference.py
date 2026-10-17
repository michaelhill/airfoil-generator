"""Reference for pasted airfoil coordinates (mirrors parseCoordinates / coordinateSurfaces in airfoil_wing.fs).

The text is handled the way it arrives from an Onshape text field: line breaks may be gone, so the airfoil's name
and the numbers run together. Coordinates are the numbers with a decimal point; leading ones outside +-1.5 (a
"12.5%" in the name) are skipped, except the two point counts that start a Lednicer file.

Usage: python3 -I tools/dat_reference.py <dir of .dat files>
Prints, per file: format, points per side, and the largest distance (fraction of chord) from the original points
to the resampled section (fine and 40-point spacing, polyline through the points) - the resampling error, without
the trailing-edge closing / opening.
"""
import glob
import os
import sys

import numpy as np

COORD_CHARS = set("0123456789.-+eE")
MIN_OPEN_GAP = 0.0025   # NACA open trailing edge, total thickness at x = 1
CHECK_POINTS = 400      # fine grid for checking whether the linear trailing-edge pinch crosses
RAMP_START = 0.9        # fallback closing: thickness tapers to zero from here to the trailing edge


def tokens(text):
    """Runs of number characters that contain a decimal point, as (start index, value)."""
    out, i = [], 0
    while i < len(text):
        if text[i] in COORD_CHARS:
            j = i
            while j < len(text) and text[j] in COORD_CHARS:
                j += 1
            tok = text[i:j]
            if "." in tok:
                try:
                    out.append((i, float(tok)))
                except ValueError:
                    pass
            i = j
        else:
            i += 1
    return out


def parse(text):
    """Returns (name, format, upper LE->TE, lower LE->TE) with x, y normalised to unit chord, or raises ValueError."""
    toks = tokens(text)
    vals = [v for _, v in toks]
    # Skip leading numbers outside +-1.5 (from the name), but stop at two whole-number counts that match the number
    # of values after them: that is a Lednicer file (upper then lower surface, each LE -> TE).
    k = 0
    fmt = None
    while k < len(vals) and abs(vals[k]) > 1.5:
        if k + 1 < len(vals):
            a, b = vals[k], vals[k + 1]
            if b > 1.5 and a == int(a) and b == int(b) and len(vals) - k - 2 == 2 * (a + b):
                fmt = "lednicer"
                break
        k += 1
    if fmt == "lednicer":
        nu = int(vals[k])
        name = text[:toks[k][0]]
        pts = np.array(vals[k + 2:]).reshape(-1, 2)
        up, lo = pts[:nu], pts[nu:]
    else:
        rest = vals[k:]
        if len(rest) % 2:
            rest = rest[1:]
            k += 1
        if len(rest) < 12:
            raise ValueError("need at least 6 points")
        fmt = "selig"
        name = text[:toks[k][0]]
        pts = np.array(rest).reshape(-1, 2)
        i = int(np.argmin(pts[:, 0]))
        up, lo = pts[:i + 1][::-1], pts[i:]
    # Normalise: leading edge (smallest x) at 0, chord 1, LE on the chord line.
    allp = np.vstack([up, lo])
    le = allp[np.argmin(allp[:, 0])]
    c = allp[:, 0].max() - le[0]
    up = (up - le) / c
    lo = (lo - le) / c
    if np.mean(up[:, 1]) < np.mean(lo[:, 1]):
        up, lo = lo, up
    return name.strip(), fmt, increasing(up), increasing(lo)


def needs_ramp(up, lo):
    """True when closing the trailing edge with a linear pinch (gap * x) would make the section cross itself, checked
    on a fine grid: a blunt edge whose surfaces spread apart towards the end (sc2110)."""
    xs = 0.5 * (1 - np.cos(np.linspace(0, np.pi, CHECK_POINTS)))
    t = resample(up, xs) - resample(lo, xs)
    return bool(np.any(t[1:-1] - t[-1] * xs[1:-1] <= 0))


def increasing(side):
    """Drops points whose x does not increase (duplicates, small kinks), so y(x) is a function."""
    keep = [side[0]]
    for p in side[1:]:
        if p[0] > keep[-1][0] + 1e-9:
            keep.append(p)
    return np.array(keep)


def pchip_slopes(x, y):
    h = np.diff(x); d = np.diff(y) / h; n = len(x)
    if n == 2:
        return np.array([d[0], d[0]])
    s = np.zeros(n)
    for k in range(1, n - 1):
        if d[k - 1] * d[k] > 0:
            w1, w2 = 2 * h[k] + h[k - 1], h[k] + 2 * h[k - 1]
            s[k] = (w1 + w2) / (w1 / d[k - 1] + w2 / d[k])

    def end(h0, h1, d0, d1):
        v = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1)
        if v * d0 <= 0:
            return 0.0
        if d0 * d1 <= 0 and abs(v) > 3 * abs(d0):
            return 3 * d0
        return v
    s[0] = end(h[0], h[1], d[0], d[1])
    s[-1] = end(h[-1], h[-2], d[-1], d[-2])
    return s


def resample(side, xs):
    x, y = side[:, 0], side[:, 1]
    s = pchip_slopes(x, y)
    out = []
    for xi in xs:
        k = min(max(np.searchsorted(x, xi) - 1, 0), len(x) - 2)
        h = x[k + 1] - x[k]; t = (xi - x[k]) / h
        t2, t3 = t * t, t * t * t
        out.append((2*t3 - 3*t2 + 1)*y[k] + (t3 - 2*t2 + t)*h*s[k] + (-2*t3 + 3*t2)*y[k+1] + (t3 - t2)*h*s[k+1])
    return np.array(out)


def surfaces(up, lo, n, closed):
    """Resampled section; closed=None leaves the trailing edge as in the data."""
    xs = 0.5 * (1 - np.cos(np.linspace(0, np.pi, n)))
    yu, yl = resample(up, xs), resample(lo, xs)
    gap = yu[-1] - yl[-1]
    if closed is None:
        return xs, yu, yl
    if closed and needs_ramp(up, lo):
        mid, th = (yu + yl) / 2, (yu - yl) * (1 - np.where(xs > RAMP_START, ((xs - RAMP_START) / (1 - RAMP_START)) ** 2, 0))
        yu, yl = mid + th / 2, mid - th / 2
    elif closed:
        yu, yl = yu - xs * gap / 2, yl + xs * gap / 2
    elif gap < MIN_OPEN_GAP:
        yu, yl = yu + xs * (MIN_OPEN_GAP - gap) / 2, yl - xs * (MIN_OPEN_GAP - gap) / 2
    return xs, yu, yl


def dist(P, q):
    a, b = P[:-1], P[1:]; ab = b - a
    tt = np.clip(np.sum((q - a) * ab, 1) / np.maximum(np.sum(ab * ab, 1), 1e-18), 0, 1)
    return np.min(np.linalg.norm(a + tt[:, None] * ab - q, axis=1))


if __name__ == "__main__":
    for f in sorted(glob.glob(os.path.join(sys.argv[1], "*.dat"))):
        text = open(f).read().replace("\n", " ")       # as pasted into a one-line text field
        name, fmt, up, lo = parse(text)
        worst = {}
        for n in (2000, 40):
            xs, yu, yl = surfaces(up, lo, n, closed=None)
            P = np.concatenate([np.column_stack([xs, yu])[::-1], np.column_stack([xs, yl])[1:]])
            # Only the shape: no trailing-edge adjustment (closed=None).
            worst[n] = max(dist(P, q) for q in np.vstack([up, lo]))
        xs, yu, yl = surfaces(up, lo, 200, closed=True)
        print(f"{os.path.basename(f):14s} {fmt:8s} '{name[:22]}' up {len(up):3d} lo {len(lo):3d}  "
              f"max dist/c fine {worst[2000]:.5f}  40 pts (chords) {worst[40]:.5f}  "
              f"{'ramp' if needs_ramp(up, lo) else 'pinch'}  min closed thickness {np.min((yu - yl)[1:-1]):+.5f}")
