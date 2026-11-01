"""Builds the small WAD of sounds and music that every client ships, from the full Freedoom WAD.

Nearby players hear a game they may not have downloaded, so they need these lumps up front.

Usage: python Tools/_WF/DoomArcade/make_noise_pack.py
"""
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "Resources/GameData/_WF/DoomArcade/freedoom1.wad"
TARGET = ROOT / "Resources/Textures/_WF/DoomArcade/freedoom1-noise.wad"


def main():
    data = SOURCE.read_bytes()
    count, table = struct.unpack("<ii", data[4:12])

    lumps = []
    for i in range(count):
        position, size = struct.unpack("<ii", data[table + i * 16:table + i * 16 + 8])
        name = data[table + i * 16 + 8:table + i * 16 + 16]
        if name.startswith((b"DS", b"D_")):
            lumps.append((name, data[position:position + size]))

    body = bytearray()
    entries = bytearray()
    offset = 12
    for name, lump in lumps:
        entries += struct.pack("<ii", offset, len(lump)) + name
        body += lump
        offset += len(lump)

    TARGET.write_bytes(b"PWAD" + struct.pack("<ii", len(lumps), offset) + body + entries)
    print(f"{TARGET.name}: {len(lumps)} lumps, {TARGET.stat().st_size} bytes")


if __name__ == "__main__":
    main()
