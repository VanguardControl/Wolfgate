"""Converts blood splatter states from NovaSector's icons/effects/blood.dmi into an RSI.

Usage: python Tools/_WF/wolfmed/import_nova_blood.py <path to blood.dmi>
DMI sheets are frame-major (frame 0 of every direction, then frame 1, ...); RSI sheets are direction-major.
"""
import json
import os
import sys

from PIL import Image

STATES = ["hitsplatter1", "hitsplatter2", "hitsplatter3",
          "splatter1", "splatter2", "splatter3", "splatter4", "splatter5", "splatter6",
          "floor1", "floor2", "floor3", "floor4", "floor5", "floor6", "floor7"]
OUT = "Resources/Textures/_WF/Wolfmed/Effects/blood_splatter.rsi"
COMMIT = "7499f47bfc6b768a72c922be46971ebfccf794a1"


def parse(description):
    states, current = [], None
    for line in description.split("\n"):
        line = line.strip()
        if line.startswith("state ="):
            current = {"state": line.split("=", 1)[1].strip().strip('"')}
            states.append(current)
        elif current is not None and "=" in line:
            key, value = [part.strip() for part in line.split("=", 1)]
            current[key] = value
    return states


def main(path):
    source = Image.open(path)
    states = parse(source.text["Description"])  # read before convert(), which drops PNG text chunks
    sheet = source.convert("RGBA")
    size = 32
    columns = sheet.width // size
    os.makedirs(OUT, exist_ok=True)
    meta_states, index = [], 0
    for state in states:
        dirs, frames = int(state.get("dirs", 1)), int(state.get("frames", 1))
        start, index = index, index + dirs * frames
        if state["state"] not in STATES:
            continue
        out = Image.new("RGBA", (frames * size, dirs * size))
        for frame in range(frames):
            for direction in range(dirs):
                cell = start + frame * dirs + direction
                box = ((cell % columns) * size, (cell // columns) * size)
                out.paste(sheet.crop((box[0], box[1], box[0] + size, box[1] + size)), (frame * size, direction * size))
        out.save(os.path.join(OUT, state["state"] + ".png"))
        entry = {"name": state["state"]}
        if dirs > 1:
            entry["directions"] = dirs
        if frames > 1:
            delays = [float(d) / 10 for d in state.get("delay", ",".join(["1"] * frames)).split(",")]
            entry["delays"] = [delays for _ in range(dirs)]
        meta_states.append(entry)
    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Taken from NovaSector at https://github.com/NovaSector/NovaSector/blob/" + COMMIT +
                     "/icons/effects/blood.dmi (originally tgstation). Converted to RSI for Wolfgate.",
        "size": {"x": size, "y": size},
        "states": meta_states,
    }
    with open(os.path.join(OUT, "meta.json"), "w", newline="\n") as handle:
        json.dump(meta, handle, indent=4)
        handle.write("\n")
    print("wrote", len(meta_states), "states")


if __name__ == "__main__":
    main(sys.argv[1])
