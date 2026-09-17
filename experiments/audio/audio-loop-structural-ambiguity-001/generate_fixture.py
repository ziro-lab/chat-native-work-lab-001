#!/usr/bin/env python3
"""Generate a 24-bar synthetic song with nested 2/4-bar structural decoys."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import struct
import wave
from pathlib import Path

SAMPLE_RATE = 16000
BPM = 120.0
BEATS_PER_BAR = 4
BAR_SECONDS = (60.0 / BPM) * BEATS_PER_BAR
BAR_FRAMES = round(BAR_SECONDS * SAMPLE_RATE)
BARS = 24
SEED = 20260917

# True start shares 2 bars with the bar-9 decoy and 4 bars with the bar-17 decoy.
STRUCTURE = [
    "A", "B", "C", "D", "E", "F", "G", "H",
    "A", "B", "X", "Y", "I", "J", "K", "L",
    "A", "B", "C", "D", "M", "N", "O", "P",
]

# Pitch-class identities. Repeated symbols are intentionally identical musical material.
ROOT_MIDI = {
    "A": 48, "B": 50, "C": 52, "D": 53,
    "E": 55, "F": 57, "G": 59, "H": 60,
    "X": 56, "Y": 51, "I": 58, "J": 54,
    "K": 49, "L": 56, "M": 51, "N": 58,
    "O": 54, "P": 49,
}

ROTATIONS = [0, 2, 5, 8, 11, 15]


def midi_hz(m: int) -> float:
    return 440.0 * (2.0 ** ((m - 69) / 12.0))


def clip_i16(x: float) -> int:
    return max(-32768, min(32767, int(round(x * 32767.0))))


def profile(index: int) -> dict:
    gains = [0.78, 0.84, 0.72, 0.88, 0.76, 0.82, 0.74, 0.86, 0.80]
    phases = [0.07, 0.63, 1.17, 0.39, 1.51, 0.91, 1.93, 1.31, 2.27]
    harmonics = [0.15, 0.27, 0.20, 0.11, 0.31, 0.18, 0.24, 0.13, 0.29]
    noises = [0.0010, 0.0018, 0.0013, 0.0020, 0.0011, 0.0016, 0.0014, 0.0019, 0.0012]
    return {"gain": gains[index], "phase": phases[index], "harmonic": harmonics[index], "noise": noises[index]}


def render_song(order: list[str], take_index: int) -> list[int]:
    p = profile(take_index)
    rng = random.Random(SEED + 10000 * take_index)
    total = BARS * BAR_FRAMES
    beat_frames = BAR_FRAMES // BEATS_PER_BAR
    out: list[int] = []

    for i in range(total):
        bar = min(BARS - 1, i // BAR_FRAMES)
        within_bar = i - bar * BAR_FRAMES
        beat = min(BEATS_PER_BAR - 1, within_bar // beat_frames)
        within_beat = within_bar - beat * beat_frames
        beat_phase = within_beat / beat_frames
        t = i / SAMPLE_RATE
        symbol = order[bar]
        root_midi = ROOT_MIDI[symbol]
        root = midi_hz(root_midi)
        third = midi_hz(root_midi + (3 if symbol in {"B", "D", "F", "Y", "J", "N", "P"} else 4))
        fifth = midi_hz(root_midi + 7)

        # Sustained harmonic bed: repeated symbols have the same pitch-class identity.
        bed = (
            math.sin(2 * math.pi * root * t + p["phase"])
            + 0.62 * math.sin(2 * math.pi * third * t + p["phase"] * 0.61)
            + 0.46 * math.sin(2 * math.pi * fifth * t + p["phase"] * 0.37)
            + p["harmonic"] * math.sin(2 * math.pi * 2 * root * t + p["phase"] * 0.19)
        ) * 0.105

        # Beat-synchronous bass and percussion keep Beat/Downbeat detection strong.
        env = math.exp(-7.0 * beat_phase)
        bass = 0.16 * env * math.sin(2 * math.pi * (root / 2.0) * t + p["phase"] * 0.13)
        if beat == 0:
            kick = 0.31 * math.exp(-11.0 * beat_phase) * math.sin(2 * math.pi * (58.0 - 16.0 * beat_phase) * t)
        elif beat == 2:
            kick = 0.22 * math.exp(-10.0 * beat_phase) * math.sin(2 * math.pi * 64.0 * t)
        else:
            kick = 0.0
        snare = 0.0
        if beat in (1, 3) and beat_phase < 0.14:
            snare = 0.085 * math.exp(-17.0 * beat_phase) * (rng.random() * 2.0 - 1.0)

        # Small phrase contour repeats with the 8-bar local phrase, not the full 24 bars.
        local_phrase = ((bar % 8) + within_bar / BAR_FRAMES) / 8.0
        contour = 0.94 + 0.06 * math.sin(2 * math.pi * local_phrase)
        noise = p["noise"] * (rng.random() * 2.0 - 1.0)
        sample = p["gain"] * contour * (bed + bass + kick + snare) + noise
        out.append(clip_i16(sample))
    return out


def write_wav(path: Path, samples: list[int]) -> None:
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        for start in range(0, len(samples), 8192):
            block = samples[start:start + 8192]
            wf.writeframes(struct.pack("<" + "h" * len(block), *block))


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def physical_bar(canonical_bar: int, rotation: int) -> int:
    # rotation means the physical file starts at canonical bar rotation+1.
    return ((canonical_bar - 1 - rotation) % BARS) + 1


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--output", type=Path, default=Path("out/fixture"))
    args = ap.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=True)

    sources = []
    for i in range(3):
        path = out / f"source-{i+1}.wav"
        write_wav(path, render_song(STRUCTURE, i))
        sources.append({"id": f"source-{i+1}", "path": path.name, "sha256": sha256(path), "take_profile": i})

    targets = []
    for j, rot in enumerate(ROTATIONS):
        order = STRUCTURE[rot:] + STRUCTURE[:rot]
        take_index = j + 3
        path = out / f"target-{j+1}.wav"
        write_wav(path, render_song(order, take_index))
        targets.append(
            {
                "id": f"target-{j+1}",
                "path": path.name,
                "sha256": sha256(path),
                "take_profile": take_index,
                "rotation_bars": rot,
                "hidden_positive_physical_bar": physical_bar(1, rot),
                "hidden_decoy_2bar_physical_bar": physical_bar(9, rot),
                "hidden_decoy_4bar_physical_bar": physical_bar(17, rot),
            }
        )

    queries = []
    for source in sources:
        for target in targets:
            queries.append({"id": f"{source['id']}-to-{target['id']}", "source": source["id"], "target": target["id"]})

    manifest = {
        "schema": "audio-loop-structural-ambiguity/v1",
        "generator": {
            "sample_rate": SAMPLE_RATE,
            "bpm": BPM,
            "beats_per_bar": BEATS_PER_BAR,
            "bar_seconds": BAR_SECONDS,
            "bars": BARS,
            "structure": STRUCTURE,
            "seed": SEED,
        },
        "sources": sources,
        "targets": targets,
        "queries": queries,
        "ground_truth": {
            "positive_canonical_bar": 1,
            "decoy_2bar_canonical_bar": 9,
            "decoy_4bar_canonical_bar": 17,
            "tolerance_seconds": 0.070,
        },
    }
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"generated {len(sources)} sources, {len(targets)} targets, {len(queries)} queries")


if __name__ == "__main__":
    main()
