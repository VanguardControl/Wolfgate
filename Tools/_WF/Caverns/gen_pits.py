"""Builds the cavern mouth pit RSIs: one per world, cut from the world's landing floor.

Each RSI has a full `pit` state (a tile with pit on every side) and 32 corner states, `<corner><mask>` for the
corners ne, nw, se and sw. A corner's mask says which of its three neighbours are also pit: 1 the neighbour above or
below it, 2 the neighbour beside it, 4 the diagonal one. WFCavernShadeVisualsSystem picks the four states per tile.

Where a pit meets solid ground it gets a dark rim; its north side shows the shaft wall dropping away, the other sides an
inner shadow. Run from the repo root: python Tools/_WF/Caverns/gen_pits.py
"""
import json
import math
import os

from PIL import Image

OUT = "Resources/Textures/_WF/Caverns/Mouths"
TILE = 32
HALF = TILE // 2

FLOOR = 0.55  # the floor a level down
RIM = 0.12  # the dark line where the ground breaks off
FACE_TOP, FACE_BOTTOM = 0.58, 0.18  # the shaft wall under the north lip, lit at the top
FACE = 12  # shaft wall height in pixels
CONTACT = 5  # shadow where the wall meets the floor
SIDE = 7  # inner shadow width on the east, west and south sides
SIDE_DARK = 0.3
CORNER = 6  # inner-corner shadow radius

TG = ("CC-BY-SA-3.0", "tgstation")
MOJAVE = ("CC-BY-NC-SA-3.0", "Mojave-Sun")

WORLDS = {
    "asclepiu": ("Resources/Textures/_Mono/Tiles/planet/water.png", ("CC-BY-SA-3.0", "Monolith (Grid Networks, #4690)")),
    "fervidus": ("Resources/Textures/Tiles/cavedrought.png", MOJAVE),
    "merak": ("Resources/Textures/Tiles/Asteroid/asteroid.png", TG),
    "aerumna": ("Resources/Textures/Tiles/chromite.png", ("CC-BY-NC-SA-3.0", "Mojave-Sun, recoloured by TheShuEd")),
    "thrascias": ("Resources/Textures/Tiles/Planet/Snow/snow.png", TG),
    "carcinoma": ("Resources/Textures/Tiles/Asteroid/ironsand.png", TG),
}

# Corner: x range, y range, whether it sits on the north side, whether it sits on the east side.
CORNERS = {
    "ne": (range(HALF, TILE), range(0, HALF), True, True),
    "nw": (range(0, HALF), range(0, HALF), True, False),
    "se": (range(HALF, TILE), range(HALF, TILE), False, True),
    "sw": (range(0, HALF), range(HALF, TILE), False, False),
}


def lerp(a, b, t):
    return a + (b - a) * max(0.0, min(1.0, t))


def shade(pixel, factor):
    r, g, b, a = pixel
    return min(255, round(r * factor)), min(255, round(g * factor)), min(255, round(b * factor)), a


def factor_at(x, y, north, east, mask):
    """Brightness at a pixel of one corner quadrant, given which neighbours are pit."""
    vertical_pit = bool(mask & 1)
    side_pit = bool(mask & 2)
    diagonal_pit = bool(mask & 4)

    dv = y if north else TILE - 1 - y  # distance from the edge above or below
    dh = TILE - 1 - x if east else x  # distance from the edge beside

    factor = FLOOR

    if not vertical_pit:
        if north:
            if dv == 0:
                factor = RIM
            elif dv < FACE:
                factor = lerp(FACE_TOP, FACE_BOTTOM, (dv - 1) / (FACE - 2))
            elif dv < FACE + CONTACT:
                factor = FLOOR * lerp(0.35, 1.0, (dv - FACE + 1) / CONTACT)
        elif dv == 0:
            factor = RIM
        elif dv <= SIDE // 2:
            factor = FLOOR * lerp(SIDE_DARK, 1.0, dv / (SIDE // 2 + 1))

    if not side_pit:
        if dh == 0:
            factor = RIM
        elif dh <= SIDE:
            factor *= lerp(SIDE_DARK, 1.0, dh / (SIDE + 1))

    if vertical_pit and side_pit and not diagonal_pit:
        # The diagonal tile is ground: its lip and shadow wrap round the corner.
        distance = math.hypot(dv, dh)
        if north and dh <= 1 and dv < FACE:
            factor = min(factor, lerp(FACE_TOP, FACE_BOTTOM, dv / FACE))
        elif distance <= CORNER:
            factor *= lerp(SIDE_DARK, 1.0, distance / (CORNER + 1))

    return factor


def build(world, source, license_id, author):
    floor = Image.open(source).convert("RGBA").crop((0, 0, TILE, TILE))
    rsi = f"{OUT}/{world}_pit.rsi"
    os.makedirs(rsi, exist_ok=True)

    full = Image.new("RGBA", (TILE, TILE))
    for y in range(TILE):
        for x in range(TILE):
            full.putpixel((x, y), shade(floor.getpixel((x, y)), FLOOR))
    full.save(f"{rsi}/pit.png")
    states = [{"name": "pit"}]

    for corner, (xs, ys, north, east) in CORNERS.items():
        for mask in range(8):
            image = Image.new("RGBA", (TILE, TILE))
            for y in ys:
                for x in xs:
                    image.putpixel((x, y), shade(floor.getpixel((x, y)), factor_at(x, y, north, east, mask)))
            name = f"{corner}{mask}"
            image.save(f"{rsi}/{name}.png")
            states.append({"name": name})

    meta = {
        "version": 1,
        "license": license_id,
        "copyright": f"Cut from {source.removeprefix('Resources/')} ({author}) and shaded into a pit for Wolfgate "
                     f"cavern mouths by Tools/_WF/Caverns/gen_pits.py.",
        "size": {"x": TILE, "y": TILE},
        "states": states,
    }
    with open(f"{rsi}/meta.json", "w", newline="\n", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    for world, (source, (license_id, author)) in WORLDS.items():
        build(world, source, license_id, author)
        print(f"{world}: {license_id}")
