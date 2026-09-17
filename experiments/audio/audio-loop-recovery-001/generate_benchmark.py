#!/usr/bin/env python3
"""Generate a deterministic synthetic loop-recovery benchmark with no third-party audio."""
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
BARS_PER_LOOP = 8
TAKES = 3
SEED = 20260917
BEAT_SECONDS = 60.0 / BPM
BAR_SECONDS = BEAT_SECONDS * BEATS_PER_BAR
LOOP_SECONDS = BAR_SECONDS * BARS_PER_LOOP

# Eight-bar harmonic plan. The final bar resolves naturally back to bar 1.
ROOTS_HZ = [220.00, 246.94, 261.63, 293.66, 220.00, 329.63, 293.66, 196.00]


def _clip_i16(x: float) -> int:
    return max(-32768, min(32767, int(round(x * 32767.0))))


def _tone(t: float, freq: float, phase: float, harmonic: float) -> float:
    return math.sin(2.0 * math.pi * freq * t + phase) + harmonic * math.sin(
        2.0 * math.pi * 2.0 * freq * t + phase * 0.37
    )


def render_take(take_index: int) -> list[int]:
    """Render one musically equivalent take with independent timbre/gain/phase/noise."""
    rng = random.Random(SEED + 1000 * take_index)
    total_samples = round(LOOP_SECONDS * SAMPLE_RATE)
    gain = [0.82, 0.74, 0.88][take_index]
    harmonic = [0.20, 0.34, 0.12][take_index]
    phase = [0.0, 0.71, 1.39][take_index]
    noise_amp = [0.0015, 0.0025, 0.0010][take_index]
    out: list[int] = []

    for i in range(total_samples):
        t = i / SAMPLE_RATE
        beat_pos = t / BEAT_SECONDS
        beat_index = int(beat_pos)
        beat_phase = beat_pos - beat_index
        bar = min(BARS_PER_LOOP - 1, beat_index // BEATS_PER_BAR)
        beat_in_bar = beat_index % BEATS_PER_BAR
        root = ROOTS_HZ[bar]

        # Pad / harmonic bed: same harmonic plan, different timbre per take.
        pad_env = 0.28 + 0.10 * math.sin(math.pi * min(1.0, beat_phase))
        pad = 0.17 * _tone(t, root, phase, harmonic)
        pad += 0.07 * _tone(t, root * 1.5, phase * 0.63 + 0.3, harmonic * 0.6)
        pad *= pad_env

        # Bass accents on beats, musically aligned across takes but not sample-identical.
        bass_env = math.exp(-7.5 * beat_phase)
        bass = 0.22 * bass_env * math.sin(2.0 * math.pi * (root / 2.0) * t + phase * 0.21)

        # Percussion: kick on 1/3, snare-ish burst on 2/4.
        perc = 0.0
        if beat_in_bar in (0, 2):
            kick_freq = 55.0 - 18.0 * beat_phase
            perc += 0.32 * math.exp(-11.0 * beat_phase) * math.sin(
                2.0 * math.pi * kick_freq * t + phase * 0.11
            )
        else:
            burst = (rng.random() * 2.0 - 1.0) if beat_phase < 0.16 else 0.0
            perc += 0.11 * math.exp(-18.0 * beat_phase) * burst

        # Gentle phrase contour keeps section shape while allowing loop closure.
        phrase = 0.92 + 0.08 * math.sin(2.0 * math.pi * (t / LOOP_SECONDS))
        noise = (rng.random() * 2.0 - 1.0) * noise_amp
        sample = gain * phrase * (pad + bass + perc) + noise
        out.append(_clip_i16(sample))
    return out


def write_wav(path: Path, samples: list[int]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(SAMPLE_RATE)
        chunk = 8192
        for start in range(0, len(samples), chunk):
            block = samples[start : start + chunk]
            wf.writeframes(struct.pack("<" + "h" * len(block), *block))


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def build_manifest(take_paths: list[Path], output: Path) -> dict:
    loop_samples = round(LOOP_SECONDS * SAMPLE_RATE)
    bar_samples = round(BAR_SECONDS * SAMPLE_RATE)
    takes = []
    for i, path in enumerate(take_paths):
        takes.append(
            {
                "id": f"take-{i+1}",
                "path": path.name,
                "sha256": sha256(path),
                "sample_rate": SAMPLE_RATE,
                "samples": loop_samples,
                "bars": BARS_PER_LOOP,
                "variation": {
                    "kind": "structure-preserving-independent-render",
                    "purpose": "break exact waveform identity while preserving beat/bar/harmony plan",
                },
            }
        )

    queries = []
    # Query: end of each take must jump to bar 1 of another take.
    for src in range(TAKES):
        dst = (src + 1) % TAKES
        query_id = f"q{src+1}-end-to-next-take"
        candidates = []
        # Positive: true loop restart (bar 1, beat 1).
        candidates.append(
            {
                "id": f"{query_id}:bar1",
                "target_sample": 0,
                "target_bar": 1,
                "target_beat": 1,
                "label": "positive",
                "reason": "known loop restart",
            }
        )
        # Hard negatives: musically plausible boundaries but wrong phrase position.
        for bar in (2, 3, 5, 7):
            candidates.append(
                {
                    "id": f"{query_id}:bar{bar}",
                    "target_sample": (bar - 1) * bar_samples,
                    "target_bar": bar,
                    "target_beat": 1,
                    "label": "hard_negative",
                    "reason": "downbeat-aligned but wrong phrase position",
                }
            )
        # Off-beat negative to ensure rhythmic alignment can be tested.
        candidates.append(
            {
                "id": f"{query_id}:bar1-beat3",
                "target_sample": 2 * round(BEAT_SECONDS * SAMPLE_RATE),
                "target_bar": 1,
                "target_beat": 3,
                "label": "hard_negative",
                "reason": "same bar but wrong beat phase",
            }
        )
        queries.append(
            {
                "id": query_id,
                "source_take": f"take-{src+1}",
                "target_take": f"take-{dst+1}",
                "source_exit_sample": loop_samples,
                "source_exit_bar": BARS_PER_LOOP,
                "source_exit_beat": BEATS_PER_BAR,
                "candidates": candidates,
            }
        )

    return {
        "schema": "audio-loop-recovery-benchmark/v1",
        "generator": {
            "seed": SEED,
            "sample_rate": SAMPLE_RATE,
            "bpm": BPM,
            "beats_per_bar": BEATS_PER_BAR,
            "bars_per_loop": BARS_PER_LOOP,
            "takes": TAKES,
        },
        "license": {
            "audio": "synthetic fixture generated by repository code; no third-party audio source",
            "repository_policy": "Apache-2.0 unless otherwise noted",
        },
        "ground_truth": {
            "definition": "The known musical loop transition is end of bar 8 -> start of bar 1.",
            "waveform_identity_expected": False,
        },
        "takes": takes,
        "queries": queries,
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=Path("out"))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)

    take_paths = []
    for i in range(TAKES):
        samples = render_take(i)
        path = args.output / f"take-{i+1}.wav"
        write_wav(path, samples)
        take_paths.append(path)

    manifest = build_manifest(take_paths, args.output)
    (args.output / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"generated {len(take_paths)} takes and {len(manifest['queries'])} ranked queries")


if __name__ == "__main__":
    main()
