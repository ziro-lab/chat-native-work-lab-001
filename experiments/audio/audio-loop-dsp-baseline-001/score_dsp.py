#!/usr/bin/env python3
"""Score phase-recovery candidates using dependency-free beat-synchronous DSP features."""
from __future__ import annotations

import argparse
import json
import math
import struct
import wave
from pathlib import Path

DOWNSAMPLE = 4
MIDI_LOW = 48   # C3
MIDI_HIGH = 83  # B5
CONTEXT_BEATS = 8


def read_mono_i16(path: Path) -> tuple[int, list[float]]:
    with wave.open(str(path), "rb") as wf:
        if wf.getnchannels() != 1 or wf.getsampwidth() != 2:
            raise ValueError(f"expected mono 16-bit PCM WAV: {path}")
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    values = struct.unpack("<" + "h" * (len(raw) // 2), raw)
    return sr, [v / 32768.0 for v in values]


def goertzel_power(samples: list[float], frequency: float, sample_rate: int) -> float:
    coeff = 2.0 * math.cos(2.0 * math.pi * frequency / sample_rate)
    s1 = 0.0
    s2 = 0.0
    for x in samples:
        s0 = x + coeff * s1 - s2
        s2 = s1
        s1 = s0
    power = s1 * s1 + s2 * s2 - coeff * s1 * s2
    return max(power, 0.0)


def frequency_bank(sample_rate: int) -> list[tuple[int, float]]:
    bank = []
    nyquist = sample_rate / 2.0
    for midi in range(MIDI_LOW, MIDI_HIGH + 1):
        frequency = 440.0 * (2.0 ** ((midi - 69) / 12.0))
        if frequency < nyquist * 0.95:
            bank.append((midi % 12, frequency))
    return bank


def normalized_chroma(samples: list[float], sample_rate: int, bank: list[tuple[int, float]]) -> list[float]:
    bins = [0.0] * 12
    for pitch_class, frequency in bank:
        bins[pitch_class] += goertzel_power(samples, frequency, sample_rate)
    norm = math.sqrt(sum(x * x for x in bins))
    if norm <= 1e-20:
        return bins
    return [x / norm for x in bins]


def cosine(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(x * x for x in b))
    if na <= 1e-20 or nb <= 1e-20:
        return 0.0
    return dot / (na * nb)


def beat_features(path: Path, expected_sr: int, beat_frames: int) -> list[list[float]]:
    sr, samples = read_mono_i16(path)
    if sr != expected_sr:
        raise ValueError(f"sample-rate mismatch: {path}: {sr} != {expected_sr}")
    if expected_sr % DOWNSAMPLE != 0 or beat_frames % DOWNSAMPLE != 0:
        raise ValueError("fixture must divide cleanly by the analysis downsample factor")
    ds = samples[::DOWNSAMPLE]
    analysis_sr = expected_sr // DOWNSAMPLE
    analysis_beat = beat_frames // DOWNSAMPLE
    bank = frequency_bank(analysis_sr)
    count = len(ds) // analysis_beat
    result = []
    for beat in range(count):
        frame = ds[beat * analysis_beat : (beat + 1) * analysis_beat]
        result.append(normalized_chroma(frame, analysis_sr, bank))
    return result


def circular_slice(features: list[list[float]], start: int, count: int) -> list[list[float]]:
    n = len(features)
    return [features[(start + i) % n] for i in range(count)]


def downbeat_alignment(target_sample: int, bar_frames: int) -> float:
    return 1.0 if target_sample % bar_frames == 0 else 0.0


def seam_score(source: list[list[float]], target: list[list[float]], start_beat: int, rhythm: float) -> tuple[float, dict]:
    tonal = cosine(source[-1], target[start_beat % len(target)])
    score = 0.95 * tonal + 0.05 * rhythm
    return score, {"seam_chroma": tonal, "downbeat": rhythm}


def context_score(source: list[list[float]], target: list[list[float]], start_beat: int, rhythm: float) -> tuple[float, dict]:
    src = circular_slice(source, 0, CONTEXT_BEATS)
    tgt = circular_slice(target, start_beat, CONTEXT_BEATS)
    tonal = sum(cosine(a, b) for a, b in zip(src, tgt)) / CONTEXT_BEATS
    score = 0.95 * tonal + 0.05 * rhythm
    return score, {"context_chroma": tonal, "context_beats": CONTEXT_BEATS, "downbeat": rhythm}


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("manifest", type=Path)
    p.add_argument("--mode", choices=("seam", "context"), required=True)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()

    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    g = manifest["generator"]
    sample_rate = int(g["sample_rate"])
    beat_frames = int(g["beat_frames"])
    bar_frames = int(g["bar_frames"])
    root = args.manifest.parent

    features = {}
    for variant in manifest["variants"]:
        features[variant["id"]] = beat_features(root / variant["path"], sample_rate, beat_frames)

    output = {"schema": "audio-loop-predictions/v1", "mode": args.mode, "queries": []}
    for query in manifest["queries"]:
        src = features[query["source_variant"]]
        tgt = features[query["target_variant"]]
        scored = []
        for candidate in query["candidates"]:
            target_sample = int(candidate["target_sample"])
            if target_sample % beat_frames != 0:
                raise ValueError(f"candidate is not beat aligned: {candidate['id']}")
            start_beat = (target_sample // beat_frames) % len(tgt)
            rhythm = downbeat_alignment(target_sample, bar_frames)
            if args.mode == "seam":
                score, components = seam_score(src, tgt, start_beat, rhythm)
            else:
                score, components = context_score(src, tgt, start_beat, rhythm)
            scored.append({"id": candidate["id"], "score": score, "components": components})
        output["queries"].append({"id": query["id"], "candidates": scored})

    args.output.write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    print(f"scored {len(output['queries'])} queries in {args.mode} mode")


if __name__ == "__main__":
    main()
