"""Import the remaining proprietary PCM sound cues from a local HighFleet installation."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import wave

SOUNDS = {"key": "switch_press.wav", "switch_on": "switch_on.wav", "switch_off": "switch_off.wav",
          "selector": "switch_metal.wav", "bearing": "switch_metal.wav", "warning": "alarm_small_07.wav"}


def extract(source, repo):
    sounds = repo / "Resources/Audio/_WF/CombatConsole/HighFleet"
    sounds.mkdir(parents=True, exist_ok=True)
    manifest = {"source": "HighFleet local installation", "license": "Proprietary; no redistribution grant supplied", "files": []}
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
    attribution = "- files: [" + ", ".join('"' + name + '.wav"' for name in SOUNDS) + "]\n"
    attribution += '  license: "Proprietary"\n  copyright: "Custom adaptations for Wolfgate, with edits and modifications by Wolfgate contributors, derived from HighFleet assets. Original HighFleet asset rights remain with their respective rights holders; no redistribution grant was supplied."\n  source: "https://store.steampowered.com/app/1434950/HighFleet/"\n'
    (sounds / "attributions.yml").write_text(attribution)
    print(f"Imported {len(SOUNDS)} sounds. Console graphics are drawn by Wolfgate and are not imported.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    extract(args.source.resolve(), Path(__file__).resolve().parents[3])
