"""Draws the 8x8 Mentor crown plates for Resources/Textures/_WG/Mentors/mentor_mark.rsi.

The newbie learner plate (Tools/_WG/Newbie/gen_newbie_icon.py) with a crown in place of the L, one state per tier:
a low silver crown for Trainees, the gold crown for Mentors, the gold crown on an olive plate for Seniors.
"""
import json
import pathlib

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Textures/_WG/Mentors/mentor_mark.rsi"

COLOURS = {
    ".": (0, 0, 0, 0),
    "s": (0, 0, 0, 90),
    "r": (150, 146, 138, 255),
    "w": (250, 247, 238, 255),
    "g": (214, 160, 52, 255),
}

# The plate with a three-point gold crown, read top row first.
CROWN = [
    ".rrrrr..",
    "rwwgwwr.",
    "rgwgwgrs",
    "rgggggrs",
    "rgggggrs",
    "rwwwwwrs",
    ".rrrrrss",
    "..sssss.",
]

# The same crown without its middle point.
LOW = [CROWN[0], "rwwwwwr.", *CROWN[2:]]

TIERS = {
    "trainee": (LOW, {"g": (176, 180, 188, 255)}),
    "mentor": (CROWN, {}),
    "senior": (CROWN, {"w": (69, 87, 77, 255), "r": (40, 50, 44, 255)}),
}


def draw(rows, colours):
    palette = dict(COLOURS)
    palette.update(colours)
    img = Image.new("RGBA", (8, 8))
    for y, row in enumerate(rows):
        for x, cell in enumerate(row):
            img.putpixel((x, y), palette[cell])
    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (rows, colours) in TIERS.items():
        draw(rows, colours).save(OUT / f"{name}.png")

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Made for Wolfgate by Tools/_WG/Mentors/gen_mentor_marks.py",
        "size": {"x": 8, "y": 8},
        "states": [{"name": name} for name in TIERS],
    }
    (OUT / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", newline="\n")


if __name__ == "__main__":
    main()
