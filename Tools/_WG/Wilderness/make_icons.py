"""Builds the Wilderness module's zone icons from the radar marker glyphs.

Run from the repository root: python Tools/_WG/Wilderness/make_icons.py
"""

import json
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
GLYPHS = ROOT / "Resources/Textures/_WG/Encounters/markers.rsi"
OUT = ROOT / "Resources/Textures/_WG/Wilderness"

# state: (glyph, badge colour, glyph colour, hazard stripes)
BADGES = {
    "safe": ("shield", (46, 158, 79), (255, 255, 255), False),
    "wilderness": ("skull", (200, 30, 30), (255, 255, 255), True),
    "outpost": ("flag", (214, 140, 20), (255, 255, 255), False),
    "notsafe": ("swords", (110, 116, 124), (255, 255, 255), False),
    "marked": ("skull", (30, 30, 30), (255, 59, 48), False),
}


def glyph(name: str, colour, scale: int) -> Image.Image:
    """The glyph cropped to its pixels, recoloured and scaled up by a whole factor."""
    image = Image.open(GLYPHS / f"{name}.png").convert("RGBA")
    box = image.getbbox()
    image = image.crop(box)
    pixels = image.load()
    for x in range(image.width):
        for y in range(image.height):
            alpha = pixels[x, y][3]
            pixels[x, y] = (*colour, alpha)
    return image.resize((image.width * scale, image.height * scale), Image.NEAREST)


def outlined(image: Image.Image, colour=(0, 0, 0, 255)) -> Image.Image:
    """The image with a one-pixel outline round its pixels."""
    out = Image.new("RGBA", (image.width + 2, image.height + 2))
    mask = image.split()[3]
    for dx in (0, 1, 2):
        for dy in (0, 1, 2):
            out.paste(Image.new("RGBA", image.size, colour), (dx, dy), mask)
    out.alpha_composite(image, (1, 1))
    return out


def badge(name, fill, ink, stripes) -> Image.Image:
    size = 32
    image = Image.new("RGBA", (size, size))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((1, 1, size - 2, size - 2), radius=6, fill=(*fill, 255), outline=(15, 15, 15, 255), width=2)
    if stripes:
        stripe = Image.new("RGBA", (size, size))
        stripe_draw = ImageDraw.Draw(stripe)
        for offset in range(-size, size * 2, 8):
            stripe_draw.polygon([(offset, size), (offset + 4, size), (offset + 4 + size, 0), (offset + size, 0)], fill=(20, 20, 20, 90))
        mask = Image.new("L", (size, size))
        ImageDraw.Draw(mask).rounded_rectangle((3, 3, size - 4, size - 4), radius=4, fill=255)
        image.paste(stripe, (0, 0), Image.composite(stripe.split()[3], Image.new("L", (size, size)), mask))
    mark = glyph(name, ink, 1)
    scale = max(1, 20 // max(mark.width, mark.height))
    mark = outlined(glyph(name, ink, scale))
    image.alpha_composite(mark, ((size - mark.width) // 2, (size - mark.height) // 2))
    return image


# The skull over a marked person, at the 8 px of the other status icons: K outline, R bone.
OVERHEAD_SKULL = [
    ".KKKKKK.",
    "KRRRRRRK",
    "KRKRRKRK",
    "KRKRRKRK",
    "KRRKKRRK",
    ".KRRRRK.",
    ".KRKKRK.",
    "..KKKK..",
]


def overhead_skull() -> Image.Image:
    colours = {"K": (20, 20, 20, 255), "R": (255, 59, 48, 255)}
    image = Image.new("RGBA", (8, 8))
    for y, row in enumerate(OVERHEAD_SKULL):
        for x, cell in enumerate(row):
            if cell in colours:
                image.putpixel((x, y), colours[cell])
    return image


def write_rsi(path: Path, size: int, states: dict, copyright: str):
    path.mkdir(parents=True, exist_ok=True)
    for state, image in states.items():
        image.save(path / f"{state}.png")
    meta = {
        "version": 1,
        "license": "CC0-1.0",
        "copyright": copyright,
        "size": {"x": size, "y": size},
        "states": [{"name": state} for state in states],
    }
    (path / "meta.json").write_text(json.dumps(meta, indent=4) + "\n", encoding="utf-8", newline="\n")


def main():
    badges = {state: badge(*spec) for state, spec in BADGES.items()}
    write_rsi(OUT / "zones.rsi", 32, badges,
              "Built by Tools/_WG/Wilderness/make_icons.py from the Wolfgate radar marker glyphs.")
    write_rsi(OUT / "skull_icon.rsi", 8, {"skull": overhead_skull()},
              "Built by Tools/_WG/Wilderness/make_icons.py, after the Wolfgate radar marker skull.")


if __name__ == "__main__":
    main()
