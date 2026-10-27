"""Draws the 8x8 learner-plate mark for Resources/Textures/_WF/Newbie/newbie_mark.rsi."""
import json
import pathlib

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Textures/_WF/Newbie/newbie_mark.rsi"

EDGE = (40, 40, 40, 255)
PLATE = (245, 245, 240, 255)
LETTER = (210, 30, 30, 255)

# A white plate with a dark rim and a red L, read top row first.
ROWS = [
    "########",
    "#......#",
    "#.L....#",
    "#.L....#",
    "#.L....#",
    "#.LLLL.#",
    "#......#",
    "########",
]


def main():
    img = Image.new("RGBA", (8, 8))
    colours = {"#": EDGE, ".": PLATE, "L": LETTER}
    for y, row in enumerate(ROWS):
        for x, cell in enumerate(row):
            img.putpixel((x, y), colours[cell])

    OUT.mkdir(parents=True, exist_ok=True)
    img.save(OUT / "mark.png")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Made for Wolfgate by Tools/_WF/Newbie/gen_newbie_icon.py",
        "size": {"x": 8, "y": 8},
        "states": [{"name": "mark"}],
    }
    (OUT / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", newline="\n")


if __name__ == "__main__":
    main()
