"""Converts the chosen Black Mesa sounds to Vorbis under Resources/Audio/_WF/LivingWorld/<group>/ and writes each
group's attributions.yml.

Plant sounds play in the world, so they're mono. Ambience plays globally, like the supplied planet beds, so it stays
stereo and is loudness-normalised to the same -20 LUFS, -2 dB true peak.

Run from the repository root: python Tools/_WF/LivingWorld/import_bms.py [path to the Black Mesa sound folder]
"""
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Audio/_WF/LivingWorld"
DEFAULT_SOURCE = Path.home() / "Music/bms_sounds_misc_dir/sound"

# Group folder: (stereo and normalised, source paths relative to the sound folder).
GROUPS = {
    "lash": (False, [
        "npc/tentacle/tent_strike1.wav",
        "npc/tentacle/tent_strike2.wav",
        "npc/tentacle/tent_swoosh1.wav",
        "npc/tentacle/tent_swoosh2.wav",
        "npc/tentacle/tent_swoosh3.wav",
    ]),
    "grab": (False, [
        "npc/barnacle/barnacle_tongue_pull1.wav",
        "npc/barnacle/barnacle_pull1.wav",
        "npc/barnacle/barnacle_pull2.wav",
        "npc/barnacle/barnacle_gulp1.wav",
        "npc/barnacle/barnacle_bark1.wav",
    ]),
    "spit": (False, [
        "npc/bullsquid/goo_attack1.wav",
        "npc/bullsquid/goo_attack2.wav",
        "npc/bullsquid/splat1.wav",
    ]),
    "pods": (False, [
        "xen/barrelcactus/explode01.wav",
        "xen/barrelcactus/explode02.wav",
        "xen/barrelcactus/flinch01.wav",
        "xen/barrelcactus/flinch02.wav",
        "xen/xen_moldcyst/moldcyst_pop.wav",
        "xen/xen_moldcyst/moldcyst_idle.wav",
        "xen/object/xen_podbreak.wav",
        "xen/object/xen_podbreak2.wav",
    ]),
    "heal": (False, [
        "xen/healingpool/healingpool_active01.wav",
        "xen/healingpool/healingpool_hum01.wav",
    ]),
    "light": (False, [
        "xen/lightplant/extend01.wav",
        "xen/lightplant/retract01.wav",
    ]),
    "critters": (False, [
        "xen/protozoan/chirp03.wav",
        "xen/protozoan/chirp05.wav",
        "xen/lilypad/lilypad_landing_soft01.wav",
        "xen/lilypad/lilypad_sinking01.wav",
    ]),
    "hackertree": (False, [
        "xen/hackertree/attack1.wav",
        "xen/hackertree/die1.wav",
        "xen/hackertree/retract_to_idle01.wav",
    ]),
    "vines": (False, [
        "xen/web/web_break01.wav",
        "xen/web/web_largebreak01.wav",
        "xen/object/int_vinebreak1.wav",
        "xen/object/int_vinebreak2.wav",
    ]),
    "crystal": (False, [
        "xen/xen_crystal/xen_crystal_radiation_hum01.wav",
        "xen/xen_crystal/xen_crystal_radiation_charging01.wav",
    ]),
    "gore": (False, [
        "bms_ambience/object_physics/gore_splat1.wav",
        "bms_ambience/object_physics/gore_splat2.wav",
        "bms_ambience/object_physics/gore1.wav",
        "physics/gibbing/gibbing1.wav",
    ]),
    "ambience": (True, [
        "xen/ambience/xen_outdoor1_mix.wav",
        "xen/ambience/xen_swamp1_mix.wav",
        "xen/ambience/xen_nih_wind1_mix.wav",
        "xen/ambience/wind1.wav",
        "bms_ambience/object_ambience/o_ambience_disaster_fire1.wav",
        "bms_ambience/object_ambience/o_ambience_disaster_rumble1.wav",
        "xen/soundscape/garg_xen_far01.wav",
        "xen/soundscape/boid_xen_medium01.wav",
        "xen/soundscape/towermantas_far01.wav",
        "bms_ambience/soundscape/hawk1.wav",
        "bms_ambience/soundscape/eagle1.wav",
        "xen/wind_fall.wav",
    ]),
}

LICENSE = "Custom"
SOURCE_URL = "https://www.crowbarcollective.com/games/black-mesa"


def convert(src: Path, dst: Path, ambience: bool):
    args = ["ffmpeg", "-y", "-v", "error", "-i", str(src)]
    if ambience:
        args += ["-af", "loudnorm=I=-20:TP=-2:LRA=11", "-ar", "44100", "-ac", "2"]
    else:
        args += ["-ac", "1"]
    args += ["-c:a", "libvorbis", "-q:a", "4", str(dst)]
    subprocess.run(args, check=True)


def main():
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_SOURCE
    missing = [rel for _, files in GROUPS.values() for rel in files if not (source / rel).is_file()]
    if missing:
        sys.exit("Missing source files:\n  " + "\n  ".join(missing))

    total = 0
    for group, (ambience, files) in GROUPS.items():
        folder = OUT / group
        folder.mkdir(parents=True, exist_ok=True)
        lines = []
        for rel in files:
            name = Path(rel).stem.lower() + ".ogg"
            convert(source / rel, folder / name, ambience)
            lines += [
                "- files:",
                f"  - {name}",
                f'  license: "{LICENSE}"',
                f'  copyright: "Black Mesa (Crowbar Collective) sounds, supplied by the owner as open source; ported from sound/{rel}"',
                f'  source: "{SOURCE_URL}"',
                "",
            ]
            total += 1
        (folder / "attributions.yml").write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"Wrote {total} files to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
