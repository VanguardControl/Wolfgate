"""Builds the cavern mouth pit RSIs: one per world, drawn over the world's landing floor.

A pit is drawn on the dual grid. Each piece is centred on a corner of the hole's tiles and covers a quarter of each of
the four tiles around it, so it can cut into the lip tiles as well as fill the hole: the rim wanders off the tile edges,
a corner of the hole is rounded off and a corner of ground poking into it is cut back. The hole always covers its own
tiles completely; on the lip only the pit is drawn, and the ground under it shows through a darkened edge, so any
ground tile works.

States (WFCavernShadeVisualsSystem picks them; the names must match its own):
- `v<mask>_<labels>_<variant>`: `mask` says which of the four tiles are pit (1 NW, 2 NE, 4 SW, 8 SE). `labels` says,
  for each piece border the rim crosses (1 top, 2 right, 4 bottom, 8 left), whether it crosses deep into the ground
  (set) or shallow. Both pieces on a border see the same tile edge, whose label is a stable hash of it, so the rim
  meets itself there; between borders it wanders freely. `variant` is one of `variants(mask)` drawings.
- `deep<nw><ne><sw><se>`: a shadow over a piece with pit all round, blended between the depths of its four tiles (0 at
  the rim, then 1, then 2 and deeper), so a big hole darkens towards its middle.
- `pit`: a full tile of pit, the placement icon.

Nothing a piece draws near its borders depends on the tiles beyond its own four, so pieces join seamlessly: the rim
is pinned to its label there, and every shadow, wall and detail stays short of the next piece's tiles.
Run from the repo root: python Tools/_WF/Caverns/gen_pits.py
"""
import json
import math
import os
import random

from PIL import Image

OUT = "Resources/Textures/_WF/Caverns/Mouths"
TILE = 32
HALF = TILE // 2

LABEL_BITES = (3.5, 9.0)  # how far past the tile edge the rim crosses a piece border, shallow or deep
BITE_MIN, BITE_MAX = 3.5, 11.0  # the rim never comes closer to its tiles or farther from them than this
WANDER = 3.5  # how far the rim wanders in and out between borders
WANDER_CELL = 8.0  # the wander's lattice spacing, so a bulge or bite is about this wide
PIN = 4.0  # within this of a border the rim sits exactly on its label
FADE = 5.0  # over this much farther the wander fades in
ROUND_PIT = 14.0  # a hole corner is rounded to this plus the bite, leaving its edges this far from the corner
CORNER_BITE = 6.2  # the bite at a hole corner, deep enough for the rounding to cover the corner of its tile
FILLET = 10.0  # the radius a ground corner poking into the hole is rounded to
FILLET_BITE = 4.5  # the bite at such a corner, shallow so the fillet ends inside the piece
CORNER_PULL = (4.0, 12.0)  # within the first distance of a corner its own bite holds; past the second, the edges'
CRUMBLE = 0.7  # pixel jitter on the rim line
SIDE = 6.5  # inner shadow width on the sides without a wall
LEDGE = 2.2  # a sliver of side wall inside the east and west rims
SIDE_DARK = 0.4
CONTACT = 3  # shadow where the wall meets the floor
LIP_SHADE = 4.5  # the ground darkens this far back from the rim
MARGIN = 2  # details keep this far from a piece's borders
DEPTH_ALPHA = (0.0, 0.3, 0.5)  # the deep overlay's darkness at each tile depth

TG = ("CC-BY-SA-3.0", "tgstation")
MOJAVE = ("CC-BY-NC-SA-3.0", "Mojave-Sun")

# Landing floor and its licence, then the look: floor brightness and tint, the shaft wall's top and foot colours,
# its height, rubble, the rim's lit edge (colour and strength) and the world's own touch.
WORLDS = {
    "asclepiu": {
        "source": "Resources/Textures/_Mono/Tiles/planet/water.png",
        "license": ("CC-BY-SA-3.0", "Monolith (Grid Networks, #4690)"),
        "floor": 0.45, "tint": (0.85, 0.95, 1.0), "wall": (112, 84, 54), "deep": (40, 34, 28), "face": 11,
        "rubble": (98, 88, 72), "edge": ((255, 255, 230), 0.12), "touch": "roots",
    },
    "fervidus": {
        "source": "Resources/Textures/Tiles/cavedrought.png",
        "license": MOJAVE,
        "floor": 0.32, "tint": (1.05, 0.9, 0.85), "wall": (104, 80, 68), "deep": (40, 22, 18), "face": 10,
        "rubble": (70, 60, 58), "edge": ((255, 200, 160), 0.14), "touch": "embers",
    },
    "merak": {
        "source": "Resources/Textures/Tiles/Asteroid/asteroid.png",
        "license": TG,
        "floor": 0.42, "tint": (1.0, 0.92, 0.85), "wall": (182, 132, 82), "deep": (70, 46, 30), "face": 10,
        "rubble": (140, 104, 70), "edge": ((255, 240, 200), 0.18), "touch": "sand",
    },
    "aerumna": {
        "source": "Resources/Textures/Tiles/chromite.png",
        "license": ("CC-BY-NC-SA-3.0", "Mojave-Sun, recoloured by TheShuEd"),
        "floor": 0.28, "tint": (0.9, 0.85, 1.1), "wall": (112, 98, 156), "deep": (16, 12, 26), "face": 10,
        "rubble": (74, 66, 100), "edge": ((214, 190, 255), 0.42), "touch": "glints",
    },
    "thrascias": {
        "source": "Resources/Textures/Tiles/Planet/Snow/snow.png",
        "license": TG,
        "floor": 0.4, "tint": (0.72, 0.86, 1.05), "wall": (184, 214, 236), "deep": (40, 62, 96), "face": 10,
        "rubble": (150, 176, 200), "edge": ((255, 255, 255), 0.0), "touch": "ice",
    },
    "carcinoma": {
        "source": "Resources/Textures/Tiles/Asteroid/ironsand.png",
        "license": TG,
        "floor": 0.3, "tint": (1.05, 0.85, 0.85), "wall": (156, 48, 64), "deep": (42, 8, 16), "face": 10,
        "rubble": (110, 40, 52), "edge": ((255, 170, 180), 0.16), "touch": "sinew",
    },
}

# The four tiles around a piece's corner: which way each lies (x, y up) and its mask bit.
QUADRANTS = {"nw": (-1, 1, 1), "ne": (1, 1, 2), "sw": (-1, -1, 4), "se": (1, -1, 8)}

# A piece's borders: the two tiles either side of it, its label bit, and the distance of a point to it.
BORDERS = {
    "top": (("nw", "ne"), 1, lambda px, py: HALF - py),
    "right": (("ne", "se"), 2, lambda px, py: HALF - px),
    "bottom": (("sw", "se"), 4, lambda px, py: HALF + py),
    "left": (("nw", "sw"), 8, lambda px, py: HALF + px),
}


def variants(mask):
    """How many drawings a mask has: the full pit three, a diagonal pair one, the rest two."""
    if mask == 15:
        return 3
    if mask in (6, 9):
        return 1
    return 2


def pit(mask, quadrant):
    return bool(mask & QUADRANTS[quadrant][2])


def crossings(mask):
    """The borders the rim crosses: those whose two tiles differ."""
    return [name for name, ((a, b), _, _) in BORDERS.items() if pit(mask, a) != pit(mask, b)]


def lerp(a, b, t):
    return a + (b - a) * max(0.0, min(1.0, t))


def mix(c1, c2, t):
    return tuple(lerp(a, b, t) for a, b in zip(c1, c2))


def scale(colour, factor):
    return tuple(max(0, min(255, round(c * factor))) for c in colour[:3])


def tint(colour, factors):
    return tuple(max(0, min(255, round(c * f))) for c, f in zip(colour, factors))


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def local(x, y):
    """A pixel centre relative to the piece's corner, y up."""
    return x + 0.5 - HALF, HALF - (y + 0.5)


def rounded_quadrant(px, py, sx, sy, cx, cy, radius):
    """Signed distance to the quadrant sx*x >= cx, sy*y >= cy, its corner rounded to the radius."""
    qx = cx + radius - sx * px
    qy = cy + radius - sy * py
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - radius


class Piece:
    """One drawing: a mask, the labels of the borders the rim crosses and a variant, with its rim field.

    Each rim edge runs from the piece's corner to a border and has its own bite along it: its label near the border,
    a wander in between, and near the corner the corner's own bite, so a hole corner rounds widely and a ground corner
    takes a fillet that ends inside the piece.
    """

    def __init__(self, world, mask, labels, variant):
        self.mask = mask
        self.rng = random.Random(f"{world}:{mask}:{labels}:{variant}")
        self.labels = {name: LABEL_BITES[1 if labels & BORDERS[name][1] else 0] for name in crossings(mask)}
        self.pits = [q for q in QUADRANTS if pit(mask, q)]
        if len(self.pits) == 1 or mask in (6, 9):
            self.corner = CORNER_BITE
        elif len(self.pits) == 3:
            self.corner = FILLET_BITE
        else:
            self.corner = None
        # Smooth wander along each edge and across the piece: random values on a lattice, blended.
        self.lines = {name: [self.rng.uniform(-1, 1) for _ in range(8)] for name in list(BORDERS) + ["across"]}
        self.lattice = {(i, j): self.rng.uniform(-1, 1) for i in range(8) for j in range(8)}

    def line(self, name, s):
        """The wander along one edge at a signed distance s from the corner."""
        g = (s + HALF) / WANDER_CELL + 1
        i = int(g)
        values = self.lines[name]
        return lerp(values[i], values[i + 1], smoothstep(g - i))

    def wander(self, px, py):
        gx, gy = (px + HALF) / WANDER_CELL + 1, (py + HALF) / WANDER_CELL + 1
        i, j = int(gx), int(gy)
        fx, fy = smoothstep(gx - i), smoothstep(gy - j)
        v = self.lattice
        return lerp(lerp(v[(i, j)], v[(i + 1, j)], fx), lerp(v[(i, j + 1)], v[(i + 1, j + 1)], fx), fy)

    def edge(self, name, t):
        """An edge's bite at a distance t from the corner along it: its label within PIN of the border."""
        label = self.labels[name]
        fade = smoothstep((HALF - PIN - t) / FADE)
        bite = label + WANDER * self.line(name, t) * fade
        if self.corner is not None:
            near, far = CORNER_PULL
            bite = lerp(bite, self.corner, smoothstep((far - t) / (far - near)))
        return max(BITE_MIN, min(BITE_MAX, bite))

    def straight(self, s, low, high):
        """The bite along a straight rim at signed distance s from the corner, between its two borders' labels."""
        fade = smoothstep((HALF - PIN - abs(s)) / FADE)
        bite = lerp(self.labels[low], self.labels[high], smoothstep((s + HALF - PIN) / (2 * (HALF - PIN))))
        return max(BITE_MIN, min(BITE_MAX, bite + WANDER * self.line("across", s) * fade))

    def quadrant(self, q, sign):
        """The rounded field of one quadrant, pit (sign 1) or ground (sign -1), from its two edges' bites."""
        sx, sy, _ = QUADRANTS[q]
        return sx, sy, ("top" if sy > 0 else "bottom"), ("right" if sx > 0 else "left")

    def field(self, px, py):
        """Signed distance from the rim, positive on the ground."""
        n = len(self.pits)
        if n == 4:
            return -99.0
        if n == 1 or self.mask in (6, 9):
            best = 99.0
            for q in self.pits:
                sx, sy, vertical, horizontal = self.quadrant(q, 1)
                bv = self.edge(vertical, max(0.0, sy * py))
                bh = self.edge(horizontal, max(0.0, sx * px))
                best = min(best, rounded_quadrant(px, py, sx, sy, -bv, -bh, ROUND_PIT + (bv + bh) / 2))
            return best
        if n == 3:
            ground = next(q for q in QUADRANTS if q not in self.pits)
            sx, sy, vertical, horizontal = self.quadrant(ground, -1)
            bv = self.edge(vertical, max(0.0, sy * py))
            bh = self.edge(horizontal, max(0.0, sx * px))
            return -rounded_quadrant(px, py, sx, sy, bv, bh, FILLET)
        (ax, ay, _), (bx, by, _) = QUADRANTS[self.pits[0]], QUADRANTS[self.pits[1]]
        if ay == by:
            return -ay * py - self.straight(px, "left", "right")
        return -ax * px - self.straight(py, "bottom", "top")

    def inside_tiles(self, px, py):
        """Whether a point lies on one of the piece's pit tiles, which must always be drawn as pit."""
        return any((px > 0) == (QUADRANTS[q][0] > 0) and (py > 0) == (QUADRANTS[q][1] > 0) for q in self.pits)


def draw_piece(world, look, floor, mask, labels, variant):
    piece = Piece(world, mask, labels, variant)
    rng = piece.rng
    salt = rng.randrange(1 << 20)
    face = look["face"]
    smooth = {}
    field = {}
    for y in range(TILE):
        for x in range(TILE):
            px, py = local(x, y)
            f = piece.field(px, py)
            smooth[(x, y)] = f
            jitter = CRUMBLE * noise(x, y, salt)
            f = f + jitter if abs(f) < 2 else f
            if piece.inside_tiles(px, py):
                f = min(f, -0.01)
            field[(x, y)] = f

    image = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    kinds = {}
    under = {}

    for y in range(TILE):
        for x in range(TILE):
            f = field[(x, y)]
            n = noise(x, y, salt + 1)
            if f > 0:
                colour = lip_colour(look, f)
                kinds[(x, y)] = "rim" if f <= 1.0 else ("lip" if f <= LIP_SHADE else "ground")
                if colour:
                    image.putpixel((x, y), colour)
                continue

            u = wall_depth(smooth, x, y, face + CONTACT)
            under[(x, y)] = u
            inset = -smooth[(x, y)]
            if u is not None and u < face:
                colour = wall_colour(look, x, u, n, face, piece)
                kinds[(x, y)] = "wall"
            elif u is None and inset < LEDGE and facing(smooth, x, y) == "side":
                colour = scale(mix(look["wall"], look["deep"], 0.55), lerp(0.8, 0.5, inset / LEDGE) * (1.0 + 0.1 * n))
                kinds[(x, y)] = "ledge"
            else:
                factor = look["floor"] * (1.0 + 0.06 * n)
                if inset < SIDE:
                    factor *= lerp(SIDE_DARK, 1.0, inset / SIDE)
                if u is not None:
                    factor *= lerp(0.4, 1.0, (u - face + 1) / (CONTACT + 1))
                fx, fy = (x + HALF) % TILE, (y + HALF) % TILE
                colour = tint(scale(floor.getpixel((fx, fy)), factor), look["tint"])
                kinds[(x, y)] = "floor"
            image.putpixel((x, y), colour + (255,))

    details(look, piece, rng, image, kinds, under, smooth, variant, face)
    return image


def noise(x, y, salt):
    """A stable value in [-1, 1] for a pixel and a salt."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFF) / 32767.5 - 1.0


def lip_colour(look, f):
    """The ground side of the rim: a dark broken edge, the lit lip behind it, then a fading shadow; None past it."""
    if f <= 1.0:
        return (0, 0, 0, 190)
    edge, strength = look["edge"]
    if f <= 2.0 and strength > 0:
        return edge + (round(255 * strength),)
    if f <= LIP_SHADE:
        return (0, 0, 0, round(255 * 0.28 * (1 - (f - 1.0) / (LIP_SHADE - 1.0))))
    return None


def facing(smooth, x, y):
    """Which way the rim nearest a pixel faces: "side" for an east or west rim, "north" or "south" otherwise."""
    def at(ix, iy):
        return smooth[(min(TILE - 1, max(0, ix)), min(TILE - 1, max(0, iy)))]
    gx = at(x + 1, y) - at(x - 1, y)
    gy = at(x, y + 1) - at(x, y - 1)
    if abs(gx) > abs(gy):
        return "side"
    return "south" if gy > 0 else "north"


def wall_depth(smooth, x, y, reach):
    """How far below the rim straight above a pit pixel lies, or None when no rim is that close above it."""
    if smooth[(x, y)] > 0:
        return 0
    for k in range(1, reach + 1):
        if y - k < 0:
            return None
        if smooth[(x, y - k)] > 0:
            return k - 1
    return None


def wall_colour(look, x, u, n, face, piece):
    """The shaft wall under a rim: strata that wobble along the tile, lit at the top and dark at the foot."""
    wx = (x + HALF) % TILE
    depth = u / (face - 1)
    wobble = 0.6 * math.sin(2 * math.pi * wx / TILE + 1.3)
    # Away from the borders each drawing bends the strata its own way.
    middle = smoothstep(min(x, TILE - 1 - x) / 8.0)
    wobble += 1.6 * middle * piece.wander(x - HALF, 3.0)
    band = int((u + 0.5 + wobble) // 2.2)
    base = mix(look["wall"], look["deep"], depth * 0.85)
    light = lerp(1.1, 0.4, depth) * (1.0 + (0.14 if band % 2 else -0.1)) * (1.0 + 0.08 * n)
    if u == 0:
        light *= 1.15  # the lit top of the cut
    return scale(base, light)


def put(image, kinds, x, y, colour, allowed, alpha=255):
    """Paints a detail pixel on an allowed surface, clear of the piece's borders."""
    if not (MARGIN <= x < TILE - MARGIN and MARGIN <= y < TILE - MARGIN):
        return False
    if kinds.get((x, y)) not in allowed:
        return False
    image.putpixel((x, y), tuple(int(c) for c in colour[:3]) + (alpha,))
    return True


def pebble(image, kinds, x, y, colour, allowed):
    """A pebble: a lit pixel, a mid one beside it and its shadow below."""
    if put(image, kinds, x, y, scale(colour, 1.25), allowed):
        put(image, kinds, x + 1, y, scale(colour, 0.95), allowed)
        put(image, kinds, x, y + 1, scale(colour, 0.45), allowed | {"floor", "wall"})


def inner(spots):
    return [(x, y) for x, y in spots if MARGIN + 1 <= x < TILE - MARGIN - 1 and MARGIN + 1 <= y < TILE - MARGIN - 1]


def details(look, piece, rng, image, kinds, under, smooth, variant, face):
    """Pebbles, rubble, cracks and the world's own touch, each only now and then so edges don't repeat."""
    spots = inner(kinds.keys())
    lip = [s for s in spots if kinds[s] == "lip"]
    near_rim = [s for s in spots if kinds[s] == "floor" and -smooth[s] < 3]
    foot = [s for s in spots if kinds[s] == "floor" and under.get(s) is not None and face - 1 <= under[s] <= face + 2]
    wall = [s for s in spots if kinds[s] == "wall"]
    floor = [s for s in spots if kinds[s] == "floor"]
    columns = sorted({x for x, _ in wall})
    rubble = look["rubble"]

    if piece.mask == 15:
        if variant == 1:
            for _ in range(rng.randint(1, 2)):
                pebble(image, kinds, *rng.choice(floor), scale(rubble, 0.6), {"floor"})
        elif variant == 2:
            x, y = rng.choice(floor)
            for _ in range(rng.randint(3, 6)):
                put(image, kinds, x, y, scale(look["deep"], 0.8), {"floor"})
                x += rng.choice((-1, 0, 1))
                y += 1
        return

    if lip and rng.random() < 0.4:
        pebble(image, kinds, *rng.choice(lip), rubble, {"lip", "rim"})
    if lip and rng.random() < 0.35:
        ground_crack(image, kinds, smooth, rng, rng.choice([s for s in spots if kinds[s] == "rim"] or lip))
    for pool in (near_rim, foot):
        if pool and rng.random() < 0.35:
            pebble(image, kinds, *rng.choice(pool), scale(rubble, 0.75), {"floor"})
    if columns and rng.random() < 0.3:
        crack(image, kinds, under, rng, rng.choice(columns), scale(look["deep"], 0.6),
              (255, 150, 60) if look["touch"] == "embers" else None, face)

    touch = look["touch"]
    if touch == "roots":
        if columns and rng.random() < 0.35:
            root(image, kinds, under, rng, rng.choice(columns), face)
    elif touch == "embers":
        if (foot or near_rim) and rng.random() < 0.25:
            x, y = rng.choice(foot or near_rim)
            put(image, kinds, x, y, (255, 120, 40), {"floor"})
            put(image, kinds, x + 1, y, (150, 50, 20), {"floor"})
    elif touch == "sand":
        if columns and rng.random() < 0.3:
            spill(image, kinds, under, rng, rng.choice(columns), look["wall"], face)
        if near_rim and rng.random() < 0.3:
            for _ in range(rng.randint(1, 2)):
                put(image, kinds, *rng.choice(near_rim), scale(look["wall"], 0.7 + 0.2 * rng.random()), {"floor"})
    elif touch == "glints":
        if rng.random() < 0.4:
            pool = wall + [s for s in spots if kinds[s] == "rim"]
            if pool:
                x, y = rng.choice(pool)
                put(image, kinds, x, y, (230, 206, 255), {"wall", "rim"})
                put(image, kinds, x + 1, y, (140, 110, 200), {"wall", "rim"})
    elif touch == "ice":
        if columns and rng.random() < 0.35:
            for _ in range(rng.randint(1, 2)):
                icicle(image, kinds, under, rng, rng.choice(columns))
        if wall and rng.random() < 0.3:
            put(image, kinds, *rng.choice(wall), (250, 252, 255), {"wall"})
    elif touch == "sinew":
        if columns and rng.random() < 0.3:
            dangle(image, kinds, under, rng, rng.choice(columns), (196, 104, 110), rng.randint(4, face + 3),
                   wiggle=rng.random() < 0.6)
        if (wall or near_rim) and rng.random() < 0.3:
            put(image, kinds, *rng.choice(wall + near_rim), (255, 196, 206), {"wall", "floor"})


def column_top(kinds, under, x):
    """The first wall pixel of a column, at the top of the wall."""
    for y in range(TILE):
        if kinds.get((x, y)) == "wall" and under.get((x, y)) == 0:
            return y
    return None


def ground_crack(image, kinds, smooth, rng, start):
    """A thin crack running back from the rim into the ground, darkening whatever ground it is."""
    x, y = start
    for _ in range(rng.randint(2, 5)):
        # Step to the neighbour farthest from the rim, with a little wander.
        options = [(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1))
                   if (x + dx, y + dy) in smooth]
        if not options:
            return
        x, y = max(options, key=lambda s: smooth[s] + rng.uniform(0, 0.8))
        if not put(image, kinds, x, y, (0, 0, 0), {"lip", "ground"}, alpha=110):
            return


def crack(image, kinds, under, rng, x, colour, glow, face):
    """A dark crack zigzagging down the wall from its top; glowing at its core on Fervidus."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    for _ in range(rng.randint(3, face - 1)):
        y += 1
        x += rng.choice((-1, 0, 0, 1))
        if not put(image, kinds, x, y, glow or colour, {"wall"}):
            return
        if glow:
            put(image, kinds, x + 1, y, (150, 40, 20), {"wall"})


def dangle(image, kinds, under, rng, x, colour, length, wiggle):
    """A strand hanging from the rim down the wall."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    for i in range(length):
        if not put(image, kinds, x, y + i, scale(colour, 1.0 + 0.1 * (i % 2)), {"wall", "floor"}):
            return
        if wiggle and rng.random() < 0.35:
            x += rng.choice((-1, 1))


def root(image, kinds, under, rng, x, face):
    """A root hanging off the rim: thick where it leaves the ground, wandering and thinning as it hangs."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    length = rng.randint(4, face + 3)
    for i in range(length):
        if not put(image, kinds, x, y + i, (60, 40, 24), {"wall", "floor"}):
            return
        if i < length // 3:
            put(image, kinds, x + 1, y + i, (104, 74, 44), {"wall", "floor"})
        if rng.random() < 0.4:
            step = rng.choice((-1, 1))
            put(image, kinds, x + step, y + i, (60, 40, 24), {"wall", "floor"})
            x += step


def spill(image, kinds, under, rng, x, sand, face):
    """Sand pouring off the rim, fanning out down the wall into a small pile at its foot."""
    top = column_top(kinds, under, x)
    if top is None:
        return
    spread = rng.randint(3, 5)
    for i in range(rng.randint(face - 2, face + 3)):
        half = i // spread
        for dx in range(-half, half + 1):
            put(image, kinds, x + dx, top + i, scale(sand, 0.85 + 0.25 * rng.random()), {"wall", "floor"})


def icicle(image, kinds, under, rng, x):
    """An icicle hanging from the rim, two pixels wide at the root and tapering."""
    y = column_top(kinds, under, x)
    if y is None:
        return
    length = rng.randint(2, 6)
    for i in range(length):
        put(image, kinds, x, y + i, (236, 248, 255), {"wall"})
        if i < length // 2:
            put(image, kinds, x + 1, y + i, (170, 214, 238), {"wall"})


def draw_deep(depths):
    """A shadow blended between the depths of the four tiles, whose centres are the piece's corners."""
    nw, ne, sw, se = (DEPTH_ALPHA[d] for d in depths)
    image = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 0))
    for y in range(TILE):
        for x in range(TILE):
            u, v = (x + 0.5) / TILE, (y + 0.5) / TILE
            alpha = lerp(lerp(nw, ne, u), lerp(sw, se, u), v)
            image.putpixel((x, y), (0, 0, 0, round(255 * alpha)))
    return image


def deep_states():
    """Every depth combination four mutually touching tiles can have, but all at the rim."""
    names = []
    for nw in range(3):
        for ne in range(3):
            for sw in range(3):
                for se in range(3):
                    depths = (nw, ne, sw, se)
                    if max(depths) - min(depths) <= 1 and max(depths) > 0:
                        names.append(depths)
    return names


def build(world, look):
    source = look["source"]
    license_id, author = look["license"]
    floor = Image.open(source).convert("RGBA").crop((0, 0, TILE, TILE))
    rsi = f"{OUT}/{world}_pit.rsi"
    os.makedirs(rsi, exist_ok=True)
    for name in os.listdir(rsi):
        if name.endswith(".png"):
            os.remove(f"{rsi}/{name}")

    states = []
    draw_piece(world, look, floor, 15, 0, 0).save(f"{rsi}/pit.png")
    states.append({"name": "pit"})

    for mask in range(1, 16):
        crossed = crossings(mask)
        bits = [BORDERS[name][1] for name in crossed]
        for combo in range(1 << len(bits)):
            labels = sum(bit for i, bit in enumerate(bits) if combo & (1 << i))
            for variant in range(variants(mask)):
                name = f"v{mask}_{labels}_{variant}"
                draw_piece(world, look, floor, mask, labels, variant).save(f"{rsi}/{name}.png")
                states.append({"name": name})

    for depths in deep_states():
        name = "deep" + "".join(str(d) for d in depths)
        draw_deep(depths).save(f"{rsi}/{name}.png")
        states.append({"name": name})

    meta = {
        "version": 1,
        "license": license_id,
        "copyright": f"Cut from {source.removeprefix('Resources/')} ({author}) and shaded into a pit, with its rim, "
                     f"shaft wall and rubble drawn over it, for Wolfgate cavern mouths by Tools/_WF/Caverns/gen_pits.py.",
        "size": {"x": TILE, "y": TILE},
        "states": states,
    }
    with open(f"{rsi}/meta.json", "w", newline="\n", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")
    return len(states)


if __name__ == "__main__":
    for world, look in WORLDS.items():
        count = build(world, look)
        print(f"{world}: {count} states, {look['license'][0]}")
