#!/usr/bin/env python3
"""Run Beat This + chroma-context ranking on independently transformed loop variants."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import statistics
import struct
import subprocess
import sys
import wave
from pathlib import Path

import torch
from beat_this.inference import File2Beats

ROOT = Path(__file__).resolve().parent
TRANSFORMER = ROOT / "transform_fixture.py"
EXPECTED_SMALL0_SHA256 = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"
TOLERANCE = 0.070
DOWNSAMPLE = 4
MIDI_LOW = 48
MIDI_HIGH = 83
CONTEXT_BEATS = 8


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def locate_checkpoint() -> Path:
    root = Path(torch.hub.get_dir()) / "checkpoints"
    found = sorted(root.glob("*small0*.ckpt"))
    if len(found) != 1:
        raise RuntimeError(f"expected one small0 checkpoint, found {found}")
    return found[0]


def read_pcm(path: Path) -> tuple[int, list[float]]:
    with wave.open(str(path), "rb") as wf:
        if wf.getnchannels() != 1 or wf.getsampwidth() != 2:
            raise ValueError(f"expected mono 16-bit PCM WAV: {path}")
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    ints = struct.unpack("<" + "h" * (len(raw) // 2), raw)
    return sr, [x / 32768.0 for x in ints]


def circular_frame(samples: list[float], sr: int, start_s: float, duration_s: float) -> list[float]:
    total = len(samples)
    start = round(start_s * sr) % total
    count = max(1, round(duration_s * sr))
    return [samples[(start + i) % total] for i in range(count)]


def goertzel_power(samples: list[float], frequency: float, sr: int) -> float:
    coeff = 2.0 * math.cos(2.0 * math.pi * frequency / sr)
    s1 = s2 = 0.0
    for x in samples:
        s0 = x + coeff * s1 - s2
        s2 = s1
        s1 = s0
    return max(s1 * s1 + s2 * s2 - coeff * s1 * s2, 0.0)


def bank(sr: int) -> list[tuple[int, float]]:
    result = []
    for midi in range(MIDI_LOW, MIDI_HIGH + 1):
        f = 440.0 * (2.0 ** ((midi - 69) / 12.0))
        if f < sr * 0.475:
            result.append((midi % 12, f))
    return result


def chroma(frame: list[float], sr: int, frequency_bank: list[tuple[int, float]]) -> list[float]:
    bins = [0.0] * 12
    for pitch_class, f in frequency_bank:
        bins[pitch_class] += goertzel_power(frame, f, sr)
    norm = math.sqrt(sum(v * v for v in bins))
    return [v / norm for v in bins] if norm > 1e-20 else bins


def cosine(a: list[float], b: list[float]) -> float:
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(x * x for x in b))
    if na <= 1e-20 or nb <= 1e-20:
        return 0.0
    return sum(x * y for x, y in zip(a, b)) / (na * nb)


def clean_inside(times, duration: float) -> list[float]:
    return [float(t) for t in times if -TOLERANCE <= float(t) < duration - TOLERANCE]


def nearest_index(values: list[float], target: float) -> int:
    return min(range(len(values)), key=lambda i: abs(values[i] - target))


def build_variant_features(path: Path, beats: list[float], downbeats: list[float]) -> dict:
    sr, samples = read_pcm(path)
    if len(beats) < 4 or not downbeats:
        raise RuntimeError(f"insufficient detected grid for {path}")
    diffs = [b - a for a, b in zip(beats, beats[1:]) if 0.2 <= b - a <= 1.5]
    beat_period = statistics.median(diffs)
    analysis_sr = sr // DOWNSAMPLE
    frequency_bank = bank(analysis_sr)
    features = []
    for beat_time in beats:
        frame = circular_frame(samples, sr, beat_time, beat_period)[::DOWNSAMPLE]
        features.append(chroma(frame, analysis_sr, frequency_bank))
    return {"beats": beats, "downbeats": downbeats, "beat_period": beat_period, "features": features}


def context_at(grid: dict, downbeat_time: float) -> list[list[float]]:
    start = nearest_index(grid["beats"], downbeat_time)
    feats = grid["features"]
    return [feats[(start + i) % len(feats)] for i in range(CONTEXT_BEATS)]


def context_similarity(a: list[list[float]], b: list[list[float]]) -> float:
    return sum(cosine(x, y) for x, y in zip(a, b)) / len(a)


def evaluate_query(candidates: list[dict]) -> dict:
    positives = [c for c in candidates if c["label"] == "positive"]
    if len(positives) != 1:
        return {"covered": False, "positive_count": len(positives)}
    ranked = sorted(candidates, key=lambda c: (-c["score"], c["time_seconds"]))
    pos = positives[0]
    rank = next(i + 1 for i, c in enumerate(ranked) if c["id"] == pos["id"])
    negatives = [c["score"] for c in candidates if c["label"] != "positive"]
    pairwise = sum(1.0 if pos["score"] > n else 0.5 if pos["score"] == n else 0.0 for n in negatives) / len(negatives)
    return {
        "covered": True,
        "positive_rank": rank,
        "hit_at_1": 1.0 if rank <= 1 else 0.0,
        "hit_at_3": 1.0 if rank <= 3 else 0.0,
        "rr": 1.0 / rank,
        "pairwise": pairwise,
        "top_candidate": ranked[0]["id"],
    }


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, default=ROOT / "out")
    args = p.parse_args()
    out = args.output.resolve()
    fixture = out / "fixture"
    out.mkdir(parents=True, exist_ok=True)
    if fixture.exists():
        shutil.rmtree(fixture)
    fixture.mkdir(parents=True)

    subprocess.run([sys.executable, str(TRANSFORMER), "--output", str(fixture)], check=True)
    manifest = json.loads((fixture / "manifest.json").read_text(encoding="utf-8"))
    audio_root = fixture / manifest["audio_root"]
    g = manifest["generator"]
    sr = int(g["sample_rate"])
    duration = int(g["loop_frames"]) / sr
    bar_seconds = int(g["bar_frames"]) / sr

    model = File2Beats(checkpoint_path="small0", device="cpu", dbn=False)
    grids = {}
    detector = []
    for variant in manifest["variants"]:
        wav = audio_root / variant["path"]
        beats_raw, downbeats_raw = model(wav)
        beats = clean_inside(beats_raw, duration)
        downbeats = clean_inside(downbeats_raw, duration)
        grid = build_variant_features(wav, beats, downbeats)
        grids[variant["id"]] = grid
        detector.append({
            "id": variant["id"],
            "beats": beats,
            "downbeats": downbeats,
            "beat_period": grid["beat_period"],
            "audio_sha256": variant["sha256"],
            "transform_profile": variant["transform_profile"],
        })

    query_rows = []
    evaluations = []
    for query in manifest["queries"]:
        source = grids[query["source_variant"]]
        target = grids[query["target_variant"]]
        if not source["downbeats"] or not target["downbeats"]:
            evaluations.append({"covered": False, "positive_count": 0})
            continue
        source_context = context_at(source, source["downbeats"][0])
        gt_time = (int(query["positive_physical_bar"]) - 1) * bar_seconds
        candidates = []
        for i, target_time in enumerate(target["downbeats"]):
            score = context_similarity(source_context, context_at(target, target_time))
            candidates.append({
                "id": f"{query['id']}:detected-db{i+1}",
                "time_seconds": target_time,
                "score": score,
                "label": "positive" if abs(target_time - gt_time) <= TOLERANCE else "hard_negative",
                "ground_truth_error_seconds": abs(target_time - gt_time),
            })
        ev = evaluate_query(candidates)
        evaluations.append(ev)
        query_rows.append({
            "id": query["id"],
            "source_variant": query["source_variant"],
            "target_variant": query["target_variant"],
            "hidden_ground_truth_seconds": gt_time,
            "candidates": candidates,
            "evaluation": ev,
        })

    covered = [e for e in evaluations if e.get("covered")]
    report = {
        "schema": "audio-loop-transform-robustness-evaluation/v1",
        "query_count": len(manifest["queries"]),
        "covered_queries": len(covered),
        "candidate_coverage": len(covered) / len(manifest["queries"]),
        "hit_at_1": sum(e["hit_at_1"] for e in covered) / len(covered) if covered else 0.0,
        "hit_at_3": sum(e["hit_at_3"] for e in covered) / len(covered) if covered else 0.0,
        "mrr": sum(e["rr"] for e in covered) / len(covered) if covered else 0.0,
        "pairwise_ranking_accuracy": sum(e["pairwise"] for e in covered) / len(covered) if covered else 0.0,
        "detector": detector,
        "queries": query_rows,
        "transform_policy": manifest["transform_policy"],
    }
    checkpoint = locate_checkpoint()
    report["checkpoint"] = {
        "name": checkpoint.name,
        "size": checkpoint.stat().st_size,
        "sha256": sha256(checkpoint),
        "expected_sha256": EXPECTED_SMALL0_SHA256,
    }
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("query_count", "covered_queries", "candidate_coverage", "hit_at_1", "hit_at_3", "mrr", "pairwise_ranking_accuracy")}, indent=2))


if __name__ == "__main__":
    main()
