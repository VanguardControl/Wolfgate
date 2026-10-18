"""
Ports Meridian Rift's wall, window, door and grille sprites onto Meta Station's structures.

Each upstream RSI that Meta's walls, windows and doors use is copied and the states Meridian Rift
has art for are replaced; states it lacks (construction stages, cracks, AI overlays) stay upstream.
Every structure prototype on the map gets a `WFMeridian<Id>` child that points at the copy, and the
map and its emergency shuttle are rewritten to use those children.

    python Tools/_WF/MeridianStructures/port.py --source <Meridian-Rift checkout> --write
    python Tools/_WF/MeridianStructures/port.py --source <Meridian-Rift checkout> --review sheet.png

Art notes:
- SS13 bitmask walls and windows become SS14 corner states: each corner takes its quadrant from the
  SS13 junction with the same three neighbours.
- Airlocks compose the frame with its fill (solid) or glass overlay; lights, panels, welds and sparks
  go into a per-family effects RSI. All facings repeat the single SS13 facing.
- Opening and closing keep the upstream animation length so door timing doesn't change.
- Meridian Rift has no uranium windows; those are its plasma windows shifted to green.
- Grilles don't smooth in SS13, so every corner state is a quadrant of the one grille sprite.
"""

import argparse
import colorsys
import json
import math
import os
import re
import shutil
import sys

import yaml
from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "MeridianMarkings"))
from ss13 import Dmi  # noqa: E402

TEXTURES = "Resources/Textures"
OUT_TEXTURES = "Resources/Textures/_WF/MeridianStructures"
OUT_PROTOTYPES = "Resources/Prototypes/_WF/MeridianStructures/structures.yml"
MAPS = {
    "Resources/Maps/meta.yml": "Resources/Maps/_WF/BlackHoleEvent/meta.yml",
    "Resources/Maps/Shuttles/emergency_meta.yml": "Resources/Maps/_WF/BlackHoleEvent/emergency_meta.yml",
}
STRUCTURE = re.compile(r"Wall|Window|Airlock|Door|Shutter|Blast|Windoor|Grille", re.I)
NOT_STRUCTURE = re.compile(r"Wallmount|ClosetWall|WallWeapon|InflatableWall|DoorRemote|DoorElectronics|VendingMachineWall")

NOVA = "modular_nova/modules/aesthetics/"
AIRLOCKS = NOVA + "airlock/icons/airlocks/"
LICENSE = "CC-BY-SA-3.0"
COPYRIGHT = ("Replaced states from Meridian Rift (https://github.com/Aphelion-Moon/Meridian-Rift), "
             "NovaSector and tgstation. Other states: %s")

# Upstream airlock RSI name -> Meridian Rift airlock sheet.
AIRLOCK_SHEETS = {
    "airlock-base": AIRLOCKS + "station/public.dmi",
    "airlock-maints": AIRLOCKS + "station/maintenance.dmi",
    "command-base": AIRLOCKS + "station/command.dmi",
    "service-base": AIRLOCKS + "station/service.dmi",
    "service-freezer": AIRLOCKS + "station/freezer.dmi",
    "service-botany": AIRLOCKS + "station/botany.dmi",
    "security-base": AIRLOCKS + "station/security.dmi",
    "medical-base": AIRLOCKS + "station/medical.dmi",
    "medical-chem": AIRLOCKS + "station/medical.dmi",
    "medical-viro": AIRLOCKS + "station/virology.dmi",
    "engineering-base": AIRLOCKS + "station/engineering.dmi",
    "engineering-atmos": AIRLOCKS + "station/atmos.dmi",
    "cargo-base": AIRLOCKS + "station/mining.dmi",
    "cargo-salvage": AIRLOCKS + "station/mining.dmi",
    "cargo-mining": AIRLOCKS + "station/mining.dmi",
    "science-base": AIRLOCKS + "station/science.dmi",
    "airlock-external": AIRLOCKS + "external/external.dmi",
    "shuttle-base": "icons/obj/doors/airlocks/shuttle/shuttle.dmi",
}
# Head-of-staff doors use Nova's own sheets; maintenance hatches keep the maintenance look.
HEAD_SHEETS = [
    ("Captain", AIRLOCKS + "cap.dmi"),
    ("HeadOfPersonnel", AIRLOCKS + "hop.dmi"),
    ("HeadOfSecurity", AIRLOCKS + "hos.dmi"),
    ("ChiefEngineer", AIRLOCKS + "ce.dmi"),
    ("ChiefMedicalOfficer", AIRLOCKS + "cmo.dmi"),
    ("ResearchDirector", AIRLOCKS + "rd.dmi"),
    ("Quartermaster", AIRLOCKS + "qm.dmi"),
]
STATION_OVERLAYS = AIRLOCKS + "station/overlays.dmi"
FAMILY_OVERLAYS = {
    "external": AIRLOCKS + "external/overlays.dmi",
    "shuttle": "icons/obj/doors/airlocks/shuttle/overlays.dmi",
    "station": STATION_OVERLAYS,
}
AIRLOCK_EFFECTS = "Structures/Doors/Airlocks/Effects/airlock-effects.rsi"
EFFECT_STATES = {
    "closed_unlit": "lights_poweron",
    "open_unlit": "lights_poweron_open",
    "opening_unlit": "lights_opening",
    "closing_unlit": "lights_closing",
    "bolted_unlit": "lights_bolts",
    "deny_unlit": "lights_denied",
    "emergency_unlit": "lights_emergency",
    "panel_closed": "panel_closed",
    "panel_open": "panel_open",
    "panel_opening": "panel_opening",
    "panel_closing": "panel_closing",
    "welded": "welded",
    "sparks": "sparks",
    "sparks_broken": "sparks_broken",
    "sparks_damaged": "sparks_damaged",
    "sparks_open": "sparks_open",
}

# Smoothed upstream RSI -> {corner state base: (sheet, overlay state or None, recolour[, fixed state])}, plus
# the single-tile states taken from the isolated junction. A fixed state is a sprite that doesn't smooth: every
# corner takes its quadrant of that one sprite.
WALL = NOVA + "walls/icons/wall.dmi"
RWALL = NOVA + "walls/icons/reinforced_wall.dmi"
WINDOWS = NOVA + "windows/icons/"
SMOOTHED = {
    "Structures/Walls/solid.rsi": {
        "corners": {"solid": (WALL, None, None), "reinf_over": (RWALL, None, None)},
        "single": {"full": (WALL, None, None), "rgeneric": (RWALL, None, None)},
    },
    "Structures/Walls/solid_rust.rsi": {
        "corners": {"solid": (WALL, "rust", None), "reinf_over": (RWALL, "rust", None)},
        "single": {"full": (WALL, "rust", None), "rgeneric": (RWALL, "rust", None)},
    },
    "Structures/Walls/shuttle.rsi": {
        "corners": {"state": ("icons/turf/walls/shuttle_wall.dmi", None, None)},
        "single": {"full": ("icons/turf/walls/shuttle_wall.dmi", None, None)},
    },
    "Structures/Walls/plastitanium.rsi": {
        "corners": {"plastitanium": ("icons/turf/walls/plastitanium_wall.dmi", None, None)},
        "single": {"full": ("icons/turf/walls/plastitanium_wall.dmi", None, None)},
    },
    "Structures/Windows/window.rsi": {
        "corners": {"window": (WINDOWS + "window.dmi", None, None)},
        "single": {"full": (WINDOWS + "window.dmi", None, None)},
    },
    "Structures/Windows/reinforced_window.rsi": {
        "corners": {"rwindow": (WINDOWS + "r_window.dmi", None, None)},
        "single": {"full": (WINDOWS + "r_window.dmi", None, None)},
    },
    "Structures/Windows/tinted_window.rsi": {
        "corners": {"twindow": (WINDOWS + "r_window_tinted.dmi", None, None)},
        "single": {"full": (WINDOWS + "r_window_tinted.dmi", None, None)},
    },
    "Structures/Windows/reinforced_plasma_window.rsi": {
        "corners": {"rpwindow": (WINDOWS + "r_window_plasma.dmi", None, None)},
        "single": {"full": (WINDOWS + "r_window_plasma.dmi", None, None)},
    },
    "Structures/Windows/reinforced_uranium_window.rsi": {
        "corners": {"ruwindow": (WINDOWS + "r_window_plasma.dmi", None, "uranium")},
        "single": {"full": (WINDOWS + "r_window_plasma.dmi", None, "uranium")},
    },
    "Structures/Walls/grille-new.rsi": {
        "corners": {"grille": ("icons/obj/structures.dmi", None, None, "grille")},
        "single": {
            "full": ("icons/obj/structures.dmi", None, None, "grille"),
            "broken": ("icons/obj/structures.dmi", None, None, "brokengrille"),
        },
    },
    "Structures/Windows/shuttle_window.rsi": {
        "corners": {"swindow": ("icons/obj/smooth_structures/shuttle_window.dmi", None, None)},
        "single": {"full": ("icons/obj/smooth_structures/shuttle_window.dmi", None, None)},
    },
}

DIRECTWINDOW = NOVA + "directwindow/icons/directwindow.dmi"
# Upstream RSI -> {upstream state: (sheet, SS13 state, recolour)}.
PLAIN = {
    "Structures/Windows/directional.rsi": {
        "window": (DIRECTWINDOW, "window", None),
        "reinforced_window": (DIRECTWINDOW, "rwindow", None),
        "tinted_window": (DIRECTWINDOW, "twindow", None),
        "frosted_window": (DIRECTWINDOW, "fwindow", None),
        "plasma_window": (DIRECTWINDOW, "plasmawindow", None),
        "plasma_reinforced_window": (DIRECTWINDOW, "plasmarwindow", None),
        "uranium_window": (DIRECTWINDOW, "plasmawindow", "uranium"),
        "uranium_reinforced_window": (DIRECTWINDOW, "plasmarwindow", "uranium"),
    },
    "Structures/Doors/Shutters/shutters.rsi": {
        s: ("icons/obj/doors/shutters.dmi", s, None) for s in ("closed", "open", "opening", "closing")
    },
    "Structures/Doors/Shutters/shutters_radiation.rsi": {
        s: ("icons/obj/doors/shutters_radiation.dmi", s, None) for s in ("closed", "open", "opening", "closing")
    },
    "Structures/Doors/Shutters/shutters_window.rsi": {
        s: ("icons/obj/doors/shutters_window.dmi", s, None) for s in ("closed", "open", "opening", "closing")
    },
    "Structures/Doors/Shutters/blastdoor.rsi": {
        s: (NOVA + "blast_door/icons/blast_door.dmi", s, None) for s in ("closed", "open", "opening", "closing")
    },
    "Structures/Doors/MineralDoors/wood_door.rsi": {
        "closed": ("icons/obj/doors/mineral_doors.dmi", "wood", None),
        "open": ("icons/obj/doors/mineral_doors.dmi", "woodopen", None),
        "opening": ("icons/obj/doors/mineral_doors.dmi", "woodopening", None),
        "closing": ("icons/obj/doors/mineral_doors.dmi", "woodclosing", None),
    },
    "Structures/Doors/secret_door.rsi": {
        "closed": (WALL, "wall-0", None),
        "open": (WALL, "wall-open", None),
        "opening": (WALL, "wall-opening", None),
        "closing": (WALL, "wall-closing", None),
    },
}


def windoor_states(prefix, assembly):
    states = {
        "closed": prefix, "open": prefix + "open", "opening": prefix + "opening",
        "closing": prefix + "closing", "deny_unlit": prefix + "deny", "sparks": prefix + "spark",
        "assembly": assembly,
    }
    result = {k: (DIRECTWINDOW, v, None) for k, v in states.items()}
    # The SS13 sprites carry their own lights.
    for blank in ("closed_unlit", "open_unlit", "opening_unlit", "closing_unlit", "secure_underlay"):
        result[blank] = None
    return result


WINDOORS = {
    "windoor": windoor_states("left", "l_windoor_assembly01"),
    "windoor_secure": windoor_states("leftsecure", "l_secure_windoor_assembly01"),
}

# Upstream animations whose length the replacement keeps.
TIMED = re.compile(r"^(opening|closing)(_unlit)?$|^panel_(opening|closing)$")


def blank():
    return Image.new("RGBA", (32, 32))


def recolour(image, how):
    if how != "uranium":
        return image
    pixels = image.load()
    out = image.copy()
    target = out.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            if not a:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            r2, g2, b2 = colorsys.hsv_to_rgb((h + 0.45) % 1.0, s, v)
            target[x, y] = (round(r2 * 255), round(g2 * 255), round(b2 * 255), a)
    return out


class Rsi:
    """An upstream RSI copied into memory; states are [dir][frame] images with per-dir delays."""

    def __init__(self, path):
        self.path = path
        with open(os.path.join(path, "meta.json"), encoding="utf-8-sig") as f:
            self.meta = json.load(f)
        self.states = {}
        for state in self.meta["states"]:
            self.states[state["name"]] = None  # loaded lazily from the upstream png

    def load_state(self, name):
        entry = next(s for s in self.meta["states"] if s["name"] == name)
        dirs = entry.get("directions", 1)
        delays = entry.get("delays") or [[1.0]] * dirs
        frames = len(delays[0])
        sheet = Image.open(os.path.join(self.path, name + ".png")).convert("RGBA")
        columns = sheet.width // 32
        images, index = [], 0
        for _d in range(dirs):
            row = []
            for _f in range(frames):
                x, y = (index % columns) * 32, (index // columns) * 32
                row.append(sheet.crop((x, y, x + 32, y + 32)))
                index += 1
            images.append(row)
        return images, delays

    def entry(self, name):
        return next((s for s in self.meta["states"] if s["name"] == name), None)


def write_state(folder, name, images, delays):
    """images: [dir][frame]."""
    count = sum(len(row) for row in images)
    columns = math.ceil(math.sqrt(count))
    rows = math.ceil(count / columns)
    sheet = Image.new("RGBA", (columns * 32, rows * 32))
    index = 0
    for row in images:
        for image in row:
            sheet.paste(image, ((index % columns) * 32, (index // columns) * 32))
            index += 1
    sheet.save(os.path.join(folder, name + ".png"), optimize=True)
    entry = {"name": name}
    if len(images) > 1:
        entry["directions"] = len(images)
    if len(images[0]) > 1:
        entry["delays"] = [[round(d, 3) for d in row] for row in delays]
    return entry


def seconds(state, frames):
    if not state.delays:
        return [0.1] * frames
    return [max(d, 0.1) / 10 for d in state.delays[:frames]] + [0.1] * (frames - len(state.delays))


def from_dmi(source, sheet, name, dirs, how=None, under=None):
    """An SS13 state as [dir][frame], repeating the single facing when the target has four."""
    state = Dmi.load(os.path.join(source, sheet)).states[name]
    frames = state.frames
    images = []
    for d in range(dirs):
        row = []
        for f in range(frames):
            image = state.images[f][min(d, state.dirs - 1) if state.dirs > 1 else 0].copy()
            if under is not None:
                base = under(f, d)
                base.alpha_composite(image)
                image = base
            row.append(recolour(image, how))
        images.append(row)
    return images, [seconds(state, frames)] * dirs


def overlay(source, sheet, name, images, delays):
    """Draws an SS13 state over each frame (frames clamp at the shorter sequence)."""
    state = Dmi.load(os.path.join(source, sheet)).states.get(name)
    if state is None:
        return images, delays
    for d, row in enumerate(images):
        for f, image in enumerate(row):
            image.alpha_composite(state.images[min(f, state.frames - 1)][0])
    return images, delays


def fit(delays, upstream_delays):
    """Scales delays to the upstream animation length."""
    total = sum(upstream_delays[0])
    own = sum(delays[0])
    if own <= 0 or total <= 0:
        return delays
    return [[d * total / own for d in row] for row in delays]


# --------------------------------------------------------------------------------- smoothing

QUADRANTS = {"NE": (16, 0, 32, 16), "SE": (16, 16, 32, 32), "SW": (0, 16, 16, 32), "NW": (0, 0, 16, 16)}
# Corner -> (counter-clockwise bit, diagonal bit, clockwise bit) in SS13 junction flags.
CORNER_BITS = {"NE": (1, 16, 4), "SE": (4, 32, 2), "SW": (2, 64, 8), "NW": (8, 128, 1)}
# RSI facing order (S, N, E, W) holds the SE, NW, NE and SW corners.
CORNER_DIRS = ["SE", "NW", "NE", "SW"]


def junction_prefix(dmi):
    for state in dmi.all_states:
        if state.name.endswith("-0"):
            return state.name[:-2]
    raise ValueError("%s has no junction states" % dmi.path)


def junction(source, sheet, bits, rust, how, fixed=None):
    dmi = Dmi.load(os.path.join(source, sheet))
    if fixed is not None:
        state = dmi.states[fixed]
    else:
        prefix = junction_prefix(dmi)
        state = dmi.states.get("%s-%d" % (prefix, bits))
        if state is None:
            state = dmi.states["%s-%d" % (prefix, bits & 15)]
    image = state.images[0][0].copy()
    if rust:
        image.alpha_composite(dmi.states[rust].images[0][0])
    return recolour(image, how)


def corner_state(source, sheet, fill, rust, how, fixed=None):
    frames = []
    for corner in CORNER_DIRS:
        ccw, diag, cw = CORNER_BITS[corner]
        bits = (ccw if fill & 1 else 0) | (cw if fill & 4 else 0)
        if fill & 2 and fill & 1 and fill & 4:
            bits |= diag
        image = junction(source, sheet, bits, rust, how, fixed)
        box = QUADRANTS[corner]
        frame = blank()
        frame.paste(image.crop(box), box[:2])
        frames.append([frame])
    return frames, [[1.0]] * 4


# --------------------------------------------------------------------------------- builds


class Build:
    """One output RSI: an upstream copy with some states replaced."""

    def __init__(self, name, upstream):
        self.name = name
        self.upstream = upstream
        self.replaced = {}  # state -> (images, delays) or None for transparent

    def write(self, root):
        src = Rsi(os.path.join(root, TEXTURES, self.upstream))
        out = os.path.join(root, OUT_TEXTURES, self.name + ".rsi")
        os.makedirs(out, exist_ok=True)
        states = []
        names = [s["name"] for s in src.meta["states"]] + [n for n in self.replaced if not src.entry(n)]
        for name in names:
            if name not in self.replaced:
                shutil.copyfile(os.path.join(src.path, name + ".png"), os.path.join(out, name + ".png"))
                states.append(src.entry(name))
                continue
            value = self.replaced[name]
            entry = src.entry(name)
            dirs = entry.get("directions", 1) if entry else 1
            if value is None:
                value = ([[blank()] for _ in range(dirs)], [[1.0]] * dirs)
            images, delays = value
            if entry and entry.get("delays") and TIMED.match(name) and len(images[0]) > 1:
                delays = fit(delays, entry["delays"])
            states.append(write_state(out, name, images, delays))
        meta = {
            "version": 1,
            "license": LICENSE,
            "copyright": COPYRIGHT % src.meta.get("copyright", "upstream"),
            "size": {"x": 32, "y": 32},
            "states": states,
        }
        with open(os.path.join(out, "meta.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(meta, f, indent=2)
            f.write("\n")
        return out


def dirs_of(root, upstream, state):
    entry = Rsi(os.path.join(root, TEXTURES, upstream)).entry(state)
    return entry.get("directions", 1) if entry else 1


def smoothed_build(root, source, name, upstream):
    spec = SMOOTHED[upstream]
    build = Build(name, upstream)
    for base, args in spec["corners"].items():
        for fill in range(8):
            build.replaced["%s%d" % (base, fill)] = corner_state(source, *args[:1], fill, *args[1:])
    for state, args in spec["single"].items():
        build.replaced[state] = ([[junction(source, args[0], 0, *args[1:])]], [[1.0]])
    return build


def plain_build(root, source, name, upstream, table):
    build = Build(name, upstream)
    for state, value in table.items():
        if value is None:
            build.replaced[state] = None
            continue
        sheet, ss13, how = value
        build.replaced[state] = from_dmi(source, sheet, ss13, dirs_of(root, upstream, state), how)
    return build


def airlock_family(sheet):
    if "/external/" in sheet:
        return "external"
    if "/shuttle/" in sheet:
        return "shuttle"
    return "station"


def airlock_build(root, source, name, upstream, sheet, glass):
    build = Build(name, upstream)
    family = airlock_family(sheet)
    overlays = FAMILY_OVERLAYS[family]
    glass_sheet = overlays if "glass_closed" in Dmi.load(os.path.join(source, overlays)).states else STATION_OVERLAYS
    for state, ss13 in (("closed", "closed"), ("open", "open"), ("opening", "opening"),
                        ("closing", "closing"), ("assembly", "construction")):
        dirs = dirs_of(root, upstream, state)
        images, delays = from_dmi(source, sheet, ss13, dirs)
        if glass:
            images, delays = overlay(source, glass_sheet, "glass_" + ss13, images, delays)
        else:
            images, delays = overlay(source, sheet, "fill_" + ss13, images, delays)
        build.replaced[state] = (images, delays)
    return build


def effects_build(root, source, family):
    build = Build("airlock_effects_" + family, AIRLOCK_EFFECTS)
    dmi = Dmi.load(os.path.join(source, FAMILY_OVERLAYS[family]))
    station = Dmi.load(os.path.join(source, STATION_OVERLAYS))
    for state, ss13 in EFFECT_STATES.items():
        dirs = dirs_of(root, AIRLOCK_EFFECTS, state)
        if ss13 in dmi.states:
            build.replaced[state] = from_dmi(source, FAMILY_OVERLAYS[family], ss13, dirs)
        elif family != "shuttle" and ss13 in station.states:
            build.replaced[state] = from_dmi(source, STATION_OVERLAYS, ss13, dirs)
        else:
            build.replaced[state] = None
    return build


def diagonal_build(root, source, name, upstream, sheet):
    """Matches each upstream diagonal facing to the SS13 diagonal junction with the closest outline."""
    build = Build(name, upstream)
    dmi = Dmi.load(os.path.join(source, sheet))
    candidates = [s for s in dmi.all_states if s.name.endswith("-diagonal")]
    src = Rsi(os.path.join(root, TEXTURES, upstream))
    for entry in src.meta["states"]:
        images, delays = src.load_state(entry["name"])
        out = []
        for row in images:
            mask = row[0].getchannel("A").point(lambda a: 255 if a else 0).tobytes()

            def score(state):
                other = state.images[0][0].getchannel("A").point(lambda a: 255 if a else 0).tobytes()
                return sum(1 for a, b in zip(mask, other) if a == b)

            best = max(candidates, key=score)
            out.append([best.images[0][0].copy()])
        build.replaced[entry["name"]] = (out, [[1.0]] * len(out))
    return build


# --------------------------------------------------------------------------------- prototypes


class Loader(yaml.SafeLoader):
    pass


def _tagged(loader, suffix, node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)


Loader.add_multi_constructor("!", _tagged)


def load_entities(root):
    entities = {}
    for folder, _, files in os.walk(os.path.join(root, "Resources/Prototypes")):
        for file in files:
            if not file.endswith(".yml"):
                continue
            path = os.path.join(folder, file)
            if os.path.normpath(path) == os.path.normpath(os.path.join(root, OUT_PROTOTYPES)):
                continue
            try:
                with open(path, encoding="utf-8") as f:
                    docs = yaml.load(f, Loader=Loader)
            except yaml.YAMLError:
                continue
            for doc in docs if isinstance(docs, list) else []:
                if isinstance(doc, dict) and doc.get("type") == "entity" and "id" in doc:
                    entities[doc["id"]] = doc
    return entities


def parents(entity):
    parent = entity.get("parent")
    return [] if parent is None else parent if isinstance(parent, list) else [parent]


def field(entities, id_, component, key):
    entity = entities.get(id_)
    if entity is None:
        return None
    for comp in entity.get("components") or []:
        if isinstance(comp, dict) and comp.get("type") == component and key in comp:
            return comp[key]
    for parent in parents(entity):
        value = field(entities, parent, component, key)
        if value is not None:
            return value
    return None


def entity_field(entities, id_, key):
    entity = entities.get(id_)
    if entity is None:
        return None
    if key in entity:
        return entity[key]
    for parent in parents(entity):
        value = entity_field(entities, parent, key)
        if value is not None:
            return value
    return None


def inherits(entities, id_, ancestor):
    entity = entities.get(id_)
    if entity is None:
        return False
    return id_ == ancestor or any(inherits(entities, p, ancestor) for p in parents(entity))


def map_protos(root):
    found = []
    for path in MAPS:
        with open(os.path.join(root, path), encoding="utf-8") as f:
            for line in f:
                m = re.match(r"^- proto: (\S+)", line)
                if m and STRUCTURE.search(m.group(1)) and not NOT_STRUCTURE.search(m.group(1)):
                    found.append(m.group(1))
    return sorted(set(found))


def plan(root, source):
    """Returns (builds by name, {proto: (rsi name, effects family or None)}, skipped protos)."""
    entities = load_entities(root)
    builds, assignment, skipped = {}, {}, []

    def ensure(name, make):
        if name not in builds:
            builds[name] = make()
        return name

    for proto in map_protos(root):
        upstream = field(entities, proto, "Sprite", "sprite")
        if upstream is None:
            skipped.append((proto, "no sprite"))
            continue
        stem = os.path.basename(upstream)[:-4]
        family = None
        if upstream in SMOOTHED:
            name = ensure(stem, lambda: smoothed_build(root, source, stem, upstream))
        elif upstream in PLAIN:
            name = ensure(stem, lambda: plain_build(root, source, stem, upstream, PLAIN[upstream]))
        elif upstream.startswith("Structures/Doors/Windoors/"):
            kind = "windoor_secure" if inherits(entities, proto, "BaseSecureWindoor") else "windoor"
            name = ensure(kind + "_" + stem, lambda: plain_build(root, source, kind + "_" + stem, upstream, WINDOORS[kind]))
        elif upstream == "Structures/Walls/shuttle_diagonal.rsi":
            name = ensure(stem, lambda: diagonal_build(root, source, stem, upstream, "icons/turf/walls/shuttle_wall.dmi"))
        elif upstream.startswith("Structures/Doors/Airlocks/") and stem in AIRLOCK_SHEETS:
            sheet = AIRLOCK_SHEETS[stem]
            if "Maint" not in proto:
                sheet = next((s for key, s in HEAD_SHEETS if key in proto), sheet)
            glass = "/Glass/" in upstream
            label = os.path.basename(sheet)[:-4]
            name = "airlock_%s%s" % (label, "_glass" if glass else "")
            name = ensure(name, lambda: airlock_build(root, source, name, upstream, sheet, glass))
            family = airlock_family(sheet)
            ensure("airlock_effects_" + family, lambda: effects_build(root, source, family))
        else:
            skipped.append((proto, upstream))
            continue
        assignment[proto] = (name, family)
    return entities, builds, assignment, skipped


def rsi_path(name):
    return OUT_TEXTURES[len(TEXTURES) + 1:] + "/" + name + ".rsi"


def prototype_yaml(entities, assignment):
    lines = [
        "# Generated by Tools/_WF/MeridianStructures/port.py. Don't edit by hand.",
        "",
    ]
    for proto, (name, family) in sorted(assignment.items()):
        suffix = entity_field(entities, proto, "suffix")
        sprite = rsi_path(name)
        lines += [
            "- type: entity",
            "  parent: %s" % proto,
            "  id: WFMeridian%s" % proto,
            "  suffix: %s" % json.dumps("Meridian" + (", " + str(suffix) if suffix else "")),
            "  components:",
            "  - type: Sprite",
            "    sprite: %s" % sprite,
        ]
        layers = field(entities, proto, "Sprite", "layers")
        if family and layers:
            remapped = []
            for layer in layers:
                layer = dict(layer)
                if layer.get("sprite") == AIRLOCK_EFFECTS:
                    layer["sprite"] = rsi_path("airlock_effects_" + family)
                remapped.append(layer)
            dumped = yaml.safe_dump(remapped, default_flow_style=None, sort_keys=False, width=200)
            lines.append("    layers:")
            lines += ["    " + l for l in dumped.rstrip("\n").split("\n")]
        if field(entities, proto, "Icon", "sprite") is not None:
            lines += ["  - type: Icon", "    sprite: %s" % sprite]
        lines.append("")
    return "\n".join(lines)


def rewrite_maps(root, assignment):
    for src, dst in MAPS.items():
        with open(os.path.join(root, src), encoding="utf-8", newline="") as f:
            text = f.read()

        def swap(m):
            proto = m.group(2)
            return m.group(1) + ("WFMeridian" + proto if proto in assignment else proto)

        text = re.sub(r"^(- proto: )(\S+)", swap, text, flags=re.M)
        os.makedirs(os.path.dirname(os.path.join(root, dst)), exist_ok=True)
        with open(os.path.join(root, dst), "w", encoding="utf-8", newline="") as f:
            f.write(text)


def review(root, builds, path):
    """One row per output RSI: the upstream and the replacement for each replaced state's first facing."""
    rows = []
    for name, build in sorted(builds.items()):
        src = Rsi(os.path.join(root, TEXTURES, build.upstream))
        cells = []
        for state, value in build.replaced.items():
            if value is None or not src.entry(state):
                continue
            before = src.load_state(state)[0][0][0]
            after = value[0][0][0]
            cells.append((before, after))
        rows.append((name, cells[:24]))
    width = max(len(c) for _, c in rows) * 70 + 10
    sheet = Image.new("RGBA", (width, len(rows) * 40), (40, 40, 48, 255))
    for r, (_name, cells) in enumerate(rows):
        for c, (before, after) in enumerate(cells):
            sheet.alpha_composite(before, (c * 70, r * 40))
            sheet.alpha_composite(after, (c * 70 + 34, r * 40))
    sheet = sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST)
    sheet.save(path)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--root", default=".")
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--review")
    args = parser.parse_args()

    entities, builds, assignment, skipped = plan(args.root, args.source)
    print("%d prototypes, %d RSIs" % (len(assignment), len(builds)))
    for proto, why in skipped:
        print("skipped %s (%s)" % (proto, why))
    if args.review:
        review(args.root, builds, args.review)
    if not args.write:
        return
    out = os.path.join(args.root, OUT_TEXTURES)
    if os.path.isdir(out):
        shutil.rmtree(out)
    for build in builds.values():
        build.write(args.root)
    os.makedirs(os.path.dirname(os.path.join(args.root, OUT_PROTOTYPES)), exist_ok=True)
    with open(os.path.join(args.root, OUT_PROTOTYPES), "w", encoding="utf-8", newline="\n") as f:
        f.write(prototype_yaml(entities, assignment))
    rewrite_maps(args.root, assignment)


if __name__ == "__main__":
    main()
