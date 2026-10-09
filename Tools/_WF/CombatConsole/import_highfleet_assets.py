"""Extract the console sprites and PCM sound cues from a local HighFleet installation."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import wave
from PIL import Image

# Rectangles are x, y, width, height within the named HighFleet sprite; None keeps it whole.
SPRITES = {
    "panel": ("instruments_back2", None),
    "crt_bezel": ("avion_main", (0, 88, 364, 314)),
    "heading_dial": ("radar_panel_eng", (194, 188, 374, 350)),
    "warning_lamp": ("avion_main", (68, 22, 36, 38)),
    "flare_rack": ("avion_flares", None),
    "button_up": ("button_std", None),
    "button_hover": ("button_std_check", None),
    "button_down": ("button_std_on", None),
    "button_amber": ("button_orange", None),
    "button_amber_down": ("button_orange_on", None),
    "button_red": ("button_red", None),
    "button_red_down": ("button_red_on", None),
    "switch_off": ("switch_01", None),
    "switch_on": ("switch_01_on", None),
    "knob": ("switch_02_01", None),
}
SOUNDS = {"key": "switch_press.wav", "switch_on": "switch_on.wav", "switch_off": "switch_off.wav",
          "selector": "switch_metal.wav", "bearing": "switch_metal.wav", "warning": "alarm_small_07.wav"}


def extract(source, repo):
    textures = repo / "Resources/Textures/_WF/CombatConsole/HighFleet"
    sounds = repo / "Resources/Audio/_WF/CombatConsole/HighFleet"
    textures.mkdir(parents=True, exist_ok=True)
    sounds.mkdir(parents=True, exist_ok=True)
    definitions = {}
    for resource in sorted((source / "Media/Tex").glob("*.res")):
        for name, body in re.findall(r"Animation\s+(\w+)\s*\{(.*?)\}", resource.read_text(errors="replace"), re.S):
            rectangle = re.search(r"rect\s*=\s*([^\r\n]+)", body)
            if rectangle:
                definitions[name] = (resource.with_suffix(".png"), tuple(map(int, rectangle[1].split(","))))
    manifest = {"source": "HighFleet local installation", "license": "Proprietary; no redistribution grant supplied", "files": []}
    for output, (sprite, crop) in SPRITES.items():
        atlas_path, (x, y, width, height) = definitions[sprite]
        if crop:
            cx, cy, width, height = crop
            x, y = x + cx, y + cy
        destination = textures / (output + ".png")
        with Image.open(atlas_path) as atlas:
            atlas.crop((x, y, x + width, y + height)).save(destination)
        manifest["files"].append({"path": destination.relative_to(repo).as_posix(), "source": atlas_path.relative_to(source).as_posix(),
                                  "sprite": sprite, "rect": [x, y, width, height], "sha256": hashlib.sha256(destination.read_bytes()).hexdigest()})
    for output, filename in SOUNDS.items():
        original = source / "Media/Snd" / filename
        destination = sounds / (output + ".wav")
        with wave.open(str(original)) as wav:
            if wav.getcomptype() != "NONE":
                raise ValueError(f"Expected PCM wave: {original}")
        processing = {}
        if output == "bearing":
            with wave.open(str(original)) as wav:
                if wav.getsampwidth() != 2:
                    raise ValueError(f"Expected 16-bit switch recording: {original}")
                params = wav.getparams()
                frames = wav.getframerate() * 120 // 1000
                samples = list(struct.unpack(f"<{frames * params.nchannels}h", wav.readframes(frames)))
            fade_in = max(1, params.framerate // 1000)
            fade_out = max(1, params.framerate * 15 // 1000)
            for frame in range(frames):
                gain = min(1, frame / fade_in, (frames - 1 - frame) / fade_out)
                for channel in range(params.nchannels):
                    index = frame * params.nchannels + channel
                    samples[index] = round(samples[index] * gain)
            with wave.open(str(destination), "wb") as wav:
                wav.setparams(params)
                wav.writeframes(struct.pack(f"<{len(samples)}h", *samples))
            processing = {"trim_start_ms": 0, "duration_ms": 120, "fade_in_ms": 1, "fade_out_ms": 15}
        else:
            shutil.copyfile(original, destination)
        manifest["files"].append({"path": destination.relative_to(repo).as_posix(), "source": original.relative_to(source).as_posix(),
                                  **processing, "sha256": hashlib.sha256(destination.read_bytes()).hexdigest()})
    (repo / "Tools/_WF/CombatConsole/highfleet-assets.json").write_text(json.dumps(manifest, indent=2) + "\n")
    for folder, names, suffix in [(textures, SPRITES, ".png"), (sounds, SOUNDS, ".wav")]:
        attribution = "- files: [" + ", ".join('"' + name + suffix + '"' for name in names) + "]\n"
        attribution += '  license: "Proprietary"\n  copyright: "HighFleet assets from the user-provided installation. Original rights are retained by the HighFleet rights holders; no redistribution grant was supplied."\n  source: "https://store.steampowered.com/app/1434950/HighFleet/"\n'
        (folder / "attributions.yml").write_text(attribution)
    print(f"Imported {len(SPRITES)} sprites and {len(SOUNDS)} sounds.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    extract(args.source.resolve(), Path(__file__).resolve().parents[3])
