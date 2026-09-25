"""Generate labelled placeholder RSIs for the planet cracker feature.

Output: <out>/<name>.rsi/{meta.json, <state>.png}. Every sprite is a flat charcoal
plate with a coloured border, the asset name, the state name and a frame/direction
marker, so real art can replace it state by state without touching prototypes.
"""
import json
import math
import os
import sys
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else "placeholder_rsi"
DIRS = ["south", "north", "east", "west"]

PALETTE = {
    "old": ((44, 42, 40, 255), (214, 140, 52, 255)),      # charcoal plate, faded orange border
    "new": ((30, 34, 44, 255), (72, 214, 232, 255)),      # dark blue plate, cyan border
    "ground": ((36, 30, 28, 255), (170, 72, 40, 255)),    # earth plate, ember border
    "fx": ((0, 0, 0, 0), (255, 220, 120, 255)),           # transparent, warm glow
    "ui": ((0, 0, 0, 0), (232, 226, 210, 255)),           # transparent, ivory
}


def font(px):
    for name in ("consola.ttf", "arial.ttf", "DejaVuSansMono.ttf"):
        try:
            return ImageFont.truetype(name, px)
        except Exception:
            continue
    return ImageFont.load_default()


def frame(size, style, asset, state, idx, total, direction=None, unshaded=False, arrow=None):
    w, h = size
    plate, border = PALETTE[style]
    img = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if plate[3]:
        d.rounded_rectangle((1, 1, w - 2, h - 2), radius=max(3, w // 12), fill=plate, outline=border, width=max(1, w // 32))
    # animation marker: a bar that sweeps with the frame index, or a glow ring for effects/unshaded
    if total > 1:
        t = idx / max(1, total - 1)
        if plate[3]:
            bar_w = max(2, (w - 8) // total)
            x0 = 4 + int(t * (w - 8 - bar_w))
            d.rectangle((x0, h - 6 - max(2, h // 12), x0 + bar_w, h - 6), fill=border)
        else:
            r = int((0.25 + 0.7 * t) * min(w, h) / 2)
            cx, cy = w // 2, h // 2
            a = int(255 * (1 - t) ** 0.5)
            d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=(border[0], border[1], border[2], a), width=max(1, w // 16))
    if unshaded:
        d.rectangle((3, 3, w - 4, h - 4), outline=(255, 255, 255, 160), width=1)
    # direction arrow
    if direction is not None:
        cx, cy = w // 2, h // 2
        L = max(4, min(w, h) // 5)
        vec = {"south": (0, L), "north": (0, -L), "east": (L, 0), "west": (-L, 0)}[direction]
        d.line((cx, cy, cx + vec[0], cy + vec[1]), fill=border, width=max(1, w // 24))
        d.ellipse((cx + vec[0] - 2, cy + vec[1] - 2, cx + vec[0] + 2, cy + vec[1] + 2), fill=border)
    # labels
    f_big = font(max(8, min(w, h) // 6))
    f_small = font(max(7, min(w, h) // 9))
    text_fill = border if not plate[3] else (232, 226, 210, 255)
    d.text((4, 3), asset, font=f_big, fill=text_fill)
    d.text((4, 3 + max(8, min(w, h) // 6) + 2), state, font=f_small, fill=text_fill)
    if total > 1:
        d.text((4, h - 4 - max(7, min(w, h) // 9) - (max(2, h // 12) + 4 if plate[3] else 0)), f"{idx + 1}/{total}", font=f_small, fill=text_fill)
    return img


def sheet(frames, size):
    n = len(frames)
    cols = min(n, 8)
    rows = math.ceil(n / cols)
    img = Image.new("RGBA", (cols * size[0], rows * size[1]), (0, 0, 0, 0))
    for i, fr in enumerate(frames):
        img.paste(fr, ((i % cols) * size[0], (i // cols) * size[1]))
    return img


def rsi(name, size, style, states, license_="CC-BY-SA-3.0", copyright_="Wolfgate placeholder, generated"):
    """states: list of dicts {name, frames=1, dirs=1, delay=0.1, unshaded=False}"""
    folder = os.path.join(OUT, name + ".rsi")
    os.makedirs(folder, exist_ok=True)
    meta = {"version": 1, "license": license_, "copyright": copyright_, "size": {"x": size[0], "y": size[1]}, "states": []}
    label = name.split("/")[-1]
    for st in states:
        frames_n = st.get("frames", 1)
        dirs_n = st.get("dirs", 1)
        delay = st.get("delay", 0.1)
        frames = []
        for di in range(dirs_n):
            direction = DIRS[di] if dirs_n == 4 else None
            for fi in range(frames_n):
                frames.append(frame(size, style, label, st["name"], fi, frames_n, direction, st.get("unshaded", False)))
        sheet(frames, size).save(os.path.join(folder, st["name"] + ".png"))
        entry = {"name": st["name"], "directions": dirs_n}
        if frames_n > 1:
            entry["delays"] = [[delay] * frames_n for _ in range(dirs_n)]
        meta["states"].append(entry)
    with open(os.path.join(folder, "meta.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(meta, fh, indent=2)
        fh.write("\n")
    return folder


def main():
    S32, S64, S96 = (32, 32), (64, 64), (96, 96)
    made = []
    # 1 gravity anchor 3x3
    made.append(rsi("Structures/gravity_anchor", S96, "old", [
        {"name": "deployed"}, {"name": "drilling", "frames": 8}, {"name": "locked", "frames": 4, "delay": 0.25},
        {"name": "off"}, {"name": "broken"}, {"name": "damaged", "frames": 4},
        {"name": "drilling-unshaded", "frames": 8, "unshaded": True}, {"name": "locked-unshaded", "frames": 4, "delay": 0.25, "unshaded": True}]))
    # 2 anchor crate and 3 gravity projector: dropped, both use existing art (see SPRITE_LIST.md).
    # 4 centrifuge 3x3
    made.append(rsi("Structures/centrifuge", S96, "old", [
        {"name": "off"}, {"name": "spinning", "frames": 8, "delay": 0.08}, {"name": "broken"},
        {"name": "glow-unshaded", "frames": 8, "delay": 0.08, "unshaded": True}]))
    # 5/6 console screens: dropped, both use stock computers.rsi faces.
    # 7 crack miner 2x2
    made.append(rsi("Structures/crack_miner", S64, "old", [
        {"name": "idle"}, {"name": "mining", "frames": 6}, {"name": "exhausted"}, {"name": "broken"},
        {"name": "mining-unshaded", "frames": 6, "unshaded": True}]))
    # 8 handheld surveyor and 9 its pulse: dropped, an existing scanner and the singularity lensing shader.
    # 10 deep vein decal
    made.append(rsi("Decals/deep_vein", S32, "ui", [{"name": "vein"}, {"name": "vein-rich"}]))
    # 11 fissures: dropped, the stock window crack overlays.
    # 12 crack ring
    made.append(rsi("Decals/crack_ring", S32, "ground", [
        {"name": "ring-straight-1", "dirs": 4}, {"name": "ring-straight-2", "dirs": 4}, {"name": "ring-straight-3", "dirs": 4},
        {"name": "ring-curve-1", "dirs": 4}, {"name": "ring-curve-2", "dirs": 4}, {"name": "ring-curve-3", "dirs": 4}]))
    # 13 crack hole tile + rim
    made.append(rsi("Tiles/crack_hole", S32, "ground", [{"name": "crack_hole"}]))
    made.append(rsi("Decals/crack_rim", S32, "ground", [{"name": "rim-straight", "dirs": 4}, {"name": "rim-curve", "dirs": 4}]))
    # 14 beams: dropped, both draw the _Mono ship laser beam.
    # P2
    made.append(rsi("Effects/chunk_burst", S96, "fx", [{"name": "burst", "frames": 8, "delay": 0.07}]))
    made.append(rsi("Interface/icons", (16, 16), "ui", [
        {"name": "anchor"}, {"name": "projector"}, {"name": "centrifuge"}, {"name": "chunk"}, {"name": "warning"}, {"name": "beam"}]))
    for m in made:
        print(m)


if __name__ == "__main__":
    main()
