"""
Builds Resources/Textures/_WF/Tributes/livingston.rsi from Meridian Rift's aquarium sprites: the aquarium in
layers, and Livingston, a lionfish recoloured from the cardinalfish, swimming back and forth.

    python Tools/_WF/Tributes/livingston.py E:/GitHub/Meridian-Rift
"""
import json
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(REPO, "Tools", "_WF", "MeridianMarkings"))
from ss13 import Dmi  # noqa: E402

OUT = os.path.join(REPO, "Resources", "Textures", "_WF", "Tributes", "livingston.rsi")
TANK_LAYERS = {
    "base": "aquarium_base",
    "water": "aquarium_glass_water",
    "sand": "sand",
    "rocks": "rocks",
    "seaweed-back": "seaweeds_back",
    "seaweed-front": "seaweeds_front",
    "borders": "aquarium_borders",
}
FISH_WIDTH = 13
SWIM_LEFT, SWIM_RIGHT, SWIM_Y = 4, 16, 6
SWIM_DELAY = 0.25


def lionfish(cardinal):
    """Turns the cardinalfish's black stripes maroon and its grey ones a pale red."""
    img = cardinal.copy()
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            lum = (r + g + b) / 3
            if lum < 70:
                px[x, y] = (int(90 + lum), int(18 + lum * 0.2), int(16 + lum * 0.2), a)
            elif lum < 190:
                px[x, y] = (int(150 + lum * 0.4), int(60 + lum * 0.3), int(50 + lum * 0.25), a)
    img = img.crop(img.getbbox())
    height = max(1, round(img.height * FISH_WIDTH / img.width))
    return img.resize((FISH_WIDTH, height), Image.NEAREST)


def swim_frames(fish):
    """The fish swims right, turns, swims left, turns."""
    right = fish.transpose(Image.FLIP_LEFT_RIGHT)
    frames = []
    path = list(range(SWIM_LEFT, SWIM_RIGHT + 1)) + list(range(SWIM_RIGHT, SWIM_LEFT - 1, -1))
    for i, x in enumerate(path):
        sprite = right if i <= SWIM_RIGHT - SWIM_LEFT else fish
        bob = (0, 0, 1, 1)[(i // 3) % 4]
        frame = Image.new("RGBA", (32, 32))
        frame.alpha_composite(sprite, (x, SWIM_Y + bob))
        frames.append(frame)
    return frames


def save_state(name, frames, delays, states):
    sheet = Image.new("RGBA", (32 * len(frames), 32))
    for i, frame in enumerate(frames):
        sheet.alpha_composite(frame, (32 * i, 0))
    sheet.save(os.path.join(OUT, name + ".png"))
    state = {"name": name}
    if len(frames) > 1:
        state["delays"] = [delays]
    states.append(state)


def main(meridian):
    icons = os.path.join(meridian, "icons", "obj", "aquarium")
    tanks = Dmi.load(os.path.join(icons, "tanks.dmi"))
    fish = Dmi.load(os.path.join(icons, "fish.dmi"))
    os.makedirs(OUT, exist_ok=True)
    states = []
    for name, source in TANK_LAYERS.items():
        state = tanks.states[source]
        frames = [row[0] for row in state.images]
        delays = [d / 10 for d in state.delays] if state.delays else [0.1] * len(frames)
        save_state(name, frames, delays, states)
    swim = swim_frames(lionfish(fish.states["cardinalfish"].images[0][0]))
    save_state("livingston", swim, [SWIM_DELAY] * len(swim), states)

    icon = Image.new("RGBA", (32, 32))
    for name in ["base", "water", "sand", "seaweed-back", "rocks", "livingston", "seaweed-front", "borders"]:
        icon.alpha_composite(Image.open(os.path.join(OUT, name + ".png")).crop((0, 0, 32, 32)))
    icon.save(os.path.join(OUT, "icon.png"))
    states.insert(0, {"name": "icon"})

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Aquarium and fish sprites from Meridian Rift (tgstation lineage), "
                     "https://github.com/tgstation/tgstation; lionfish recolour and swim frames by Wolfgate, "
                     "made with Tools/_WF/Tributes/livingston.py",
        "size": {"x": 32, "y": 32},
        "states": states,
    }
    with open(os.path.join(OUT, "meta.json"), "w", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main(sys.argv[1])
