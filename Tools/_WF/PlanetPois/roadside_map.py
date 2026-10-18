"""Turn a hand-made grid map into a roadside site that faces a road on its south edge.

    python Tools/_WF/PlanetPois/roadside_map.py <saved grid.yml> <name>

Writes Resources/Maps/_WF/PlanetPois/<name>.yml and prints the table entry for it. On the way it:
- swaps indestructible walls for their ordinary versions (sites are fully destructible),
- keeps only the grid components a site uses (no shuttle, gravity, atmosphere or implicit roof),
- roofs the enclosed rooms: every tile the open ground can't reach past walls, windows and doors, and their walls,
- lays a strip of the world's road (5 wide, a sidewalk each side) along the south edge, with an entrance at each end.
The site is a Through site that turns with the road (`rotate: true`).
"""
import base64
import re
import struct
import sys
from collections import deque
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[3]
ROAD, SHOULDER, WIDTH = "RMCPlanetHybrisaAsphalt", "RMCPlanetHybrisaSidewalk", 5
KEEP = {"MetaData", "Transform", "MapGrid", "DecalGrid"}
BLOCKS = re.compile(r"^(Wall|Window|Grille|Airlock|.*Door|.*Window)")


class Loader(yaml.SafeLoader):
    pass


Loader.add_multi_constructor('!', lambda l, s, n: None)


def main():
    source, name = Path(sys.argv[1]), sys.argv[2]
    text = source.read_text(encoding="utf-8").replace("\r\n", "\n")
    data = yaml.load(text, Loader=Loader)

    text = re.sub(r"(- proto: \w+?)Indestructible\n", r"\1\n", text)

    # Tiles and where blocking entities stand.
    tilemap = {int(k): v for k, v in data["tilemap"].items()}
    tiles, chunks = {}, {}
    for group in data["entities"]:
        for ent in group["entities"]:
            for comp in ent.get("components", []):
                if comp.get("type") == "MapGrid":
                    for key, chunk in comp["chunks"].items():
                        cx, cy = map(int, key.split(","))
                        raw = base64.b64decode(chunk["tiles"])
                        chunks[key] = bytearray(raw)
                        for i in range(256):
                            tid = struct.unpack_from("<i", raw, i * 7)[0]
                            if tilemap.get(tid, "Space") != "Space":
                                tiles[cx * 16 + i % 16, cy * 16 + i // 16] = tid
    blocked = set()
    for group in data["entities"]:
        if not group["proto"] or not BLOCKS.match(group["proto"]):
            continue
        for ent in group["entities"]:
            for comp in ent.get("components", []):
                if comp.get("type") == "Transform" and comp.get("parent") == 1:
                    x, y = map(float, str(comp["pos"]).split(","))
                    blocked.add((int(x // 1), int(y // 1)))

    xs, ys = [p[0] for p in tiles], [p[1] for p in tiles]
    left, right, bottom, top = min(xs), max(xs), min(ys), max(ys)

    # Interior: what the outside can't reach past a wall, window or door.
    outside, frontier = set(), deque((x, y) for x in range(left - 1, right + 2) for y in (bottom - 1, top + 1))
    while frontier:
        p = frontier.popleft()
        if p in outside or p in blocked or not (left - 1 <= p[0] <= right + 1 and bottom - 1 <= p[1] <= top + 1):
            continue
        outside.add(p)
        frontier.extend(((p[0] + 1, p[1]), (p[0] - 1, p[1]), (p[0], p[1] + 1), (p[0], p[1] - 1)))
    roofed = {p for p in tiles if p not in outside}
    roofed |= {p for p in blocked if any((p[0] + dx, p[1] + dy) in roofed for dx in (-1, 0, 1) for dy in (-1, 0, 1))}

    # The road along the south edge.
    road_id, shoulder_id = max(tilemap) + 1, max(tilemap) + 2
    depth = WIDTH + 2
    for x in range(left, right + 1):
        for i in range(depth):
            y = bottom - 1 - i
            tiles[x, y] = shoulder_id if i in (0, depth - 1) else road_id
    for (x, y), tid in tiles.items():
        key = f"{x // 16},{y // 16}"
        chunk = chunks.setdefault(key, bytearray(struct.pack("<iBBB", 0, 0, 0, 0) * 256))
        if key not in data["entities"][0]["entities"][0]["components"][2]["chunks"] and tilemap.get(0) != "Space":
            # A new chunk's empty tiles have to name Space.
            space = next(k for k, v in tilemap.items() if v == "Space")
            for i in range(256):
                if struct.unpack_from("<i", chunk, i * 7)[0] == 0:
                    struct.pack_into("<i", chunk, i * 7, space)
        i = (y - (y // 16) * 16) * 16 + (x - (x // 16) * 16)
        struct.pack_into("<i", chunk, i * 7, tid)

    masks = {}
    for x, y in roofed:
        key = f"{x // 8},{y // 8}"
        masks[key] = masks.get(key, 0) | (1 << ((y % 8) * 8 + x % 8))

    # Rebuild the grid entity: its chunks, roof and decals, and nothing a shuttle carries.
    grid_start = text.index("- proto: \"\"\n")
    grid_end = text.index("\n- proto: ", grid_start + 1) + 1
    grid = text[grid_start:grid_end]
    decals = re.search(r"    - type: DecalGrid\n(?:      .*\n|        .*\n)*", grid)
    header = "- proto: \"\"\n  entities:\n  - uid: 1\n    components:\n    - type: MetaData\n    - type: Transform\n      parent: invalid\n    - type: MapGrid\n      chunks:\n"
    body = "".join(f"        {k}:\n          ind: {k}\n          tiles: {base64.b64encode(bytes(v)).decode()}\n          version: 7\n"
                   for k, v in sorted(chunks.items()))
    roof = "    - type: Roof\n      data:\n" + "".join(f"        {k}: {v}\n" for k, v in sorted(masks.items()))
    text = text[:grid_start] + header + body + roof + (decals.group(0) if decals else "") + text[grid_end:]

    text = re.sub(r"tilemap:\n((?:  .*\n)*)", lambda m: "tilemap:\n" + m.group(1) + f"  {road_id}: {ROAD}\n  {shoulder_id}: {SHOULDER}\n", text, count=1)
    out = ROOT / "Resources/Maps/_WF/PlanetPois" / f"{name}.yml"
    out.write_text(text, encoding="utf-8", newline="\n")

    lane = bottom - 1 - depth // 2
    print(f"""Wrote {out.relative_to(ROOT)}: {right - left + 1} x {top - bottom + 1 + depth}, {len(roofed)} roofed tiles.
  - mapPath: /Maps/_WF/PlanetPois/{name}.yml
    name: wf-poi-{name.replace('_', '-')}
    expeditions: false
    footprint: {left},{bottom - depth},{right + 1},{top + 1}
    minCount: 1
    maxCount: 1
    connection: Through
    sockets:
    - offset: {left},{lane}
      direction: West
    - offset: {right},{lane}
      direction: East
    rotate: true""")


if __name__ == "__main__":
    main()
