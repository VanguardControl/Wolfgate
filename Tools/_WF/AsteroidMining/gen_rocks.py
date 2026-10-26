"""Procedural asteroid rock RSIs: rockN / veinsN / heatN per size, variants 0-2 round and 3-5 irregular, plus a
metallic copy of each (blue steel with bare plates). Also writes shapes.json with each irregular variant's outline and
convex hull in metres, which go into the rocks' `shapes` in Resources/Prototypes/_WF/AsteroidMining/asteroids.yml.
Needs numpy, scipy and Pillow. python gen_rocks.py <outroot>"""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
SIZES = {"small": 2, "medium": 3, "large": 5, "huge": 7}
TILE = 32

OUTLINE = (34, 29, 28)
# warm regolith (desaturated asteroid-tile browns) and cool bare rock (rock_asteroid greys)
RAMP_A = [(46, 39, 35), (56, 47, 41), (67, 57, 49), (79, 67, 57), (91, 78, 66), (104, 90, 76), (118, 103, 88), (134, 118, 101)]
RAMP_B = [(44, 41, 40), (54, 51, 49), (65, 61, 58), (77, 72, 68), (89, 84, 79), (102, 96, 90), (116, 110, 103)]
VEIN = {"edge": (112, 112, 112), "dark": (160, 160, 160), "mid": (200, 200, 200), "light": (230, 230, 230), "core": (255, 255, 255)}
LIGHT = np.array([-0.62, -0.62, 0.48]); LIGHT /= np.linalg.norm(LIGHT)


def smooth_noise(rng, h, w, scale, aniso=None):
    gh, gw = max(2, int(h / scale) + 3), max(2, int(w / scale) + 3)
    g = rng.standard_normal((gh, gw)).astype(np.float32)
    im = Image.fromarray(g, "F").resize((int(gw * scale), int(gh * scale)), Image.BICUBIC)
    a = np.asarray(im)[:h, :w]
    return a / (a.std() + 1e-6)


def fbm(rng, h, w, scale, octaves=3, gain=0.5):
    tot, amp, s = np.zeros((h, w), np.float32), 1.0, scale
    for _ in range(octaves):
        tot += amp * smooth_noise(rng, h, w, s)
        amp *= gain; s = max(1.5, s / 2)
    return tot / (tot.std() + 1e-6)


def radius_fn(rng, R):
    """Polar silhouette r(theta) in px: faceted polygon blended with soft lumps, clamped to the safe band."""
    n = int(rng.integers(9, 13)) + R
    ang = np.sort((np.arange(n) + rng.uniform(-0.3, 0.3, n)) * 2 * np.pi / n + rng.uniform(0, 6.28)) % (2 * np.pi)
    ang = np.sort(ang)
    rad = rng.uniform(0.855, 0.945, n)
    harm = [(k, rng.uniform(0, 2 * np.pi), rng.uniform(0.004, 0.012)) for k in range(5, 12)]

    def r(theta):
        t = np.mod(theta, 2 * np.pi)
        a2 = np.concatenate([ang - 2 * np.pi, ang, ang + 2 * np.pi]); r2 = np.concatenate([rad, rad, rad])
        lin = np.interp(t, a2, r2)
        i = np.searchsorted(a2, t); i = np.clip(i, 1, len(a2) - 1)
        f = (t - a2[i - 1]) / (a2[i] - a2[i - 1])
        cos = r2[i - 1] + (r2[i] - r2[i - 1]) * (1 - np.cos(f * np.pi)) / 2
        v = 0.3 * lin + 0.7 * cos
        for k, ph, am in harm:
            v = v + am * np.sin(k * t + ph)
        return np.clip(v, 0.84, 0.955) * R * TILE
    return r


def radius_irregular(rng, R):
    """Elongated, lobed or bitten silhouette: the faceted edge shaped by low harmonics and a dent, peak at 0.955 R."""
    n = int(rng.integers(8, 12)) + R
    ang = np.sort((np.arange(n) + rng.uniform(-0.3, 0.3, n)) * 2 * np.pi / n + rng.uniform(0, 6.28)) % (2 * np.pi)
    ang = np.sort(ang)
    rad = rng.uniform(0.86, 1.0, n)
    harm = [(k, rng.uniform(0, 2 * np.pi), rng.uniform(0.004, 0.012)) for k in range(5, 12)]
    e, phi = rng.uniform(0.2, 0.34), rng.uniform(0, np.pi)
    l, psi = rng.uniform(0.04, 0.13), rng.uniform(0, 2 * np.pi)
    b, chi = rng.uniform(0.0, 0.08), rng.uniform(0, 2 * np.pi)
    dent, tau, w = rng.uniform(0.0, 0.22), rng.uniform(0, 2 * np.pi), rng.uniform(0.25, 0.5)

    def raw(theta):
        t = np.mod(theta, 2 * np.pi)
        a2 = np.concatenate([ang - 2 * np.pi, ang, ang + 2 * np.pi]); r2 = np.concatenate([rad, rad, rad])
        lin = np.interp(t, a2, r2)
        i = np.searchsorted(a2, t); i = np.clip(i, 1, len(a2) - 1)
        f = (t - a2[i - 1]) / (a2[i] - a2[i - 1])
        cos = r2[i - 1] + (r2[i] - r2[i - 1]) * (1 - np.cos(f * np.pi)) / 2
        v = 0.3 * lin + 0.7 * cos
        for k, ph, am in harm:
            v = v + am * np.sin(k * t + ph)
        v = v * (1 + e * np.cos(2 * (t - phi)) + l * np.cos(3 * (t - psi)) + b * np.cos(t - chi))
        dd = np.angle(np.exp(1j * (t - tau)))
        return v * (1 - dent * np.exp(-(dd / w) ** 2))

    peak = raw(np.linspace(0, 2 * np.pi, 720, endpoint=False)).max()

    def r(theta):
        return np.clip(raw(theta) / peak * 0.955, 0.42, 0.955) * R * TILE
    return r


def clean_mask(m):
    for _ in range(3):
        k4 = ndimage.convolve(m.astype(int), np.array([[0, 1, 0], [1, 0, 1], [0, 1, 0]]), mode="constant")
        m = np.where(m & (k4 <= 1), False, m)
        m = np.where(~m & (k4 >= 3), True, m)
    return m


def make_rock(R, seed, irregular=False):
    rng = np.random.default_rng(seed)
    S = 2 * R * TILE; c = S / 2; Rp = R * TILE
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    dx, dy = xx + 0.5 - c, yy + 0.5 - c
    d = np.hypot(dx, dy); th = np.arctan2(dy, dx)
    rfn = radius_irregular(rng, R) if irregular else radius_fn(rng, R)
    redge = rfn(th)
    mask = clean_mask(d <= redge)

    # height field: dome + lumps + craters + grain
    q = np.clip(d / redge, 0, 1)
    h = np.sqrt(np.clip(1 - q ** 2, 0, 1)) * Rp * 0.42
    h += fbm(rng, S, S, Rp * 0.45, 3) * Rp * 0.035
    h += fbm(rng, S, S, max(3.0, Rp * 0.06), 2) * Rp * 0.006
    h += smooth_noise(rng, S, S, 2.0) * 0.30

    craters = []
    def place(n, lo, hi, depth_k):
        tries = 0
        while n > 0 and tries < 4000:
            tries += 1
            rc = rng.uniform(lo, hi)
            a = rng.uniform(0, 2 * np.pi)
            rr = rng.uniform(0, 1) ** 0.6 * (rfn(np.array([a]))[0] - rc * 1.25 - 3)
            if rr < 0: continue
            cx, cy = c + rr * math.cos(a), c + rr * math.sin(a)
            if any(math.hypot(cx - x, cy - y) < (rc + r0) * 0.7 for x, y, r0 in craters): continue
            craters.append((cx, cy, rc)); n -= 1
            t = np.hypot(xx + 0.5 - cx, yy + 0.5 - cy) / rc
            depth, rim = rc * depth_k, rc * 0.16
            inside = -depth + (depth + rim) * np.clip(t, 0, 1) ** 3.2
            outside = rim * np.exp(-((t - 1) / 0.32) ** 2)
            prof = np.where(t < 1, inside, outside)
            nonlocal_h[0] = nonlocal_h[0] + prof
    nonlocal_h = [h]
    place(int(rng.integers(1, 3)) + (1 if R >= 5 else 0), Rp * 0.17, Rp * 0.28, 0.32)
    place(int(3 + R * 1.3), Rp * 0.07, Rp * 0.13, 0.40)
    place(int(6 + R * R * 0.8), 2.5, max(4.0, Rp * 0.045), 0.5)
    h = nonlocal_h[0]

    gy, gx = np.gradient(h)
    n = np.stack([-gx, -gy, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    lam = n @ LIGHT
    cav = h - ndimage.gaussian_filter(h, max(2, Rp * 0.05))
    shade = lam + 0.025 * np.clip(cav, -6, 6) - 0.10 * q ** 6

    # material bands along a diagonal, like the asteroid tile streaks
    u = (dx + dy) / math.sqrt(2); v = (dx - dy) / math.sqrt(2)
    band = 0.6 * fbm(rng, S, S, Rp * 0.5, 2) + np.sin(v / max(5.0, Rp * 0.07) + 1.2 * fbm(rng, S, S, Rp * 0.35, 2))
    matB = (band + 0.22 * smooth_noise(rng, S, S, 2.5)) > 0.85

    # quantize
    lo, hi = np.percentile(shade[mask], 3), np.percentile(shade[mask], 99.3)
    s01 = np.clip((shade - lo) / (hi - lo), 0, 0.9999)
    idxA = (s01 ** 0.95 * len(RAMP_A)).astype(int)
    idxB = np.clip((s01 * len(RAMP_B)).astype(int) - 0, 0, len(RAMP_B) - 1)
    idx = np.where(matB, idxB + 100, idxA)
    idx = majority(idx, mask)

    edge = mask & ~ndimage.binary_erosion(mask, structure=np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]]))
    return dict(S=S, c=c, Rp=Rp, mask=mask, edge=edge, idx=idx, lam=lam, rfn=rfn, d=d, th=th,
                craters=craters, rng=rng, xx=xx, yy=yy, h=h, irregular=irregular)


def majority(idx, mask):
    out = idx.copy()
    H, W = idx.shape
    pad = np.pad(idx, 1, mode="edge")
    neigh = np.stack([pad[1 + oy:H + 1 + oy, 1 + ox:W + 1 + ox] for oy in (-1, 0, 1) for ox in (-1, 0, 1) if oy or ox], 0)
    same = (neigh == idx[None]).sum(0)
    lonely = mask & (same <= 1)
    ys, xs = np.nonzero(lonely)
    for y, x in zip(ys, xs):
        vals, cnt = np.unique(neigh[:, y, x], return_counts=True)
        out[y, x] = vals[np.argmax(cnt)]
    return out


def render_rock(rk, fissure):
    S = rk["S"]; img = np.zeros((S, S, 4), np.uint8)
    idx = rk["idx"].copy()
    # darken along major fissures
    f = fissure & rk["mask"] & ~rk["edge"]
    idx = np.where(f & (idx < 100), np.maximum(idx - 2, 0), idx)
    idx = np.where(f & (idx >= 100), np.maximum(idx - 2, 100), idx)
    for i, col in enumerate(RAMP_A):
        img[(idx == i) & rk["mask"]] = (*col, 255)
    for i, col in enumerate(RAMP_B):
        img[(idx == 100 + i) & rk["mask"]] = (*col, 255)
    img[rk["edge"]] = (*OUTLINE, 255)
    return img


def jagged(rng, p0, p1, rough, minseg=3.0):
    """Midpoint-displaced polyline from p0 to p1."""
    pts = [p0, p1]
    while True:
        out = [pts[0]]; split = False
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            L = math.hypot(x1 - x0, y1 - y0)
            if L > minseg:
                nx, ny = -(y1 - y0) / L, (x1 - x0) / L
                o = rng.normal(0, rough * L)
                out.append(((x0 + x1) / 2 + nx * o, (y0 + y1) / 2 + ny * o)); split = True
            out.append((x1, y1))
        pts = out
        if not split: return pts


def inside_pt(rk, x, y, margin):
    c = rk["c"]
    return math.hypot(x - c, y - c) <= rk["rfn"](np.array([math.atan2(y - c, x - c)]))[0] - margin


def clip_path(rk, pts, margin):
    out = []
    for p in pts:
        if not inside_pt(rk, p[0], p[1], margin): break
        out.append(p)
    return out


def render_veins(rk):
    rng = rk["rng"]; S = rk["S"]; Rp = rk["Rp"]; c = rk["c"]
    inner = ndimage.binary_erosion(rk["mask"], iterations=2)
    area = rk["mask"].sum()
    canvas = Image.new("L", (S, S), 0); dr = ImageDraw.Draw(canvas)
    v = np.zeros((S, S), bool); frac = 0
    nclusters = 0
    centres = []
    while frac < 0.125 and nclusters < 200:
        nclusters += 1
        for _ in range(30):
            a = rng.uniform(0, 2 * np.pi)
            reach = rk["rfn"](np.array([a]))[0] / 0.9 if rk["irregular"] else Rp
            rr = rng.uniform(0, 1) ** 0.7 * 0.72 * reach
            cx, cy = c + rr * math.cos(a), c + rr * math.sin(a)
            if all(math.hypot(cx - x, cy - y) > Rp * 0.22 for x, y in centres): break
        centres.append((cx, cy))
        rad = Rp * rng.uniform(0.10, 0.20)
        ang = rng.uniform(0, np.pi)
        # a few roughly parallel streaks
        for _ in range(int(rng.integers(1, 4))):
            off = rng.normal(0, rad * 0.45); ln = rad * rng.uniform(0.7, 1.3)
            aa = ang + rng.normal(0, 0.18)
            ox, oy = cx - math.sin(aa) * off, cy + math.cos(aa) * off
            p0 = (ox - math.cos(aa) * ln, oy - math.sin(aa) * ln); p1 = (ox + math.cos(aa) * ln, oy + math.sin(aa) * ln)
            pts = jagged(rng, p0, p1, 0.18, 2.5)
            w = int(rng.integers(1, 2 + Rp // 90))
            # taper: thick middle, thin ends
            n = len(pts)
            dr.line(pts[n // 5: n - n // 5], fill=255, width=w + 1)
            dr.line(pts, fill=255, width=max(1, w - 1))
        # crystal flecks: small angular chunks around the cluster
        for _ in range(int(rng.integers(2, 4 + Rp // 40))):
            fr = abs(rng.normal(0, rad * 0.6)); fa = rng.uniform(0, 2 * np.pi)
            fx, fy = cx + fr * math.cos(fa), cy + fr * math.sin(fa)
            sz = rng.uniform(1.0, 1.8 + Rp / 110)
            k = int(rng.integers(4, 6)); rot = rng.uniform(0, 2 * np.pi)
            poly = [(fx + sz * rng.uniform(0.6, 1.3) * math.cos(rot + i * 2 * np.pi / k), fy + sz * rng.uniform(0.6, 1.3) * math.sin(rot + i * 2 * np.pi / k)) for i in range(k)]
            dr.polygon(poly, fill=255)
        v = (np.asarray(canvas) > 0) & inner
        frac = v.sum() / area
    v = clean_mask(v) & inner
    frac = v.sum() / area
    core = ndimage.binary_erosion(v, structure=np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]]))
    border = v & ~core
    lam = rk["lam"]
    shadow = lam < np.percentile(lam[rk["mask"]], 35)
    img = np.zeros((S, S, 4), np.uint8)
    ul = v & ~np.roll(v, 1, 0) | v & ~np.roll(v, 1, 1)      # top or left edge: catches light
    lr = v & ~np.roll(v, -1, 0) | v & ~np.roll(v, -1, 1)    # bottom or right edge: shadowed
    img[v] = (*VEIN["mid"], 255)
    img[core] = (*VEIN["light"], 255)
    img[core & shadow] = (*VEIN["mid"], 255)
    img[v & ~core & shadow] = (*VEIN["dark"], 255)
    img[ul & ~lr] = (*VEIN["core"], 255)
    img[ul & ~lr & shadow] = (*VEIN["light"], 255)
    img[lr & core] = (*VEIN["dark"], 255)
    img[lr & ~ul] = (*VEIN["edge"], 255)
    return img, frac


def crack_paths(rk):
    rng = rk["rng"]; c = rk["c"]; Rp = rk["Rp"]; rfn = rk["rfn"]
    paths = []
    cx, cy = c + rng.uniform(-0.12, 0.12) * Rp, c + rng.uniform(-0.12, 0.12) * Rp
    k = int(rng.integers(4, 6)) + (1 if Rp >= 160 else 0)
    base = rng.uniform(0, 2 * np.pi)
    for i in range(k):
        a = base + i * 2 * np.pi / k + rng.normal(0, 0.3)
        end_r = rfn(np.array([a]))[0] * rng.uniform(0.72, 0.9)
        p1 = (c + end_r * math.cos(a), c + end_r * math.sin(a))
        pts = clip_path(rk, jagged(rng, (cx, cy), p1, 0.12, 4.0), 4)
        if len(pts) < 3: continue
        paths.append((pts, 0))
        for _ in range(int(1 + Rp / 70)):
            j = int(rng.integers(len(pts) // 5, len(pts) - 1))
            px, py = pts[j]
            dirn = math.atan2(pts[-1][1] - cy, pts[-1][0] - cx)
            a0 = dirn + rng.choice([-1, 1]) * rng.uniform(0.5, 1.1)
            ln = Rp * rng.uniform(0.15, 0.35)
            sub = clip_path(rk, jagged(rng, (px, py), (px + ln * math.cos(a0), py + ln * math.sin(a0)), 0.15, 3.5), 4)
            if len(sub) < 3: continue
            paths.append((sub, 1))
            for _ in range(int(Rp / 110)):
                j2 = int(rng.integers(1, len(sub) - 1)); qx, qy = sub[j2]
                a1 = a0 + rng.choice([-1, 1]) * rng.uniform(0.5, 1.2); l2 = Rp * rng.uniform(0.06, 0.14)
                t = clip_path(rk, jagged(rng, (qx, qy), (qx + l2 * math.cos(a1), qy + l2 * math.sin(a1)), 0.18, 3.0), 4)
                if len(t) >= 2: paths.append((t, 2))
    for _ in range(int(Rp / 40)):
        a = rng.uniform(0, 2 * np.pi); rr = rng.uniform(0.3, 0.65) * Rp
        x0, y0 = c + rr * math.cos(a), c + rr * math.sin(a); a1 = rng.uniform(0, 2 * np.pi); l2 = Rp * rng.uniform(0.07, 0.15)
        t = clip_path(rk, jagged(rng, (x0, y0), (x0 + l2 * math.cos(a1), y0 + l2 * math.sin(a1)), 0.18, 3.0), 4)
        if len(t) >= 2: paths.append((t, 2))
    return paths, (cx, cy)


def render_heat(rk):
    S = rk["S"]; Rp = rk["Rp"]
    paths, (cx, cy) = crack_paths(rk)
    layers = {t: Image.new("L", (S, S), 0) for t in (0, 1, 2)}
    for pts, tier in paths:
        dr = ImageDraw.Draw(layers[tier])
        if tier == 0 and Rp >= 90:
            n = len(pts); cut = max(2, n // 2)
            dr.line(pts[:cut], fill=255, width=2, joint="curve")
            dr.line(pts[cut - 1:], fill=255, width=1)
        else:
            dr.line(pts, fill=255, width=1)
    t0, t1, t2 = (np.asarray(layers[t]) > 0 for t in (0, 1, 2))
    hot = ndimage.binary_dilation(np.hypot(rk["xx"] + 0.5 - cx, rk["yy"] + 0.5 - cy) <= max(1.5, Rp / 40), iterations=0) if False else (np.hypot(rk["xx"] + 0.5 - cx, rk["yy"] + 0.5 - cy) <= max(1.5, Rp / 40))
    inner = ndimage.binary_erosion(rk["mask"], iterations=3)
    core = (t0 | t1 | t2 | hot) & inner
    halo = ndimage.binary_dilation(core, structure=np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]])) & ~core & rk["mask"] & ~rk["edge"]
    img = np.zeros((S, S, 4), np.uint8)
    img[halo] = (205, 205, 205, 110)
    img[t2 & inner] = (212, 212, 212, 255)
    img[t1 & inner] = (236, 236, 236, 255)
    img[(t0 | hot) & inner] = (255, 255, 255, 255)
    # fissure mask for the rock layer: 1px skeleton of major cracks
    fis = Image.new("L", (S, S), 0); dr = ImageDraw.Draw(fis)
    for pts, tier in paths:
        if tier == 0: dr.line(pts, fill=255, width=1)
    return img, np.asarray(fis) > 0


def check_bounds(rk, R):
    m = rk["mask"]; d = rk["d"]; Rp = R * TILE
    # outermost pixel corner distance
    ys, xs = np.nonzero(m)
    c = rk["c"]
    far = np.max(np.hypot(np.maximum(np.abs(xs - c), np.abs(xs + 1 - c)), np.maximum(np.abs(ys - c), np.abs(ys + 1 - c))))
    return far / Rp, np.min(d[rk["edge"]]) / Rp


METAL_A = [(36, 40, 48), (46, 52, 62), (58, 65, 77), (71, 79, 93), (85, 94, 110), (100, 110, 128), (117, 128, 147), (137, 149, 169)]
METAL_B = [(50, 53, 58), (64, 68, 74), (80, 85, 92), (98, 104, 112), (118, 125, 134), (141, 149, 158), (168, 176, 186)]
METAL_OUTLINE = (26, 29, 35)
GLINT = (214, 224, 238)


FACET = [(70, 78, 92), (96, 106, 122), (126, 137, 155), (158, 170, 188), (192, 203, 218), (226, 233, 242)]
FACET_EDGE = (40, 45, 54)


def facets(out, rk, seed):
    """Raised plates of bare metal: flat-shaded convex chunks, lit by their own tilt, with a dark rim."""
    rng = np.random.default_rng(seed)
    S, c, rfn = rk["S"], rk["c"], rk["rfn"]
    inner = ndimage.binary_erosion(rk["mask"], iterations=3)
    area, covered = inner.sum(), np.zeros(inner.shape, bool)
    for _ in range(400):
        if covered.sum() > area * 0.13:
            break
        a = rng.uniform(0, 2 * np.pi)
        rr = rng.uniform(0, 1) ** 0.6 * rfn(np.array([a]))[0] * 0.8
        cx, cy = c + rr * math.cos(a), c + rr * math.sin(a)
        size = rk["Rp"] * rng.uniform(0.06, 0.13) + 2
        k = int(rng.integers(4, 7)); rot = rng.uniform(0, 2 * np.pi)
        poly = [(cx + size * rng.uniform(0.6, 1.2) * math.cos(rot + i * 2 * np.pi / k),
                 cy + size * rng.uniform(0.6, 1.2) * math.sin(rot + i * 2 * np.pi / k)) for i in range(k)]
        im = Image.new("L", (S, S), 0); ImageDraw.Draw(im).polygon(poly, fill=255)
        m = (np.asarray(im) > 0) & inner & ~covered
        if m.sum() < 6:
            continue
        covered |= m
        # each plate tilts its own way; a split face catches the light on one side
        n = np.array([rng.normal(0, 0.6), rng.normal(0, 0.6), 1.0]); n /= np.linalg.norm(n)
        base = float(n @ LIGHT)
        side = ((rk["xx"] - cx) * math.cos(rot) + (rk["yy"] - cy) * math.sin(rot)) > 0
        level = np.clip((base + 0.15 * np.where(side, 1, -1) + 0.2) / 1.2, 0, 0.999)
        idx = (level * len(FACET)).astype(int)
        for i, col in enumerate(FACET):
            out[m & (idx == i)] = (*col, 255)
        rim = m & ~ndimage.binary_erosion(m, structure=np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]]))
        lower = rim & (np.roll(~m, -1, 0) | np.roll(~m, -1, 1))
        out[lower] = (*FACET_EDGE, 255)
        top = rim & (np.roll(~m, 1, 0) | np.roll(~m, 1, 1)) & ~lower
        out[top] = (*FACET[-1], 255)


def metallic(rock, seed, rk=None):
    """Maps the regolith palette onto blue steel and bright polished bands, with sparse glints on lit faces."""
    out = rock.copy()
    rgb = rock[..., :3]
    opaque = rock[..., 3] > 0
    lut = {OUTLINE: METAL_OUTLINE}
    lut.update({a: b for a, b in zip(RAMP_A, METAL_A)})
    lut.update({a: b for a, b in zip(RAMP_B, METAL_B)})
    for src, dst in lut.items():
        out[(rgb == src).all(-1) & opaque] = (*dst, 255)
    rng = np.random.default_rng(seed)
    lit = ((rgb == RAMP_B[-1]).all(-1) | (rgb == RAMP_A[-1]).all(-1) | (rgb == RAMP_B[-2]).all(-1)) & opaque
    out[lit & (rng.random(lit.shape) < 0.05)] = (*GLINT, 255)
    if rk is not None:
        facets(out, rk, seed + 101)
    return out


def outline(rfn, scale=1.0, n=32):
    """The silhouette in metres around the rock's centre, world axes (y up)."""
    t = np.arange(n) * 2 * np.pi / n
    r = rfn(t) / TILE * scale
    return [(round(float(x), 3), round(float(y), 3)) for x, y in zip(r * np.cos(t), -r * np.sin(t))]


def hull8(points):
    pts = sorted(set(points))

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0: lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0: upper.pop()
        upper.append(p)
    h = lower[:-1] + upper[:-1]
    while len(h) > 8:
        areas = [abs(cross(h[i - 1], h[i], h[(i + 1) % len(h)])) for i in range(len(h))]
        h.pop(int(np.argmin(areas)))
    return h


def write_meta(path, R, states, extra):
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": "Created for Wolfgate (AsteroidMining)" + extra,
            "size": {"x": 2 * R * TILE, "y": 2 * R * TILE},
            "states": sorted(states, key=lambda s: (s["name"][:-1] != "rock", s["name"][:-1] != "veins", s["name"]))}
    with open(os.path.join(path, "meta.json"), "w", newline="\n") as f:
        json.dump(meta, f, indent=2); f.write("\n")


def main():
    report = {}
    shapes = {}
    for name, R in SIZES.items():
        rsi = os.path.join(OUT, f"asteroid_{name}.rsi"); os.makedirs(rsi, exist_ok=True)
        mrsi = os.path.join(OUT, f"asteroid_{name}_metallic.rsi"); os.makedirs(mrsi, exist_ok=True)
        states = []
        shapes[name] = {}
        for v in range(6):
            irregular = v >= 3
            rk = make_rock(R, 1000 * R + 17 * v + 3, irregular)
            heat, fis = render_heat(rk)
            rock = render_rock(rk, fis)
            veins, frac = render_veins(rk)
            veins[~rk["mask"]] = 0; heat[~rk["mask"]] = 0
            for st, arr in ((f"rock{v}", rock), (f"veins{v}", veins), (f"heat{v}", heat)):
                Image.fromarray(arr, "RGBA").save(os.path.join(rsi, st + ".png"))
            for st, arr in ((f"rock{v}", metallic(rock, 7 * R + v, rk)),(f"veins{v}", veins), (f"heat{v}", heat)):
                Image.fromarray(arr, "RGBA").save(os.path.join(mrsi, st + ".png"))
            if irregular:
                shapes[name][v] = dict(outline=outline(rk["rfn"], 1.05), hull=hull8(outline(rk["rfn"], 0.92, 64)),
                                       area=round(float(rk["mask"].sum()) / (np.pi * (R * TILE) ** 2), 3))
            far, nearedge = check_bounds(rk, R)
            report[f"{name}{v}"] = dict(maxR=round(far, 3), minEdgeR=round(nearedge, 3), vein_frac=round(float(frac), 3))
            states += [{"name": f"rock{v}"}, {"name": f"veins{v}"}, {"name": f"heat{v}"}]
        write_meta(rsi, R, states, "")
        write_meta(mrsi, R, states, ", metallic")
    with open(os.path.join(OUT, "shapes.json"), "w") as f:
        json.dump(shapes, f, indent=1)
    for k, v in report.items(): print(k, v)


if __name__ == "__main__":
    main()
