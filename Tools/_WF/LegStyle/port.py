"""Ports Meridian Rift's digitigrade leg art into the RSIs the leg style prototypes point at.

Run from the repository root against a Meridian Rift checkout:

    python Tools/_WF/LegStyle/port.py --source <Meridian-Rift checkout>

An SS13 leg is one sprite; here it is a leg and a foot layer, so each is cut at the row the upstream
species cut theirs. `shade` scales a set's brightness to the plantigrade legs of the species that wears it.

Shoes are drawn for plantigrade feet, so each set also gets a displacement map that moves them onto its
paws: row by row, every stretch of digitigrade leg samples the stretch of human leg on that row.
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
# Rows from here down wear the shoe.
SHOE_ROW = 25
# The legs shoes are drawn for.
PLANTIGRADE = "Resources/Textures/Mobs/Species/Human/parts.rsi"

SETS = {
    "digitigrade_mammal.rsi": {
        "sheet": "mammal_parts_greyscale.dmi",
        "left": "mammal_l_leg_digi",
        "right": "mammal_r_leg_digi",
        "shade": 0.89,
        "shoes": "shoes_mammal",
    },
    "digitigrade_human.rsi": {
        "sheet": "human_parts_greyscale.dmi",
        "left": "human_l_leg_digi",
        "right": "human_r_leg_digi",
        "shade": 1.0,
        "shoes": "shoes_human",
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


def _runs(image, y):
    """The stretches of opaque pixels on a row, as (first, last) columns."""
    runs, start = [], None
    for x in range(image.width + 1):
        solid = x < image.width and image.getpixel((x, y))[3] > 0
        if solid and start is None:
            start = x
        elif not solid and start is not None:
            runs.append((start, x - 1))
            start = None
    return runs


def _plantigrade():
    """The human legs and feet, one image per facing."""
    cells = [ss13.Image.new("RGBA", (32, 32)) for _ in range(4)]
    for state in ("l_leg", "r_leg", "l_foot", "r_foot"):
        sheet = ss13.Image.open(os.path.join(PLANTIGRADE, state + ".png")).convert("RGBA")
        for i, cell in enumerate(cells):
            x, y = (i % 2) * 32, (i // 2) * 32
            cell.alpha_composite(sheet.crop((x, y, x + 32, y + 32)))
    return cells


def _shoe_map(legs, plantigrade):
    """The displacement map for one facing: red is how far right of a pixel the shoe is sampled."""
    out = ss13.Image.new("RGBA", legs.size)
    for y in range(SHOE_ROW, legs.height):
        targets, sources = _runs(legs, y), _runs(plantigrade, y)
        if not sources:
            continue
        for index, (first, last) in enumerate(targets):
            # Two legs side by side pair up; seen from the side both wear the one shoe that is drawn.
            if len(sources) == len(targets):
                left, right = sources[index]
            else:
                left, right = sources[0][0], sources[-1][1]
            length, width = last - first + 1, right - left + 1
            for k in range(length):
                if length > width:
                    column = left + k * width // length
                elif k < (length + 1) // 2:
                    # A narrower paw keeps the shoe's heel and toe and drops its middle.
                    column = left + k
                else:
                    column = right - (length - 1 - k)
                out.putpixel((first + k, y), (128 + column - (first + k), 128, 0, 255))
    return out


def _sheet(cells):
    sheet = ss13.Image.new("RGBA", (64, 64))
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % 2) * 32, (i // 2) * 32))
    return sheet


def _write_meta(folder, copyright, states, srgb=True):
    meta = {"version": 1, "license": "CC-BY-SA-3.0", "copyright": copyright, "size": {"x": 32, "y": 32}}
    if not srgb:
        meta["load"] = {"srgb": False}
    meta["states"] = [{"name": state, "directions": 4} for state in sorted(states)]
    with open(os.path.join(folder, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(meta, indent=2) + "\n")


def port(source):
    commit = subprocess.run(["git", "-C", source, "rev-parse", "HEAD"], capture_output=True, text=True,
                            check=True).stdout.strip()
    plantigrade = _plantigrade()
    maps = os.path.join(OUTPUT, "displacement.rsi")
    os.makedirs(maps, exist_ok=True)
    for name, entry in SETS.items():
        dmi = ss13.Dmi.load(os.path.join(source, ICONS, entry["sheet"]))
        folder = os.path.join(OUTPUT, name)
        os.makedirs(folder, exist_ok=True)
        states = []
        legs = [ss13.Image.new("RGBA", (32, 32)) for _ in range(4)]
        for side, state in (("l", entry["left"]), ("r", entry["right"])):
            cells = [_shade(cell, entry["shade"]) for cell in dmi.states[state].images[0]]
            _strip(cells, 0, FOOT_ROW).save(os.path.join(folder, "%s_leg.png" % side))
            _strip(cells, FOOT_ROW, 32).save(os.path.join(folder, "%s_foot.png" % side))
            states += ["%s_leg" % side, "%s_foot" % side]
            for whole, cell in zip(legs, cells):
                whole.alpha_composite(cell)
        _write_meta(folder,
                    "Taken from Meridian Rift at %s/tree/%s (%s/%s), which carries the art of NovaSector, "
                    "Skyrat-tg and tgstation. Each leg is cut into a leg and a foot." % (
                        REPOSITORY, commit, ICONS, entry["sheet"]),
                    states)
        _sheet([_shoe_map(leg, flat) for leg, flat in zip(legs, plantigrade)]).save(
            os.path.join(maps, entry["shoes"] + ".png"))
        print("wrote", folder)
    _write_meta(maps,
                "Made for Wolfgate by Tools/_WF/LegStyle/port.py from the digitigrade legs beside it and the "
                "human legs.",
                [entry["shoes"] for entry in SETS.values()], srgb=False)
    print("wrote", maps)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", required=True, help="Meridian Rift checkout")
    port(parser.parse_args().source)
