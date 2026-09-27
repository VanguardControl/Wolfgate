"""Builds the cavern mouth pit RSIs: one per world, cut from the world's landing floor.

Each RSI has a full `pit` state (a tile with pit on every side) and 96 corner states, `<corner><mask>_<variant>` for
the corners ne, nw, se and sw, masks 0-7 and variants 0-2. A corner's mask says which of its three neighbours are also
pit: 1 the neighbour above or below it, 2 the neighbour beside it, 4 the diagonal one. WFCavernShadeVisualsSystem picks
the four states per tile, and the variant from a stable hash of the tile.

Where a pit meets solid ground it gets a crumbling lip: a dark rim whose line wanders in and out, with bits of ground
overhanging it, pebbles on it and rubble below it. A pit corner whose two sides meet ground is rounded. The north side
shows the shaft wall dropping away, with strata and cracks and rubble at its foot; the other sides an inner shadow. Each
world adds its own touch: roots, embers, sand spill, crystal glints, icicles and frost, or sinew and wet sheen.

Every state is drawn from the same rule over its tile and three neighbours, and nothing reaches 16 pixels from a tile
edge, so a corner meets its neighbours seamlessly. What varies between variants is pinned to the base drawing at the
corner's borders. Run from the repo root: python Tools/_WF/Caverns/gen_pits.py
"""
import json
import math
import os
import random

from PIL import Image

OUT = "Resources/Textures/_WF/Caverns/Mouths"
TILE = 32
HALF = TILE // 2
VARIANTS = 3

FLOOR = 0.6  # the floor a level down
RIM = 0.3  # the dark line where the ground breaks off, as a share of the lip colour
LIP = 1.6  # the lip's base line, in pixels from the tile edge: a shaded edge of ground, then the rim
JAG_UP, JAG_DOWN = 3.0, 1.2  # how far the lip may bulge into the pit and bite back into the ground
ROUND = 9.0  # radius of a pit corner whose two sides meet ground
FACE = 9  # shaft wall height in pixels
CONTACT = 3  # shadow where the wall meets the floor
SIDE = 7  # inner shadow width on the east, west and south sides
SIDE_DARK = 0.35
REACH = 15  # nothing is drawn farther than this from a tile edge, so corners join seamlessly
MARGIN = 2  # variant details keep this far from a corner's borders

TG = ("CC-BY-SA-3.0", "tgstation")
MOJAVE = ("CC-BY-NC-SA-3.0", "Mojave-Sun")

# Landing floor, its licence, and the world's look: lip (ground seen from above), wall top and bottom, touch.
WORLDS = {
    "asclepiu": {
        "source": "Resources/Textures/_Mono/Tiles/planet/water.png",
        "license": ("CC-BY-SA-3.0", "Monolith (Grid Networks, #4690)"),
        "lip": (74, 96, 52), "wall": (104, 78, 50), "deep": (126, 120, 104), "touch": "roots", "tint": (0.9, 1.0, 1.0),
    },
    "fervidus": {
        "source": "Resources/Textures/Tiles/cavedrought.png",
        "license": MOJAVE,
        "lip": (44, 42, 42), "wall": (104, 80, 68), "deep": (70, 40, 30), "touch": "embers", "tint": (1.05, 0.95, 0.9),
    },
    "merak": {
        "source": "Resources/Textures/Tiles/Asteroid/asteroid.png",
        "license": TG,
        "lip": (160, 124, 80), "wall": (176, 128, 80), "deep": (122, 82, 52), "touch": "sand", "tint": (1.0, 0.95, 0.9),
    },
    "aerumna": {
        "source": "Resources/Textures/Tiles/chromite.png",
        "license": ("CC-BY-NC-SA-3.0", "Mojave-Sun, recoloured by TheShuEd"),
        "lip": (36, 34, 58), "wall": (64, 58, 92), "deep": (30, 27, 44), "touch": "glints", "tint": (0.95, 0.9, 1.1),
    },
    "thrascias": {
        "source": "Resources/Textures/Tiles/Planet/Snow/snow.png",
        "license": TG,
        "lip": (232, 238, 244), "wall": (176, 206, 228), "deep": (92, 126, 160), "touch": "ice", "tint": (0.8, 0.9, 1.05),
    },
    "carcinoma": {
        "source": "Resources/Textures/Tiles/Asteroid/ironsand.png",
        "license": TG,
        "lip": (140, 26, 58), "wall": (150, 44, 60), "deep": (74, 14, 28), "touch": "sinew", "tint": (1.05, 0.9, 0.9),
    },
}

# Corner: whether it sits on the north side, whether it sits on the east side.
CORNERS = {"ne": (True, True), "nw": (True, False), "se": (False, True), "sw": (False, False)}


def lerp(a, b, t):
    return a + (b - a) * max(0.0, min(1.0, t))


def mix(c1, c2, t):
    return tuple(lerp(a, b, t) for a, b in zip(c1, c2))


def scale(colour, factor):
    return tuple(max(0, min(255, round(c * factor))) for c in colour[:3])


def noise(x, y, salt):
    """A stable value in [-1, 1] for a tile pixel, the same in every state, so textures join across corners."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFF) / 32767.5 - 1.0


def rect_distance(pv, ph, rect):
    """Distance from a point to an axis-aligned rectangle (pv0, pv1, ph0, ph1), 0 inside it."""
    pv0, pv1, ph0, ph1 = rect
    dv = max(pv0 - pv, 0.0, pv - pv1)
    dh = max(ph0 - ph, 0.0, ph - ph1)
    return math.hypot(dv, dh)


class Corner:
    """One corner quadrant of a pit tile, in local coordinates: pv is the distance in from the edge above or below it,
    ph the distance in from the edge beside it. Negative values lie in the neighbouring tiles."""

    def __init__(self, north, east, mask):
        self.north = north
        self.east = east
        self.vertical_ground = not mask & 1
        self.side_ground = not mask & 2
        self.diagonal_ground = not mask & 4
        self.ground = []
        if self.vertical_ground:
            self.ground.append((-TILE, 0.0, 0.0, TILE))
        if self.side_ground:
            self.ground.append((0.0, TILE, -TILE, 0.0))
        if self.diagonal_ground:
            self.ground.append((-TILE, 0.0, -TILE, 0.0))
        self.jag_v = [0.0] * HALF
        self.jag_s = [0.0] * HALF

    def local(self, x, y):
        pv = (y if self.north else TILE - 1 - y) + 0.5
        ph = (TILE - 1 - x if self.east else x) + 0.5
        return pv, ph

    def tile(self, pv, ph):
        y = int(pv - 0.5) if self.north else TILE - 1 - int(pv - 0.5)
        x = TILE - 1 - int(ph - 0.5) if self.east else int(ph - 0.5)
        return x, y

    def distance(self, pv, ph):
        """Distance from a pixel centre to solid ground, negative inside it; a corner meeting ground on both sides is rounded."""
        if not self.ground:
            return 99.0
        if any(r[0] <= pv < r[1] and r[2] <= ph < r[3] for r in self.ground):
            return -1.0
        if self.vertical_ground and self.side_ground and 0 <= pv < ROUND and 0 <= ph < ROUND:
            return ROUND - math.hypot(ROUND - pv, ROUND - ph)
        return min(rect_distance(pv, ph, r) for r in self.ground)

    def lip(self, pv, ph):
        """Where the lip ends at a pixel: its base line, moved by the variant's jag along this corner's own edges."""
        inside = 0 <= pv < HALF and 0 <= ph < HALF
        if not inside:
            return LIP
        jag_v = self.jag_v[int(ph)] if self.vertical_ground else None
        jag_s = self.jag_s[int(pv)] if self.side_ground else None
        if jag_v is not None and jag_s is not None:
            weight = ph / (pv + ph)
            return LIP + jag_v * weight + jag_s * (1 - weight)
        if jag_v is not None:
            return LIP + jag_v
        if jag_s is not None:
            return LIP + jag_s
        return LIP

    def edge(self, pv, ph):
        """Distance from a pixel into the pit past the lip: below -1 overhanging ground, -1 to 0 the rim, then pit."""
        return self.distance(pv, ph) - self.lip(pv, ph)

    def under_lip(self, pv, ph):
        """On a north corner, how far a pixel lies below the lip straight above it, or None when no lip is near."""
        if not self.north:
            return None
        for step in range(0, REACH + 1):
            if self.edge(pv - step, ph) < 0:
                return step - 1
        return None


def jag(rng):
    """A lip profile along one edge of a corner: a few bulges and bites, pinned to zero at both ends."""
    profile = [0.0] * HALF
    for _ in range(rng.randint(2, 3)):
        centre = rng.uniform(3, HALF - 4)
        width = rng.uniform(1.2, 2.8)
        height = rng.uniform(-JAG_DOWN, JAG_UP) if rng.random() < 0.3 else rng.uniform(1.0, JAG_UP)
        for i in range(HALF):
            taper = max(0.0, min(1.0, min(i, HALF - 1 - i) / 3.0))
            profile[i] += height * math.exp(-((i - centre) / width) ** 2) * taper
    return [max(-JAG_DOWN, min(JAG_UP, v)) for v in profile]


def draw_corner(world, look, floor, corner_name, mask, variant):
    north, east = CORNERS[corner_name]
    corner = Corner(north, east, mask)
    rng = random.Random(f"{world}:{corner_name}:{mask}:{variant}")
    corner.jag_v = jag(rng)
    corner.jag_s = jag(rng)
    salt = sum(ord(c) for c in world)

    xs = range(HALF, TILE) if east else range(0, HALF)
    ys = range(0, HALF) if north else range(HALF, TILE)
    image = Image.new("RGBA", (TILE, TILE))
    kinds = {}
    under = {}

    for y in ys:
        for x in xs:
            pv, ph = corner.local(x, y)
            e = corner.edge(pv, ph)
            u = corner.under_lip(pv, ph)
            under[(x, y)] = u
            n = noise(x, y, salt)

            if e < -2:
                colour = scale(look["lip"], 1.02 + 0.12 * n)
                kinds[(x, y)] = "lip"
            elif e < -1:
                colour = scale(look["lip"], 0.72 + 0.08 * n)  # the ground darkens as it breaks off
                kinds[(x, y)] = "lip"
            elif e < 0:
                colour = scale(look["lip"], RIM + 0.05 * n)
                kinds[(x, y)] = "rim"
            elif u is not None and u < FACE and pv < REACH:
                colour = wall_colour(look, x, u, n)
                kinds[(x, y)] = "wall"
            else:
                factor = FLOOR
                if e < SIDE:
                    factor *= lerp(SIDE_DARK, 1.0, e / SIDE)
                if u is not None and u < FACE + CONTACT and pv < REACH:
                    factor *= lerp(0.45, 1.0, (u - FACE + 1) / (CONTACT + 1))
                colour = tint(scale(floor.getpixel((x, y)), factor), look["tint"])
                kinds[(x, y)] = "floor"

            image.putpixel((x, y), colour + (255,))

    # Variant details may only touch pixels clear of the corner's borders.
    ix, iy = inner(xs, ys)
    inside = {(x, y): kind for (x, y), kind in kinds.items() if x in ix and y in iy}
    details(look, corner, rng, image, inside, under, ix, iy, variant)
    return image


def tint(colour, factors):
    return tuple(max(0, min(255, round(c * f))) for c, f in zip(colour, factors))


def wall_colour(look, x, u, n):
    """The shaft wall under a north lip: strata that wobble along the tile, lit at the top and dark at the foot."""
    depth = u / (FACE - 1)
    wobble = 0.8 * math.sin(2 * math.pi * x / HALF) + 0.5 * math.sin(2 * math.pi * x / TILE + 1.3)
    band = int((u + 0.5 + wobble) // 2.2)
    base = mix(look["wall"], look["deep"], depth * 0.8)
    light = lerp(1.1, 0.45, depth) * (1.0 + (0.14 if band % 2 else -0.1)) * (1.0 + 0.08 * n)
    if u == 0:
        light *= 0.5  # the shadow right under the overhang
    elif u == 1:
        light *= 1.12  # the lit top of the cut
    return scale(base, light)


def put(image, kinds, x, y, colour, allowed):
    """Paints a detail pixel if it is one of the corner's inner pixels and on an allowed surface."""
    if (x, y) not in kinds or kinds[(x, y)] not in allowed:
        return False
    image.putpixel((x, y), tuple(int(c) for c in colour[:3]) + (255,))
    return True


def inner(xs, ys):
    """The corner's pixels at least MARGIN from its borders, where a variant may draw."""
    return [x for x in xs if xs[0] + MARGIN <= x <= xs[-1] - MARGIN], [y for y in ys if ys[0] + MARGIN <= y <= ys[-1] - MARGIN]


def pebble(image, kinds, x, y, colour, allowed):
    """A pebble: a lit pixel, a mid one beside it and its shadow below."""
    if put(image, kinds, x, y, scale(colour, 1.25), allowed):
        put(image, kinds, x + 1, y, scale(colour, 0.95), allowed)
        put(image, kinds, x, y + 1, scale(colour, 0.45), allowed | {"floor", "wall"})


def details(look, corner, rng, image, kinds, under, xs, ys, variant):
    """Pebbles, rubble, cracks and the world's own touch, on the corner's inner pixels only."""
    spots = [(x, y) for y in ys for x in xs]
    lip_spots = [s for s in spots if kinds[s] in ("lip", "rim")]
    near_lip = [s for s in spots if kinds[s] == "floor" and corner.edge(*corner.local(*s)) < 3]
    foot = [s for s in spots if kinds[s] == "floor" and under[s] is not None and FACE - 1 <= under[s] <= FACE + 3]
    wall = [s for s in spots if kinds[s] == "wall"]
    floor_all = [s for s in spots if kinds[s] == "floor"]

    rubble = scale(mix(look["wall"], look["deep"], 0.4), 0.95)
    for _ in range(rng.randint(0, 2)):
        if lip_spots:
            pebble(image, kinds, *rng.choice(lip_spots), look["lip"], {"lip", "rim"})
    for _ in range(rng.randint(0, 2)):
        if near_lip:
            pebble(image, kinds, *rng.choice(near_lip), rubble, {"floor"})
    for _ in range(rng.randint(0, 2)):
        if foot:
            pebble(image, kinds, *rng.choice(foot), rubble, {"floor"})
    if not lip_spots and not wall and floor_all and variant == 2:
        pebble(image, kinds, *rng.choice(floor_all), scale(rubble, 0.7), {"floor"})

    columns = sorted({x for x, _ in wall})
    if columns and rng.random() < 0.7:
        crack(image, kinds, under, rng, rng.choice(columns), ys, scale(look["deep"], 0.45),
              (255, 150, 60) if look["touch"] == "embers" else None)

    touch = look["touch"]
    if touch == "roots" and columns:
        for _ in range(rng.randint(1, 2)):
            root(image, kinds, under, rng, rng.choice(columns), ys)
        for _ in range(rng.randint(0, 3)):
            if lip_spots:
                put(image, kinds, *rng.choice(lip_spots), scale(look["lip"], 1.35), {"lip"})
    elif touch == "embers":
        for _ in range(rng.randint(0, 2)):
            if foot or near_lip:
                x, y = rng.choice(foot or near_lip)
                put(image, kinds, x, y, (255, 120, 40), {"floor"})
    elif touch == "sand":
        if columns and rng.random() < 0.8:
            spill(image, kinds, under, rng, rng.choice(columns), look["lip"])
        for _ in range(rng.randint(1, 3)):
            if near_lip:
                put(image, kinds, *rng.choice(near_lip), scale(look["lip"], 0.9 + 0.2 * rng.random()), {"floor"})
    elif touch == "glints":
        for _ in range(rng.randint(1, 3)):
            pool = wall + lip_spots
            if pool:
                x, y = rng.choice(pool)
                put(image, kinds, x, y, (214, 186, 255), {"wall", "lip", "rim"})
                put(image, kinds, x + 1, y, (120, 96, 170), {"wall", "lip", "rim"})
    elif touch == "ice":
        for _ in range(rng.randint(1, 3)):
            if columns:
                icicle(image, kinds, under, rng, rng.choice(columns), ys)
        for _ in range(rng.randint(1, 4)):
            if lip_spots or near_lip:
                put(image, kinds, *rng.choice(lip_spots + near_lip), (250, 252, 255), {"lip", "rim", "floor"})
    elif touch == "sinew":
        for _ in range(rng.randint(1, 2)):
            if columns:
                dangle(image, kinds, under, rng, rng.choice(columns), ys, (196, 104, 110), FACE, wiggle=False)
        for _ in range(rng.randint(0, 2)):
            pool = wall + near_lip
            if pool:
                put(image, kinds, *rng.choice(pool), (255, 196, 206), {"wall", "floor"})


def column_top(kinds, under, x, ys):
    """The first wall pixel of a column, at the top of the wall."""
    for y in ys:
        if kinds.get((x, y)) == "wall" and under[(x, y)] == 0:
            return y
    return None


def crack(image, kinds, under, rng, x, ys, colour, glow):
    """A dark crack zigzagging down the wall from its top; glowing at its core on Fervidus."""
    y = column_top(kinds, under, x, ys)
    if y is None:
        return
    for _ in range(rng.randint(3, FACE - 1)):
        y += 1
        x += rng.choice((-1, 0, 0, 1))
        if not put(image, kinds, x, y, glow or colour, {"wall"}):
            return
        if glow:
            put(image, kinds, x + 1, y, (150, 40, 20), {"wall"})


def dangle(image, kinds, under, rng, x, ys, colour, length, wiggle):
    """Something hanging from the lip down the wall: a root, or a strand of sinew."""
    y = column_top(kinds, under, x, ys)
    if y is None:
        return
    for i in range(length):
        if not put(image, kinds, x, y + i, scale(colour, 1.0 + 0.1 * (i % 2)), {"wall", "floor"}):
            return
        if wiggle and rng.random() < 0.35:
            x += rng.choice((-1, 1))


def root(image, kinds, under, rng, x, ys):
    """A root hanging off the lip: thick where it leaves the ground, wandering and thinning as it hangs."""
    y = column_top(kinds, under, x, ys)
    if y is None:
        return
    length = rng.randint(5, FACE + 3)
    for i in range(length):
        if not put(image, kinds, x, y + i, (60, 40, 24), {"wall", "floor"}):
            return
        if i < length // 3:
            put(image, kinds, x + 1, y + i, (104, 74, 44), {"wall", "floor"})
        if rng.random() < 0.4:
            step = rng.choice((-1, 1))
            put(image, kinds, x + step, y + i, (60, 40, 24), {"wall", "floor"})
            x += step


def spill(image, kinds, under, rng, x, sand):
    """Sand pouring off the lip, fanning out down the wall into a small pile at its foot."""
    top = None
    for (px, py), kind in kinds.items():
        if px == x and kind == "wall" and under[(px, py)] == 0:
            top = py
    if top is None:
        return
    for i in range(FACE + 2):
        half = i // 3
        for dx in range(-half, half + 1):
            shade_ = 0.85 + 0.25 * rng.random()
            put(image, kinds, x + dx, top + i, scale(sand, shade_), {"wall", "floor"})


def icicle(image, kinds, under, rng, x, ys):
    """An icicle hanging from the lip, two pixels wide at the root and tapering."""
    y = column_top(kinds, under, x, ys)
    if y is None:
        return
    length = rng.randint(3, 7)
    for i in range(length):
        put(image, kinds, x, y + i, (236, 248, 255), {"wall"})
        if i < length // 2:
            put(image, kinds, x + 1, y + i, (170, 214, 238), {"wall"})


def build(world, look):
    source = look["source"]
    license_id, author = look["license"]
    floor = Image.open(source).convert("RGBA").crop((0, 0, TILE, TILE))
    rsi = f"{OUT}/{world}_pit.rsi"
    os.makedirs(rsi, exist_ok=True)
    for name in os.listdir(rsi):
        if name.endswith(".png"):
            os.remove(f"{rsi}/{name}")

    full = Image.new("RGBA", (TILE, TILE))
    for y in range(TILE):
        for x in range(TILE):
            full.putpixel((x, y), scale(floor.getpixel((x, y)), FLOOR) + (255,))
    full.save(f"{rsi}/pit.png")
    states = [{"name": "pit"}]

    for corner in CORNERS:
        for mask in range(8):
            for variant in range(VARIANTS):
                name = f"{corner}{mask}_{variant}"
                draw_corner(world, look, floor, corner, mask, variant).save(f"{rsi}/{name}.png")
                states.append({"name": name})

    meta = {
        "version": 1,
        "license": license_id,
        "copyright": f"Cut from {source.removeprefix('Resources/')} ({author}) and shaded into a pit, with its lip, "
                     f"shaft wall and rubble drawn over it, for Wolfgate cavern mouths by Tools/_WF/Caverns/gen_pits.py.",
        "size": {"x": TILE, "y": TILE},
        "states": states,
    }
    with open(f"{rsi}/meta.json", "w", newline="\n", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    for world, look in WORLDS.items():
        build(world, look)
        print(f"{world}: {look['license'][0]}")
