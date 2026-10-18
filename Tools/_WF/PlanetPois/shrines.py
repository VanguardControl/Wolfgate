"""The otherworldly shrines: one per world, off the roads, found rather than driven to."""

import math

from map_author import PoiMap


def _fountain_of_life():
    """Carcinoma: a clearing of raw flesh round the Fountain of Life, walled by tendons and flesh trees."""
    size = 25
    m = PoiMap("carcinoma_fountain_of_life", size, size, "Carcinoma", "wf-poi-carcinoma-fountain-of-life",
               connection="Isolated", inhabited=False, ground="WFFloorFlesh", expeditions=False)
    c = size // 2
    for y in range(size):
        for x in range(size):
            if math.hypot(x - c, y - c) <= 5.5:
                m.tiles[x, y] = "FloorFlesh"
    # A ring of tendons with four ways in, and trees just inside the edge.
    for y in range(size):
        for x in range(size):
            r = math.hypot(x - c, y - c)
            way = abs(x - c) <= 1 or abs(y - c) <= 1
            if 9.5 <= r <= 10.5 and not way:
                m.entity("WFCarcinomaTendons", x, y)
            elif 11.4 <= r <= 12.2 and (x * 7 + y * 3) % 5 == 0 and not way:
                m.entity("WFFleshPustuleTree" if (x + y) % 3 == 0 else "WFFleshTree", x, y)
    for x, y in ((c - 4, c + 3), (c + 4, c - 3), (c + 3, c + 4), (c - 3, c - 4)):
        m.entity("WFFleshPolyp", x, y)
    m.entity("WFFountainOfLife", c, c)
    m.landmarks.append(("Fountain of Life", c, c))
    m.notes.append("Carcinoma's shrine: off the roads, three doses of vitae a round.")
    return m


def build_maps():
    return [_fountain_of_life()]
