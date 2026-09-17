#!/usr/bin/env python3
"""Rank detected downbeat candidates with 2/4/8-bar chroma context."""
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
GENERATOR = ROOT / "generate_fixture.py"
EXPECTED_SMALL0_SHA256 = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"
TOLERANCE = 0.070
DOWNSAMPLE = 4
MIDI_LOW = 42
MIDI_HIGH = 83
CONTEXT_BARS = (2, 4, 8)
BEATS_PER_BAR = 4


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


def frequency_bank(sr: int) -> list[tuple[int, float]]:
    out = []
    for midi in range(MIDI_LOW, MIDI_HIGH + 1):
        f = 440.0 * (2.0 ** ((midi - 69) / 12.0))
        if f < sr * 0.475:
            out.append((midi % 12, f))
    return out


def chroma(frame: list[float], sr: int, bank: list[tuple[int, float]]) -> list[float]:
    bins = [0.0] * 12
    for pitch_class, f in bank:
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


def build_grid(path: Path, beats: list[float], downbeats: list[float]) -> dict:
    sr, samples = read_pcm(path)
    duration = len(samples) / sr
    if len(beats) < 16 or len(downbeats) < 8:
        raise RuntimeError(f"insufficient detected grid for {path}: beats={len(beats)} downbeats={len(downbeats)}")
    diffs = [b - a for a, b in zip(beats, beats[1:]) if 0.2 <= b - a <= 1.5]
    beat_period = statistics.median(diffs)
    analysis_sr = sr // DOWNSAMPLE
    bank = frequency_bank(analysis_sr)
    feats = []
    for beat_time in beats:
        frame = circular_frame(samples, sr, beat_time, beat_period)[::DOWNSAMPLE]
        feats.append(chroma(frame, analysis_sr, bank))
    return {"beats": beats, "downbeats": downbeats, "beat_period": beat_period, "features": feats, "duration": duration}


def context_at(grid: dict, downbeat_time: float, context_beats: int) -> list[list[float]]:
    start = nearest_index(grid["beats"], downbeat_time)
    feats = grid["features"]
    return [feats[(start + i) % len(feats)] for i in range(context_beats)]


def context_similarity(a: list[list[float]], b: list[list[float]]) -> float:
    return sum(cosine(x, y) for x, y in zip(a, b)) / len(a)


def evaluate_candidates(candidates: list[dict], score_key: str) -> dict:
    positives = [c for c in candidates if c["label"] == "positive"]
    if len(positives) != 1:
        return {"covered": False, "positive_count": len(positives)}
    ranked = sorted(candidates, key=lambda c: (-c[score_key], c["time_seconds"]))
    positive = positives[0]
    pos_rank = next(i + 1 for i, c in enumerate(ranked) if c["id"] == positive["id"])
    ps = positive[score_key]
    negatives = [c[score_key] for c in candidates if c["label"] != "positive"]
    pairwise = sum(1.0 if ps > n else 0.5 if ps == n else 0.0 for n in negatives) / len(negatives)
    return {
        "covered": True,
        "positive_rank": pos_rank,
        "hit_at_1": 1.0 if pos_rank <= 1 else 0.0,
        "hit_at_3": 1.0 if pos_rank <= 3 else 0.0,
        "rr": 1.0 / pos_rank,
        "pairwise": pairwise,
        "top_candidate": ranked[0]["id"],
    }


def nearest_candidate(candidates: list[dict], target_time: float) -> dict:
    return min(candidates, key=lambda c: abs(c["time_seconds"] - target_time))


def aggregate(evals: list[dict]) -> dict:
    covered = [e for e in evals if e.get("covered")]
    return {
        "query_count": len(evals),
        "covered_queries": len(covered),
        "candidate_coverage": len(covered) / len(evals),
        "hit_at_1": sum(e["hit_at_1"] for e in covered) / len(covered) if covered else 0.0,
        "hit_at_3": sum(e["hit_at_3"] for e in covered) / len(covered) if covered else 0.0,
        "mrr": sum(e["rr"] for e in covered) / len(covered) if covered else 0.0,
        "pairwise_ranking_accuracy": sum(e["pairwise"] for e in covered) / len(covered) if covered else 0.0,
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--output", type=Path, default=ROOT / "out")
    args = ap.parse_args()
    out = args.output.resolve()
    fixture = out / "fixture"
    if out.exists():
        shutil.rmtree(out)
    fixture.mkdir(parents=True)

    subprocess.run([sys.executable, str(GENERATOR), "--output", str(fixture)], check=True)
    manifest = json.loads((fixture / "manifest.json").read_text(encoding="utf-8"))
    bar_seconds = float(manifest["generator"]["bar_seconds"])
    duration = float(manifest["generator"]["bars"]) * bar_seconds

    model = File2Beats(checkpoint_path="small0", device="cpu", dbn=False)
    grids = {}
    detector = []
    for item in [*manifest["sources"], *manifest["targets"]]:
        path = fixture / item["path"]
        beats_raw, downbeats_raw = model(path)
        beats = clean_inside(beats_raw, duration)
        downbeats = clean_inside(downbeats_raw, duration)
        grid = build_grid(path, beats, downbeats)
        grids[item["id"]] = grid
        detector.append({"id": item["id"], "beats": len(beats), "downbeats": len(downbeats), "beat_period": grid["beat_period"]})

    target_meta = {x["id"]: x for x in manifest["targets"]}
    per_context_evals = {bars: [] for bars in CONTEXT_BARS}
    decoy_competitive = {bars: {"2bar": 0, "4bar": 0} for bars in CONTEXT_BARS}
    query_reports = []

    for query in manifest["queries"]:
        source = grids[query["source"]]
        target = grids[query["target"]]
        meta = target_meta[query["target"]]
        source_anchor = source["downbeats"][0]
        hidden_positive = (meta["hidden_positive_physical_bar"] - 1) * bar_seconds
        hidden_decoy2 = (meta["hidden_decoy_2bar_physical_bar"] - 1) * bar_seconds
        hidden_decoy4 = (meta["hidden_decoy_4bar_physical_bar"] - 1) * bar_seconds

        # Scores are computed before Ground Truth labels are attached.
        source_contexts = {
            bars: context_at(source, source_anchor, bars * BEATS_PER_BAR)
            for bars in CONTEXT_BARS
        }
        candidates = []
        for i, t in enumerate(target["downbeats"]):
            scores = {
                f"score_{bars}bar": context_similarity(
                    source_contexts[bars], context_at(target, t, bars * BEATS_PER_BAR)
                )
                for bars in CONTEXT_BARS
            }
            candidates.append({"id": f"{query['id']}:db{i+1}", "time_seconds": t, **scores})

        # Ground Truth becomes visible only after all candidate scores exist.
        for c in candidates:
            c["label"] = "positive" if abs(c["time_seconds"] - hidden_positive) <= TOLERANCE else "hard_negative"
            c["positive_error_seconds"] = abs(c["time_seconds"] - hidden_positive)

        decoy2 = nearest_candidate(candidates, hidden_decoy2)
        decoy4 = nearest_candidate(candidates, hidden_decoy4)
        positive = nearest_candidate(candidates, hidden_positive)
        diagnostics = {}
        for bars in CONTEXT_BARS:
            key = f"score_{bars}bar"
            ev = evaluate_candidates(candidates, key)
            per_context_evals[bars].append(ev)
            positive_score = positive[key]
            d2_gap = positive_score - decoy2[key]
            d4_gap = positive_score - decoy4[key]
            # Competitive means the known structural decoy is essentially tied or beats the positive.
            if d2_gap <= 0.015:
                decoy_competitive[bars]["2bar"] += 1
            if d4_gap <= 0.015:
                decoy_competitive[bars]["4bar"] += 1
            diagnostics[str(bars)] = {
                "evaluation": ev,
                "positive_score": positive_score,
                "decoy_2bar_score": decoy2[key],
                "decoy_4bar_score": decoy4[key],
                "positive_minus_decoy_2bar": d2_gap,
                "positive_minus_decoy_4bar": d4_gap,
            }

        query_reports.append(
            {
                "id": query["id"],
                "source": query["source"],
                "target": query["target"],
                "detected_candidates": len(candidates),
                "hidden_positive_seconds": hidden_positive,
                "hidden_decoy_2bar_seconds": hidden_decoy2,
                "hidden_decoy_4bar_seconds": hidden_decoy4,
                "diagnostics": diagnostics,
            }
        )

    metrics = {str(bars): aggregate(per_context_evals[bars]) for bars in CONTEXT_BARS}
    for bars in CONTEXT_BARS:
        metrics[str(bars)]["competitive_decoy_2bar_queries"] = decoy_competitive[bars]["2bar"]
        metrics[str(bars)]["competitive_decoy_4bar_queries"] = decoy_competitive[bars]["4bar"]

    checkpoint = locate_checkpoint()
    report = {
        "schema": "audio-loop-structural-ambiguity-report/v1",
        "metrics_by_context_bars": metrics,
        "detector": detector,
        "queries": query_reports,
        "checkpoint": {
            "name": checkpoint.name,
            "size": checkpoint.stat().st_size,
            "sha256": sha256(checkpoint),
            "expected_sha256": EXPECTED_SMALL0_SHA256,
        },
    }
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(metrics, indent=2))


if __name__ == "__main__":
    main()
