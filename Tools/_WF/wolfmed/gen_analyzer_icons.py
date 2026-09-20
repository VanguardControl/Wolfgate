#!/usr/bin/env python3
"""Generates the Wolfmed analyzer_icons RSI: the pictograms the health analyzer's wounds tab draws.

Two families, one state each, all 32x32 single-direction:
  nine wound categories   cut puncture ballistic blunt burn internal infection mechanical other
  twelve part conditions  fracture bleeding internal_bleeding embedded necrosis overheating
                          scar pain impaired clotting sepsis blood_low

Every glyph is an off-white silhouette on transparent, so the client tints it per category with
Modulate. Each is drawn as an 8x supersampled alpha mask and resized down once, which keeps the
edges clean at the 18-20 px the panel actually renders them at. Strokes are deliberately fat for
the same reason.

Deterministic: no randomness, so re-running reproduces the RSI byte for byte.

Usage: python Tools/_WF/wolfmed/gen_analyzer_icons.py
Writes Resources/Textures/_WF/Wolfmed/Interface/analyzer_icons.rsi/
"""

import json
import math
import os

from PIL import Image, ImageChops, ImageDraw

FRAME = 32
SUPER = 8
INK = (235, 238, 240)

ROOT = os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
OUT_DIR = os.path.join(
    ROOT, "Resources", "Textures", "_WF", "Wolfmed", "Interface", "analyzer_icons.rsi")

# The client resolves these names through WolfmedWoundCategories.IconState and
# WolfmedAnalyzerIcons; the integration test asserts both lists against this one.
CATEGORY_STATES = [
    "cut", "puncture", "ballistic", "blunt", "burn",
    "internal", "infection", "mechanical", "other",
]
CONDITION_STATES = [
    "fracture", "bleeding", "internal_bleeding", "embedded", "necrosis", "overheating",
    "scar", "pain", "impaired", "clotting", "sepsis", "blood_low",
]


class Mask:
    """An 8x supersampled alpha mask drawn in 32x32 coordinates."""

    def __init__(self):
        self.image = Image.new("L", (FRAME * SUPER, FRAME * SUPER), 0)
        self.draw = ImageDraw.Draw(self.image)

    def _scale(self, points):
        return [(x * SUPER, y * SUPER) for x, y in points]

    def stroke(self, points, width, ink=255, caps=True):
        self.draw.line(self._scale(points), fill=ink, width=int(width * SUPER), joint="curve")
        if not caps:
            return
        for point in (points[0], points[-1]):
            self.dot(point, width / 2, ink)

    def poly(self, points, ink=255):
        self.draw.polygon(self._scale(points), fill=ink)

    def dot(self, centre, radius, ink=255):
        x, y = centre
        self.draw.ellipse(
            [(x - radius) * SUPER, (y - radius) * SUPER,
             (x + radius) * SUPER, (y + radius) * SUPER], fill=ink)

    def ring(self, centre, radius, width, ink=255):
        x, y = centre
        self.draw.ellipse(
            [(x - radius) * SUPER, (y - radius) * SUPER,
             (x + radius) * SUPER, (y + radius) * SUPER],
            outline=ink, width=int(width * SUPER))

    def box(self, rect, radius=0, ink=255):
        scaled = [rect[0] * SUPER, rect[1] * SUPER, rect[2] * SUPER, rect[3] * SUPER]
        if radius > 0:
            self.draw.rounded_rectangle(scaled, radius=radius * SUPER, fill=ink)
        else:
            self.draw.rectangle(scaled, fill=ink)

    def droplet(self, centre, radius, tip, ink=255):
        """A teardrop: a circle with a triangle pulled up to `tip`."""
        x, y = centre
        half = radius * 0.86
        self.dot(centre, radius, ink)
        self.poly([(x, tip), (x - half, y), (x + half, y)], ink)


def glyph(name):
    """Returns the finished 32x32 alpha mask for one state."""
    mask = Mask()

    if name == "cut":
        # Two parallel gashes, the shorter one offset so the pair reads as a slash and not an arrow.
        mask.stroke([(9, 27), (27, 9)], 5)
        mask.stroke([(5, 20), (13, 12)], 3.2)
    elif name == "puncture":
        # An entry hole seen head on, with the tearing around it.
        mask.ring((16, 16), 10, 3.6)
        mask.dot((16, 16), 3.4)
        for index in range(4):
            angle = math.pi / 2 * index + math.pi / 4
            mask.stroke([(16 + 12 * math.cos(angle), 16 + 12 * math.sin(angle)),
                         (16 + 15 * math.cos(angle), 16 + 15 * math.sin(angle))], 2.4, caps=False)
    elif name == "ballistic":
        # An upright round: ogive nose, cannelure, case.
        mask.poly([(16, 3), (21, 11), (21, 27), (11, 27), (11, 11)])
        mask.dot((16, 11), 5)
        mask.stroke([(11, 19), (21, 19)], 1.6, ink=0, caps=False)
    elif name == "blunt":
        # A six point impact burst.
        points = []
        for index in range(12):
            angle = math.pi * index / 6 - math.pi / 2
            radius = 13 if index % 2 == 0 else 5.5
            points.append((16 + radius * math.cos(angle), 16 + radius * math.sin(angle)))
        mask.poly(points)
    elif name == "burn":
        # Three tongues over a round body. A single tongue survives the downscale as a plain
        # teardrop and becomes indistinguishable from the bleeding drop.
        mask.dot((16, 22), 8.4)
        mask.poly([(16, 2), (20.5, 18), (11.5, 18)])
        mask.poly([(7.5, 9), (13, 22), (4, 22)])
        mask.poly([(24.5, 9), (28, 22), (19, 22)])
    elif name == "internal":
        # Something wrong inside the trunk: a body outline with a filled organ in it.
        mask.box([6, 4, 26, 29], radius=7)
        mask.box([9, 7, 23, 26], radius=5, ink=0)
        mask.dot((16, 17), 5)
    elif name == "infection":
        # A trefoil. Three lobes with the centre punched back out.
        for index in range(3):
            angle = math.pi * 2 * index / 3 - math.pi / 2
            mask.dot((16 + 8 * math.cos(angle), 16 + 8 * math.sin(angle)), 6.2)
        mask.dot((16, 16), 4.2, ink=0)
        mask.dot((16, 16), 2.4)
    elif name == "mechanical":
        mask.poly(gear(16, 16, 13.5, 9.5, 8))
        mask.dot((16, 16), 4.2, ink=0)
    elif name == "other":
        for x in (7.5, 16, 24.5):
            mask.dot((x, 16), 3.4)
    elif name == "fracture":
        # A long bone with a jagged break punched through the shaft.
        for x in (6, 26):
            mask.dot((x, 11.5), 4.4)
            mask.dot((x, 20.5), 4.4)
        mask.box([5, 13, 27, 19])
        # The break has to be wide enough to survive the downscale or the bone reads as whole.
        mask.stroke([(13, 6), (18, 12), (13, 16), (18, 20), (13, 26)], 3.6, ink=0, caps=False)
    elif name == "bleeding":
        mask.droplet((16, 20), 8.5, 3)
    elif name == "internal_bleeding":
        # The same drop, but behind a closed surface.
        mask.ring((16, 16), 14, 2.6)
        mask.droplet((16, 19), 6, 8)
    elif name == "embedded":
        # A shard driven through tissue.
        mask.box([3, 23, 29, 29], radius=2.6)
        mask.poly([(16, 2), (22, 13), (16, 27), (10, 13)])
    elif name == "necrosis":
        mask.dot((16, 14), 10)
        mask.box([11, 20, 21, 28], radius=2)
        mask.dot((12, 13), 3.2, ink=0)
        mask.dot((20, 13), 3.2, ink=0)
        mask.poly([(16, 17), (18, 21), (14, 21)], ink=0)
        for x in (14, 18):
            mask.stroke([(x, 23), (x, 28)], 1.4, ink=0, caps=False)
    elif name == "overheating":
        mask.box([11, 4, 17, 23], radius=3)
        mask.dot((14, 25), 5.2)
        mask.stroke([(22, 22), (26, 18), (22, 14), (26, 10)], 2.4)
    elif name == "scar":
        mask.stroke([(5, 16), (27, 16)], 3)
        for x in (9, 14.5, 20, 25):
            mask.stroke([(x - 2.6, 11), (x + 2.6, 21)], 2.4)
    elif name == "pain":
        mask.poly([(20, 2), (9, 17), (15, 17), (12, 30), (24, 14), (17, 14)])
    elif name == "impaired":
        mask.ring((16, 16), 13, 4)
        mask.stroke([(8.5, 23.5), (23.5, 8.5)], 4)
    elif name == "clotting":
        # A drop caught before it falls. The chevron rather than a flat bar, so it cannot be
        # mistaken for the embedded shard driven through a flat surface.
        mask.droplet((16, 13), 6, 3)
        mask.stroke([(4, 28), (16, 19), (28, 28)], 3.4)
    elif name == "sepsis":
        # The trefoil, but loose in the circulation.
        mask.ring((16, 16), 14, 2.6)
        for index in range(3):
            angle = math.pi * 2 * index / 3 - math.pi / 2
            mask.dot((16 + 5.4 * math.cos(angle), 16 + 5.4 * math.sin(angle)), 4.4)
        mask.dot((16, 16), 2.8, ink=0)
    elif name == "blood_low":
        return low_droplet()
    else:
        raise SystemExit("no glyph for " + name)

    return mask.image.resize((FRAME, FRAME), Image.LANCZOS)


def low_droplet():
    """A drop drawn as an outline with only its bottom filled: the reserve is low."""
    outer = Mask()
    outer.droplet((16, 20), 8.5, 3)
    inner = Mask()
    inner.droplet((16, 20), 6, 7.5)

    rim = ImageChops.subtract(outer.image, inner.image)
    floor = Image.new("L", outer.image.size, 0)
    ImageDraw.Draw(floor).rectangle(
        [0, 21 * SUPER, FRAME * SUPER, FRAME * SUPER], fill=255)
    combined = ImageChops.lighter(rim, ImageChops.multiply(inner.image, floor))
    return combined.resize((FRAME, FRAME), Image.LANCZOS)


def gear(cx, cy, outer, inner, teeth):
    """Polygon points for a squared-tooth cog."""
    points = []
    step = math.pi * 2 / teeth
    for index in range(teeth):
        base = step * index
        for fraction, radius in ((0.04, outer), (0.46, outer), (0.56, inner), (0.94, inner)):
            angle = base + step * fraction
            points.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle)))
    return points


def write_state(name):
    alpha = glyph(name)
    image = Image.new("RGBA", (FRAME, FRAME), INK + (0,))
    image.putalpha(alpha)
    image.save(os.path.join(OUT_DIR, name + ".png"), optimize=True)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    states = CATEGORY_STATES + CONDITION_STATES
    for name in states:
        write_state(name)

    meta = {
        "version": 1,
        "license": "CC-BY-SA-3.0",
        "copyright": "Made for Wolfgate (Wolfmed)",
        "size": {"x": FRAME, "y": FRAME},
        "states": [{"name": name} for name in states],
    }
    with open(os.path.join(OUT_DIR, "meta.json"), "w", newline="\n") as handle:
        json.dump(meta, handle, indent=2)
        handle.write("\n")
    print("wrote %d states to %s" % (len(states), OUT_DIR))


if __name__ == "__main__":
    main()
