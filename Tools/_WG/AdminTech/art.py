"""Converts the Admin Tech art from a Meridian Rift checkout into RSIs.

The source is modular_aphelion/modules/admin_tech/icons/*.dmi. Each RSI below takes the listed DMI states as they
are: no recolouring and no redrawing. Worn clothing that the MeridianClothing port already converts (uniforms,
jackets, visors, masks, gauntlets, the shield projector) is not repeated here.

Run from the repository root (requires Pillow):
    python Tools/_WG/AdminTech/art.py --source <Meridian-Rift checkout>
Then: python Tools/_WG/Ci/modules.py --write
"""
import argparse
import json
import os
import re
import subprocess

from PIL import Image

ICONS = "modular_aphelion/modules/admin_tech/icons"
OUT = os.path.join("Resources", "Textures", "_WG", "AdminTech")
SOURCE_URL = "https://github.com/Aphelion-Moon/Meridian-Rift"
ITEMS = ICONS + "/admin_items.dmi"
CLOTHING = ICONS + "/admin_clothing.dmi"
WORN = ICONS + "/worn_admin_clothing.dmi"
# The cytotheca wears Nova Sector's Tarkon shemagh, as it does on Meridian Rift.
TARKON_NECK = "modular_nova/modules/tarkon/icons/obj/clothing/neck.dmi"
TARKON_NECK_WORN = "modular_nova/modules/tarkon/icons/mob/clothing/neck.dmi"

# RSI name -> [(RSI state, DMI file, DMI state)]
RSIS = {
    # An over-ear headset here draws icon_alt and alt-equipped-EARS; the plain states are kept for anything else.
    "headset_bluespace": [
        ("icon", CLOTHING, "blue-headset"),
        ("icon_alt", CLOTHING, "blue-headset"),
        ("equipped-EARS", WORN, "blue-headset"),
        ("alt-equipped-EARS", WORN, "blue-headset"),
    ],
    "headset_subspace": [
        ("icon", CLOTHING, "sub-headset"),
        ("icon_alt", CLOTHING, "sub-headset"),
        ("equipped-EARS", WORN, "sub-headset"),
        ("alt-equipped-EARS", WORN, "sub-headset"),
    ],
    "magboots_bluespace": [
        ("icon", CLOTHING, "blue-magboots0"),
        ("icon-on", CLOTHING, "blue-magboots1"),
        ("equipped-FEET", WORN, "blue-magboots0"),
        ("on-equipped-FEET", WORN, "bluemagboots1"),
    ],
    "magboots_subspace": [
        ("icon", CLOTHING, "sub-magboots0"),
        ("icon-on", CLOTHING, "sub-magboots1"),
        ("equipped-FEET", WORN, "sub-magboots0"),
        ("on-equipped-FEET", WORN, "sub-magboots1"),
    ],
    "satchel": [("icon", CLOTHING, "admeme_satchel"), ("equipped-BELT", WORN, "admeme_satchel")],
    "tank": [
        ("icon", ITEMS, "sub-tank"),
        ("equipped-BELT", WORN, "sub-tank"),
        ("equipped-SUITSTORAGE", WORN, "sub-tank"),
    ],
    "contacts": [("icon", ITEMS, "contacts")],
    "pocket_bluespace": [("icon", ITEMS, "blue-pocket")],
    "pocket_subspace": [("icon", ITEMS, "sub-pocket")],
    "construction_bag_bluespace": [("icon", ITEMS, "blue-bag")],
    "construction_bag_subspace": [("icon", ITEMS, "sub-bag")],
    "multitool": [("icon", ITEMS, "sub-multitool")],
    "beaker": [("icon", ITEMS, "sub-beaker")],
    "beaker_stasis": [("icon", ITEMS, "sub-beaker-noreact")],
    "cytotheca": [("icon", TARKON_NECK, "armplate_shemaugh"), ("equipped-NECK", TARKON_NECK_WORN, "armplate_shemaugh")],
}


class Dmi:
    """A DMI sheet: its cell size and, per state, its frames as images by direction."""

    def __init__(self, path):
        self.image = Image.open(path).convert("RGBA")
        text = Image.open(path).info.get("Description", "")
        self.width = int(re.search(r"width = (\d+)", text).group(1))
        self.height = int(re.search(r"height = (\d+)", text).group(1))
        self.states = {}
        cell = 0
        for block in text.split("state = ")[1:]:
            name = re.match(r'"([^"]*)"', block).group(1)
            dirs = int(re.search(r"dirs = (\d+)", block).group(1))
            frames = int(re.search(r"frames = (\d+)", block).group(1))
            delay = re.search(r"delay = ([\d.,]+)", block)
            delays = [float(d) / 10 for d in delay.group(1).split(",")] if delay else [1.0] * frames
            # BYOND stores a state frame by frame, each frame as its directions.
            cells = [[self._cell(cell + f * dirs + d) for f in range(frames)] for d in range(dirs)]
            self.states.setdefault(name, (cells, delays))
            cell += dirs * frames

    def _cell(self, index):
        columns = self.image.width // self.width
        x, y = (index % columns) * self.width, (index // columns) * self.height
        return self.image.crop((x, y, x + self.width, y + self.height))


def write_rsi(name, states, dmis, commit):
    folder = os.path.join(OUT, name + ".rsi")
    os.makedirs(folder, exist_ok=True)
    meta_states = []
    size = None
    for out_state, dmi_name, dmi_state in states:
        dmi = dmis[dmi_name]
        cells, delays = dmi.states[dmi_state]
        size = (dmi.width, dmi.height)
        # An RSI stores a state direction by direction, each direction as its frames.
        flat = [frame for direction in cells for frame in direction]
        sheet = Image.new("RGBA", (dmi.width * len(flat), dmi.height), (0, 0, 0, 0))
        for i, frame in enumerate(flat):
            sheet.paste(frame, (i * dmi.width, 0))
        sheet.save(os.path.join(folder, out_state + ".png"))
        entry = {"name": out_state}
        if len(cells) > 1:
            entry["directions"] = len(cells)
        if len(delays) > 1:
            entry["delays"] = [delays] * len(cells)
        meta_states.append(entry)

    sources = sorted({dmi_name for _, dmi_name, _ in states})
    own = all(source.startswith(ICONS) for source in sources)
    credit = ("from its admin_tech module by moonridden, with sprites by moonridden, plejek and realwinterfrost" if own
              else "which carries the art of NovaSector, Skyrat-tg and tgstation")
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Taken from Meridian Rift at %s/tree/%s (%s), %s. Converted for Wolfgate by "
                     "Tools/_WG/AdminTech/art.py." % (SOURCE_URL, commit, ", ".join(sources), credit),
        "size": {"x": size[0], "y": size[1]},
        "states": meta_states,
    }
    with open(os.path.join(folder, "meta.json"), "w", newline="\n") as f:
        json.dump(meta, f, indent=2)
        f.write("\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--source", required=True, help="a Meridian Rift checkout")
    args = parser.parse_args()

    commit = subprocess.run(["git", "-C", args.source, "rev-parse", "HEAD"], capture_output=True, text=True,
                            check=True).stdout.strip()
    names = {dmi_name for states in RSIS.values() for _, dmi_name, _ in states}
    dmis = {name: Dmi(os.path.join(args.source, *name.split("/"))) for name in names}
    for name, states in RSIS.items():
        write_rsi(name, states, dmis, commit)
        print("wrote %s.rsi (%d states)" % (name, len(states)))


if __name__ == "__main__":
    main()
