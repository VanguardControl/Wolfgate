"""Ports Meridian Rift's digitigrade leg art into the RSIs the leg style prototypes point at.

Run from the repository root against a Meridian Rift checkout:

    python Tools/_WF/LegStyle/port.py --source <Meridian-Rift checkout>

An SS13 leg is one sprite; here it is a leg and a foot layer, so each is cut at the row the upstream
species cut theirs. `shade` scales a set's brightness to the plantigrade legs of the species that wears it.
"""
import argparse
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "MeridianMarkings"))
import ss13  # noqa: E402

REPOSITORY = "https://github.com/Aphelion-Moon/Meridian-Rift"
ICONS = "modular_nova/modules/bodyparts/icons"
OUTPUT = "Resources/Textures/_WF/LegStyle"
# Rows from here down are the foot.
FOOT_ROW = 29

SETS = {
    "digitigrade_mammal.rsi": {
        "sheet": "mammal_parts_greyscale.dmi",
        "left": "mammal_l_leg_digi",
        "right": "mammal_r_leg_digi",
        "shade": 0.89,
    },
    "digitigrade_human.rsi": {
        "sheet": "human_parts_greyscale.dmi",
        "left": "human_l_leg_digi",
        "right": "human_r_leg_digi",
        "shade": 1.0,
    },
}


def _shade(image, factor):
    if factor == 1.0:
        return image
    r, g, b, a = image.split()
    r, g, b = (c.point(lambda v: min(255, round(v * factor))) for c in (r, g, b))
    return ss13.Image.merge("RGBA", (r, g, b, a))


def _strip(cells, top, bottom):
    """The four facings side by side, keeping only rows top..bottom."""
    sheet = ss13.Image.new("RGBA", (64, 64))
    for i, cell in enumerate(cells):
        part = ss13.Image.new("RGBA", cell.size)
        part.paste(cell.crop((0, top, cell.width, bottom)), (0, top))
        sheet.paste(part, ((i % 2) * 32, (i // 2) * 32))
    return sheet


def port(source):
    commit = subprocess.run(["git", "-C", source, "rev-parse", "HEAD"], capture_output=True, text=True,
                            check=True).stdout.strip()
    for name, entry in SETS.items():
        dmi = ss13.Dmi.load(os.path.join(source, ICONS, entry["sheet"]))
        folder = os.path.join(OUTPUT, name)
        os.makedirs(folder, exist_ok=True)
        states = []
        for side, state in (("l", entry["left"]), ("r", entry["right"])):
            cells = [_shade(cell, entry["shade"]) for cell in dmi.states[state].images[0]]
            _strip(cells, 0, FOOT_ROW).save(os.path.join(folder, "%s_leg.png" % side))
            _strip(cells, FOOT_ROW, 32).save(os.path.join(folder, "%s_foot.png" % side))
            states += ["%s_leg" % side, "%s_foot" % side]
        meta = {
            "version": 1,
            "license": "CC-BY-SA-3.0",
            "copyright": "Taken from Meridian Rift at %s/tree/%s (%s/%s), which carries the art of NovaSector, "
                         "Skyrat-tg and tgstation. Each leg is cut into a leg and a foot." % (
                             REPOSITORY, commit, ICONS, entry["sheet"]),
            "size": {"x": 32, "y": 32},
            "states": [{"name": state, "directions": 4} for state in sorted(states)],
        }
        with open(os.path.join(folder, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(meta, indent=2) + "\n")
        print("wrote", folder)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", required=True, help="Meridian Rift checkout")
    port(parser.parse_args().source)
