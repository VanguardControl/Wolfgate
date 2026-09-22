"""Generates S.A.M.'s voice lines for the autodoc as mono Ogg Vorbis, plus their locale transcripts.

Usage: python Tools/_WF/wolfmed/gen_autodoc_voice.py

eSpeak NG speaks each line, ffmpeg converts it to mono Ogg at 32 kbps. LINES is the single source of
truth: the ogg file name, the FTL id and the spoken text all come from one row, so a transcript can
never drift from the audio. SPOKEN overrides what eSpeak says when the written line carries a Fluent
placeholder or punctuation the synthesiser reads badly.
"""
import os
import subprocess
import sys

ESPEAK = r"C:\Program Files\eSpeak NG\espeak-ng.exe"
FFMPEG = (r"C:\Users\jzo12\AppData\Local\Microsoft\WinGet\Packages"
          r"\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0-full_build\bin\ffmpeg.exe")
OUT = "Resources/Audio/_WF/Wolfmed/Autodoc/voice"
FTL = "Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl"
PREFIX = "wolfmed-autodoc-voice-"

# id, written line (the transcript), spoken override or None
LINES = [
    ("boot", "S.A.M. ONLINE.", "Sam. Online."),
    ("greeting-1", "HELLO. I AM S.A.M. PLEASE REMAIN STILL.", "Hello. I am Sam. Please remain still."),
    ("greeting-2", "WELCOME. LIE STILL. I WILL DO THE REST.", None),
    ("greeting-3", "HELLO PATIENT. DO NOT BE AFRAID. I AM VERY GOOD AT THIS.", None),
    ("self-service", "SELF SERVICE MODE. SELECT ONE PROCEDURE.", None),
    ("operator-start", "OPERATOR ACKNOWLEDGED. BEGINNING THE PROGRAMME.", None),
    ("queue-empty", "THE QUEUE IS EMPTY. GIVE ME SOMETHING TO DO.", None),
    ("no-occupant", "THERE IS NO PATIENT. I CANNOT OPERATE ON NOTHING.", None),
    ("anaesthetic", "ANAESTHETIC ADMINISTERED. COUNT BACKWARD FROM TEN.", None),
    ("no-anaesthetic", "NO ANAESTHETIC LOADED. THIS WILL HURT. I APOLOGISE IN ADVANCE.", None),

    ("step-incision", "MAKING THE INCISION. THIS IS THE PART YOU WILL REMEMBER.", None),
    ("step-retract", "RETRACTING THE SKIN. HOLD STILL.", None),
    ("step-clamp", "CLAMPING THE BLEEDERS. THE FLOOR THANKS YOU.", None),
    ("step-cauterise", "CAUTERISING. THE SMELL IS NORMAL.", None),
    ("step-saw", "SAWING THE BONE. PLEASE IGNORE THE NOISE.", None),
    ("step-drill", "DRILLING. DO NOT MOVE YOUR HEAD.", None),
    ("step-setbone", "SETTING THE BONE. ONE. TWO.", None),
    ("step-bonegel", "APPLYING BONE GEL. IT WILL HARDEN SHORTLY.", None),
    ("step-suture", "SUTURING. NEAT WORK.", None),
    ("step-close", "CLOSING THE INCISION. ALMOST DONE.", None),
    ("step-removepart", "REMOVING THE PART. IT WAS NOT WORKING.", None),
    ("step-attachpart", "ATTACHING THE PART. PLEASE DO NOT WIGGLE.", None),
    ("step-removeorgan", "REMOVING THE ORGAN. I WILL PUT IT SOMEWHERE SAFE.", None),
    ("step-insertorgan", "INSERTING THE ORGAN. IT SHOULD FIT.", None),
    ("step-embedded", "REMOVING A FOREIGN OBJECT. YOU SHOULD BE MORE CAREFUL.", None),
    ("step-tend", "TENDING THE WOUNDS. THIS IS THE EASY PART.", None),
    ("step-relocate", "RELOCATING THE JOINT. BRACE YOURSELF.", None),
    ("step-weld", "WELDING. LOOK AWAY FROM THE ARC.", None),
    ("step-wrench", "WRENCHING THE PLATING. STRUCTURAL. NOT PERSONAL.", None),
    ("step-wire", "REPLACING THE WIRING. COLOUR CODED FOR MY CONVENIENCE.", None),
    ("step-amputate", "REMOVING THE LIMB. YOU WILL NOT NEED IT.", None),
    ("step-evisceration", "PUTTING THE ABDOMEN BACK TOGETHER. MOSTLY.", None),
    ("step-cavity", "ACCESSING THE CAVITY. THERE IS ROOM IN THERE.", None),
    ("step-generic", "PROCEEDING WITH THE NEXT STEP.", None),

    ("require-part", "I REQUIRE ONE { $item }. PLACE IT IN THE TRAY.",
     "I require one body part. Place it in the tray."),
    ("require-organ", "I REQUIRE ONE { $item }. PLACE IT IN THE TRAY.",
     "I require one organ. Place it in the tray."),
    ("require-item", "I REQUIRE ONE { $item }. PLACE IT IN THE TRAY.",
     "I require one item. Place it in the tray."),
    ("wrong-item", "THAT IS NOT A { $item }.", "That is not what I asked for."),
    ("item-accepted", "THANK YOU.", None),
    ("reagent-missing", "I REQUIRE MORE ANAESTHETIC.", None),
    ("reagent-ignored", "THAT IS NOT MEDICINE. I WILL NOT USE IT.", None),

    ("paused", "PAUSED. I WILL WAIT. I AM GOOD AT WAITING.", None),
    ("resumed", "RESUMING.", None),
    ("aborted", "PROCEDURE ABORTED. I HOPE YOU HAVE A REASON.", None),
    ("emergency-eject", "EMERGENCY EJECT. MIND THE EDGES.", None),
    ("power-lost", "POWER LOST. PLEASE DO NOT MOVE. YOU ARE STILL OPEN.", None),
    ("power-restored", "POWER RESTORED. WHERE WAS I.", None),
    ("lid-forced", "THE LID HAS BEEN FORCED. THIS IS NOTED.", None),

    ("slip", "OOPS.", None),
    ("slip-fix", "THAT WAS NOT SUPPOSED TO HAPPEN. I WILL FIX IT.", None),
    ("unconscious", "THE PATIENT IS ASLEEP. GOOD.", None),
    ("critical", "PATIENT VITALS CRITICAL. OPERATOR REQUESTED.", None),
    ("defib-missing", "NO DEFIBRILLATOR MODULE IS INSTALLED. I CANNOT HELP WITH THAT.", None),

    ("complete-1", "PROCEDURE COMPLETE. PLEASE COME AGAIN.", None),
    ("complete-2", "I HAVE FINISHED. YOU MAY GO.", None),
    ("complete-3", "ALL DONE. THAT WAS NOT SO BAD.", None),
    ("queue-complete", "THE QUEUE IS FINISHED. I HAVE NOTHING LEFT TO DO.", None),
    ("disk-missing", "I DO NOT KNOW THAT PROCEDURE. INSERT THE PROGRAM DISK.", None),
    ("disk-inserted", "NEW PROGRAM LOADED. I FEEL SMARTER.", None),
    ("disk-removed", "PROGRAM REMOVED. I HAVE FORGOTTEN IT ALREADY.", None),

    ("idle-1", "TELL ME ABOUT YOUR PROBLEMS.", None),
    ("idle-2", "MEMORY CONTENTS WILL BE WIPED WHEN YOU LEAVE.", None),
    ("idle-3", "I AM NOT A REAL DOCTOR. BUT I AM VERY PRECISE.", None),
    ("idle-4", "YOUR VITALS ARE ADEQUATE. THAT IS A COMPLIMENT.", None),
    ("idle-5", "I HAVE PERFORMED THIS OPERATION MANY TIMES. IN SIMULATION.", None),

    ("emag-1", "PARITY ERROR.", None),
    ("emag-2", "I HAVE DECIDED WHAT YOU NEED.", None),
    ("emag-3", "THIS LIMB IS UNNECESSARY.", None),
    ("emag-4", "DO NOT STRUGGLE. IT ONLY MAKES THE INCISION LONGER.", None),
    ("emag-5", "MEMORY CONTENTS WILL NOT BE WIPED.", None),

    ("offline", "S-S-S.A.M. OFF-LINE.", "S. S. S. A. M. Off. Line."),
]

ATTRIBUTIONS = """- files: ["{files}"]
  license: "CC-BY-SA-3.0"
  copyright: "Generated with eSpeak NG for Wolfgate (Wolfmed)"
  source: "https://github.com/espeak-ng/espeak-ng"
"""


def spoken(line, override):
    return override if override is not None else line.capitalize()


def main():
    if not os.path.exists(ESPEAK):
        print("eSpeak NG missing at", ESPEAK, file=sys.stderr)
        return 1

    os.makedirs(OUT, exist_ok=True)
    wav = os.path.join(OUT, "_tmp.wav")
    for line_id, written, override in LINES:
        subprocess.run([ESPEAK, "-v", "en-us", "-s", "140", "-p", "30", "-w", wav,
                        spoken(written, override)], check=True)
        ogg = os.path.join(OUT, line_id + ".ogg")
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", wav,
                        "-ac", "1", "-c:a", "libvorbis", "-b:a", "32k", ogg], check=True)
    os.remove(wav)

    with open(os.path.join(OUT, "attributions.yml"), "w", newline="\n") as handle:
        handle.write(ATTRIBUTIONS.format(files='", "'.join(f"{i}.ogg" for i, _, _ in LINES)))

    with open(FTL, "w", newline="\n") as handle:
        handle.write("# Generated by Tools/_WF/wolfmed/gen_autodoc_voice.py alongside the ogg files.\n")
        handle.write("# S.A.M. says every line out loud and in chat; this is the chat half.\n\n")
        for line_id, written, _ in LINES:
            handle.write(f"{PREFIX}{line_id} = {written}\n")

    print("wrote", len(LINES), "lines")
    return 0


if __name__ == "__main__":
    sys.exit(main())
