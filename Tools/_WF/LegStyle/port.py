"""Ports Meridian Rift's leg art into the RSIs and base sprites the leg style prototypes point at.

Run from the repository root against a Meridian Rift checkout:

    python Tools/_WF/LegStyle/port.py --source <Meridian-Rift checkout>

An SS13 leg is one sprite; here it is a leg and a foot layer, so each is cut at the row the upstream
species cut theirs. Every set is tinted to the species that wears it: its channels are scaled by how that
species' torso compares with the torso the legs were drawn for. Where a narrower torso would leave a gap
above the new legs, the species' own hip pixels fill it.

Clothing is drawn for plantigrade legs and moved by displacement maps:
- Shoes get a map per paw shape. Row by row, every stretch of digitigrade leg samples the stretch of human
  leg on that row.
- Jumpsuits keep the leg rows of the map Reptilians use. A species whose own map also reshapes the upper
  body gets a map with its rows above the hip and those leg rows below, or untouched leg rows for a
  plantigrade option.
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
TEXTURES = "Resources/Textures"
OUTPUT = "_WF/LegStyle"
PROTOTYPES = "Resources/Prototypes/_WF/LegStyle/base_sprites.yml"
# Rows from here down are the foot.
FOOT_ROW = 29
# Rows from here down wear the shoe; the ones above it, from the hip, may borrow the species' own leg.
SHOE_ROW = 25
# Rows from here down are where the legs change a jumpsuit.
HIP_ROW = 22
# The legs shoes are drawn for.
PLANTIGRADE = "Mobs/Species/Human/parts.rsi"
# The jumpsuit map made for digitigrade legs.
DIGITIGRADE_MAP = ("_White/Mobs/Species/displacement.rsi", "jumpsuit")
LEG_STATES = ("l_leg", "r_leg", "l_foot", "r_foot")
LAYERS = (("LLeg", "l_leg"), ("RLeg", "r_leg"), ("LFoot", "l_foot"), ("RFoot", "r_foot"))

# RSI -> the Meridian sheet and limb it is cut from, the leg state suffix ("_digi" for digitigrade legs),
# the body it is tinted to, the id its base sprites take and the shoe map its paws use.
SETS = {
    "digitigrade_human.rsi": ("human_parts_greyscale.dmi", "human", "_digi", "Mobs/Species/Human/parts.rsi", "HumanLegDigi", "human"),
    "digitigrade_mammal.rsi": ("mammal_parts_greyscale.dmi", "mammal", "_digi", "_DV/Mobs/Species/Vulpkanin/parts.rsi", "MammalLegDigi", "mammal"),
    "digitigrade_feroxi.rsi": ("aquatic_parts_greyscale.dmi", "aquatic", "_digi", "_DV/Mobs/Species/Feroxi/parts.rsi", "FeroxiLegDigi", "mammal"),
    "digitigrade_goblin.rsi": ("humanoid_parts_greyscale.dmi", "humanoid", "_digi", "_NF/Mobs/Species/Goblin/parts.rsi", "GoblinLegDigi", "human"),
    "digitigrade_rodentia.rsi": ("mammal_parts_greyscale.dmi", "mammal", "_digi", "_DV/Mobs/Species/Rodentia/parts.rsi", "RodentiaLegDigi", "mammal"),
    "digitigrade_shadekin.rsi": ("shadekin_parts_greyscale.dmi", "shadekin", "_digi", "_Starlight/Mobs/Species/Shadekin/parts.rsi", "ShadekinLegDigi", "shadekin"),
    "digitigrade_skrell.rsi": ("humanoid_parts_greyscale.dmi", "humanoid", "_digi", "_RMC14/Mobs/Skrells/parts.rsi", "SkrellLegDigi", "human"),
    "digitigrade_slime.rsi": ("slime_parts_greyscale.dmi", "slime", "_digi", "Mobs/Species/Slime/parts.rsi", "SlimeLegDigi", "mammal"),
    "digitigrade_tajaran.rsi": ("mammal_parts_greyscale.dmi", "mammal", "_digi", "_Goobstation/Mobs/Species/Tajaran/parts.rsi", "TajaranLegDigi", "mammal"),
    "digitigrade_thaven.rsi": ("humanoid_parts_greyscale.dmi", "humanoid", "_digi", "_Impstation/Mobs/Species/Thaven/parts.rsi", "ThavenLegDigi", "human"),
    "plantigrade_synth.rsi": ("synthliz_parts_greyscale.dmi", "synthliz", "", "_HL/Mobs/Species/Synth/parts.rsi", "SynthLegPlanti", None),
}

# Jumpsuit-like maps: state -> (map kept above the hip, whether the leg rows below are digitigrade).
SUIT_MAPS = {
    "jumpsuit_female": (("Mobs/Species/Human/displacement.rsi", "jumpsuit-female"), True),
    "jumpsuit_thaven": (("_Impstation/Mobs/Species/Thaven/displacement.rsi", "jumpsuit"), True),
    "jumpsuit_reptilian_plantigrade_female": (("_White/Mobs/Species/displacement.rsi", "jumpsuit-female"), False),
    "jumpsuit_synth_plantigrade": (("_HL/Mobs/Species/Synth/displacement.rsi", "jumpsuit"), False),
    "outerclothing_synth_plantigrade": (("_HL/Mobs/Species/Synth/displacement.rsi", "outerclothing"), False),
}


def _cells(folder, states):
    """The given states of an RSI laid over each other, one image per facing."""
    cells = [ss13.Image.new("RGBA", (32, 32)) for _ in range(4)]
    for state in states:
        sheet = ss13.Image.open(os.path.join(TEXTURES, folder, state + ".png")).convert("RGBA")
        for i, cell in enumerate(cells):
            x, y = (i % 2) * 32, (i // 2) * 32
            cell.alpha_composite(sheet.crop((x, y, x + 32, y + 32)))
    return cells


def _sheet(cells):
    sheet = ss13.Image.new("RGBA", (64, 64))
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % 2) * 32, (i // 2) * 32))
    return sheet


def _strip(cells, top, bottom):
    """The four facings as a sheet, keeping only rows top..bottom."""
    parts = []
    for cell in cells:
        part = ss13.Image.new("RGBA", cell.size)
        part.paste(cell.crop((0, top, cell.width, bottom)), (0, top))
        parts.append(part)
    return _sheet(parts)


def _means(cells):
    """The mean of each channel over the opaque pixels."""
    pixels = [cell.getpixel((x, y)) for cell in cells for y in range(cell.height) for x in range(cell.width)]
    pixels = [p for p in pixels if p[3] > 0]
    return [sum(p[c] for p in pixels) / len(pixels) for c in range(4)]


def _scale(image, gains):
    channels = [c.point(lambda v, g=g: min(255, round(v * g))) for c, g in zip(image.split(), gains)]
    return ss13.Image.merge("RGBA", channels)


def _torso(folder):
    with open(os.path.join(TEXTURES, folder, "meta.json"), encoding="utf-8-sig") as f:
        states = {state["name"] for state in json.load(f)["states"]}
    return _cells(folder, ["torso_m" if "torso_m" in states else "torso"])


def _fill_hip(cell, own, others):
    """Copies the species' own leg pixels at the hip that neither the new legs nor the torso cover."""
    for y in range(HIP_ROW, SHOE_ROW):
        for x in range(cell.width):
            pixel = own.getpixel((x, y))
            if pixel[3] > 0 and cell.getpixel((x, y))[3] == 0 and all(o.getpixel((x, y))[3] == 0 for o in others):
                cell.putpixel((x, y), pixel)


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


def _suit_map(above, below):
    """One map's rows above the hip over another's leg rows; no `below` leaves the legs as drawn."""
    out = ss13.Image.new("RGBA", above.size, (128, 128, 0, 255))
    out.paste(above.crop((0, 0, above.width, HIP_ROW)), (0, 0))
    if below is not None:
        out.paste(below.crop((0, HIP_ROW, below.width, below.height)), (0, HIP_ROW))
    return out


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
    plantigrade = _cells(PLANTIGRADE, LEG_STATES)
    maps = os.path.join(TEXTURES, OUTPUT, "displacement.rsi")
    os.makedirs(maps, exist_ok=True)
    map_states, paws = [], {}
    prototypes = ["# Base sprites for the leg styles. Art from Meridian Rift; written by Tools/_WF/LegStyle/port.py.\n"]

    for name, (sheet, limb, legs, body, sprite, shoes) in SETS.items():
        dmi = ss13.Dmi.load(os.path.join(source, ICONS, sheet))
        chest = dmi.states.get(limb + "_chest_m") or dmi.states[limb + "_chest"]
        gains = [ours / theirs for ours, theirs in zip(_means(_torso(body)), _means(chest.images[0]))]

        folder = os.path.join(TEXTURES, OUTPUT, name)
        os.makedirs(folder, exist_ok=True)
        whole = [ss13.Image.new("RGBA", (32, 32)) for _ in range(4)]
        sides = {side: [_scale(cell, gains) for cell in dmi.states["%s_%s_leg%s" % (limb, side, legs)].images[0]]
                 for side in "lr"}
        torso = _torso(body)
        for side, other in ("lr", "rl"):
            cells = sides[side]
            for i, own in enumerate(_cells(body, ["%s_leg" % side])):
                _fill_hip(cells[i], own, (sides[other][i], torso[i]))
            _strip(cells, 0, FOOT_ROW).save(os.path.join(folder, "%s_leg.png" % side))
            _strip(cells, FOOT_ROW, 32).save(os.path.join(folder, "%s_foot.png" % side))
            for image, cell in zip(whole, cells):
                image.alpha_composite(cell)
        _write_meta(folder,
                    "Taken from Meridian Rift at %s/tree/%s (%s/%s), which carries the art of NovaSector, "
                    "Skyrat-tg and tgstation. Each leg is cut into a leg and a foot and tinted to %s." % (
                        REPOSITORY, commit, ICONS, sheet, body),
                    LEG_STATES)
        for layer, state in LAYERS:
            prototypes.append("\n- type: humanoidBaseSprite\n  id: WFMob%s\n  baseSprite:\n    sprite: %s/%s\n    state: %s\n" % (
                sprite.replace("Leg", layer), OUTPUT, name, state))
        print("wrote %s, gains %s" % (folder, " ".join("%.2f" % gain for gain in gains)))

        if shoes is None:
            continue
        shape = [frozenset((x, y) for y in range(SHOE_ROW, 32) for x in range(32) if image.getpixel((x, y))[3] > 0)
                 for image in whole]
        if shoes not in paws:
            paws[shoes] = shape
            state = "shoes_" + shoes
            _sheet([_shoe_map(leg, flat) for leg, flat in zip(whole, plantigrade)]).save(
                os.path.join(maps, state + ".png"))
            map_states.append(state)
        elif paws[shoes] != shape:
            raise ValueError("%s is not shaped like the other legs that wear the %s shoe map" % (name, shoes))

    digitigrade = _cells(DIGITIGRADE_MAP[0], [DIGITIGRADE_MAP[1]])
    for state, ((folder, top), bent) in SUIT_MAPS.items():
        _sheet([_suit_map(above, below if bent else None)
                for above, below in zip(_cells(folder, [top]), digitigrade)]).save(os.path.join(maps, state + ".png"))
        map_states.append(state)
    _write_meta(maps,
                "Made for Wolfgate by Tools/_WF/LegStyle/port.py. Shoe maps come from the shape of the legs beside "
                "them. The others join a species' own map above the hip (%s) to the leg rows of Litogin's %s/%s." % (
                    ", ".join(sorted({top[0] for top, _ in SUIT_MAPS.values()})), *DIGITIGRADE_MAP),
                map_states, srgb=False)
    print("wrote", maps)

    with open(PROTOTYPES, "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(prototypes))
    print("wrote", PROTOTYPES)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", required=True, help="Meridian Rift checkout")
    port(parser.parse_args().source)
