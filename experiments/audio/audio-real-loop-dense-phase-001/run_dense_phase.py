#!/usr/bin/env python3
"""Dense beat-independent chroma phase search on pinned real loop variants."""
from __future__ import annotations

import json
import math
import shutil
import subprocess
import sys
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
AUDIO_ROOT = ROOT.parent
BASE_DIR = AUDIO_ROOT / "audio-real-loop-ranking-001"
BASE_RUNNER = BASE_DIR / "run_real_benchmark.py"
BASE_OUT = BASE_DIR / "out"
OUT = ROOT / "out"
GRID_TARGET_SECONDS = 0.040
WINDOW_FRAMES = 8192
TOLERANCE = 0.070


def read_wav(path: Path) -> tuple[int, np.ndarray]:
    with wave.open(str(path), "rb") as wf:
        if wf.getnchannels() != 1 or wf.getsampwidth() != 2:
            raise ValueError(f"expected mono 16-bit PCM WAV: {path}")
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    return sr, np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0


def pitch_class_map(sr: int, n_fft: int) -> tuple[np.ndarray, np.ndarray]:
    freqs = np.fft.rfftfreq(n_fft, d=1.0 / sr)
    valid = (freqs >= 55.0) & (freqs <= 4000.0)
    idx = np.where(valid)[0]
    midi = np.rint(69.0 + 12.0 * np.log2(freqs[idx] / 440.0)).astype(np.int32)
    pcs = midi % 12
    return idx, pcs


def circular_chroma(samples: np.ndarray, sr: int) -> tuple[np.ndarray, float]:
    duration = len(samples) / sr
    frame_count = max(32, int(math.ceil(duration / GRID_TARGET_SECONDS)))
    hop_seconds = duration / frame_count
    starts = np.rint(np.arange(frame_count) * hop_seconds * sr).astype(np.int64) % len(samples)
    window = np.hanning(WINDOW_FRAMES).astype(np.float32)
    fft_bins, pitch_classes = pitch_class_map(sr, WINDOW_FRAMES)
    chroma = np.zeros((frame_count, 12), dtype=np.float32)
    offsets = np.arange(WINDOW_FRAMES, dtype=np.int64)

    for i, start in enumerate(starts):
        frame = samples[(start + offsets) % len(samples)] * window
        spectrum = np.fft.rfft(frame)
        power = np.sqrt(np.maximum((spectrum.real * spectrum.real + spectrum.imag * spectrum.imag), 0.0))
        row = np.bincount(pitch_classes, weights=power[fft_bins], minlength=12).astype(np.float32)
        norm = float(np.linalg.norm(row))
        if norm > 1e-12:
            row /= norm
        chroma[i] = row
    return chroma, hop_seconds


def phase_scores(source: np.ndarray, target: np.ndarray) -> np.ndarray:
    if source.shape != target.shape:
        raise ValueError(f"feature shape mismatch {source.shape} != {target.shape}")
    n = source.shape[0]
    scores = np.empty(n, dtype=np.float64)
    # Rows are L2 normalized, so dot product is framewise cosine similarity.
    for shift in range(n):
        scores[shift] = float(np.mean(np.sum(source * np.roll(target, -shift, axis=0), axis=1)))
    return scores


def circular_error_seconds(a: float, b: float, duration: float) -> float:
    raw = abs(a - b)
    return min(raw, duration - raw)


def source_id_to_meta(base_report: dict) -> dict:
    return {s["id"]: s for s in base_report["sources"]}


def main() -> None:
    if OUT.exists():
        shutil.rmtree(OUT)
    OUT.mkdir(parents=True)

    # Generate exactly the same pinned source/target audio used by the downbeat-only baseline.
    subprocess.run([sys.executable, str(BASE_RUNNER)], cwd=BASE_DIR, check=True)
    base_report = json.loads((BASE_OUT / "report.json").read_text(encoding="utf-8"))
    audio_dir = BASE_OUT / "runtime-audio"
    sources = source_id_to_meta(base_report)

    feature_cache: dict[str, tuple[np.ndarray, float, int, float]] = {}

    def features(path: Path) -> tuple[np.ndarray, float, int, float]:
        key = str(path)
        if key not in feature_cache:
            sr, samples = read_wav(path)
            chroma, hop_seconds = circular_chroma(samples, sr)
            feature_cache[key] = (chroma, hop_seconds, sr, len(samples) / sr)
        return feature_cache[key]

    rows = []
    per_source: dict[str, dict] = {}
    for query in base_report["queries"]:
        source_id = query["source_id"]
        profile = int(query["profile"])
        source_path = audio_dir / f"{source_id}-source.wav"
        target_path = audio_dir / f"{source_id}-target-{profile}.wav"
        src_features, src_hop, src_sr, duration = features(source_path)
        tgt_features, tgt_hop, tgt_sr, target_duration = features(target_path)
        if src_sr != tgt_sr or abs(duration - target_duration) > 1e-9:
            raise RuntimeError(f"{query['id']}: source/target audio shape mismatch")
        if abs(src_hop - tgt_hop) > 1e-12 or src_features.shape != tgt_features.shape:
            raise RuntimeError(f"{query['id']}: dense grid mismatch")

        # Candidate scores are computed before Ground Truth is read.
        scores = phase_scores(src_features, tgt_features)
        best_index = int(np.argmax(scores))
        selected_time = best_index * src_hop
        sorted_indices = np.argsort(-scores)
        runner_up_index = next(int(i) for i in sorted_indices if int(i) != best_index)
        runner_up_time = runner_up_index * src_hop

        # Ground Truth is revealed only after the phase estimate exists.
        gt_seconds = float(query["ground_truth_frame"]) / float(sources[source_id]["sample_rate"])
        error_seconds = circular_error_seconds(selected_time, gt_seconds, duration)
        correct = error_seconds <= TOLERANCE
        nearest_grid_index = int(round(gt_seconds / src_hop)) % len(scores)
        nearest_grid_time = nearest_grid_index * src_hop
        quantization_error = circular_error_seconds(nearest_grid_time, gt_seconds, duration)

        row = {
            "id": query["id"],
            "source_id": source_id,
            "profile": profile,
            "rotation_bars": query["rotation_bars"],
            "duration_seconds": duration,
            "grid_frames": len(scores),
            "grid_hop_seconds": src_hop,
            "selected_index": best_index,
            "selected_time_seconds": selected_time,
            "selected_score": float(scores[best_index]),
            "runner_up_time_seconds": runner_up_time,
            "runner_up_score": float(scores[runner_up_index]),
            "top1_minus_top2": float(scores[best_index] - scores[runner_up_index]),
            "hidden_ground_truth_seconds": gt_seconds,
            "nearest_grid_quantization_error_seconds": quantization_error,
            "phase_error_seconds": error_seconds,
            "correct_at_70ms": correct,
        }
        rows.append(row)
        bucket = per_source.setdefault(source_id, {"queries": 0, "correct": 0, "errors_seconds": []})
        bucket["queries"] += 1
        bucket["correct"] += int(correct)
        bucket["errors_seconds"].append(error_seconds)

    for bucket in per_source.values():
        bucket["accuracy"] = bucket["correct"] / bucket["queries"]
        bucket["max_error_seconds"] = max(bucket["errors_seconds"])
        bucket["mean_error_seconds"] = sum(bucket["errors_seconds"]) / len(bucket["errors_seconds"])

    accuracy = sum(r["correct_at_70ms"] for r in rows) / len(rows)
    max_quantization = max(r["nearest_grid_quantization_error_seconds"] for r in rows)
    report = {
        "schema": "audio-real-loop-dense-phase/v1",
        "query_count": len(rows),
        "hit_at_1_at_70ms": accuracy,
        "max_candidate_grid_quantization_error_seconds": max_quantization,
        "grid_target_seconds": GRID_TARGET_SECONDS,
        "stft_window_frames": WINDOW_FRAMES,
        "per_source": per_source,
        "queries": rows,
        "baseline_downbeat_only": {
            "candidate_coverage": base_report["candidate_coverage"],
            "covered_hit_at_1": base_report["covered_hit_at_1"],
            "end_to_end_accuracy": base_report["end_to_end_accuracy"],
            "abstained_queries": base_report["abstained_queries"],
        },
        "interpretation": "Dense circular chroma phase search removes Beat/Downbeat candidate coverage as a hard dependency and is compared against the exact same pinned real-audio variants.",
    }
    (OUT / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({
        "query_count": report["query_count"],
        "hit_at_1_at_70ms": report["hit_at_1_at_70ms"],
        "max_candidate_grid_quantization_error_seconds": report["max_candidate_grid_quantization_error_seconds"],
        "baseline_downbeat_only": report["baseline_downbeat_only"],
        "per_source": report["per_source"],
    }, indent=2))


if __name__ == "__main__":
    main()
