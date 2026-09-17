#!/usr/bin/env python3
"""Evaluate Beat This + adaptive chroma context on pinned real seamless loop packs."""
from __future__ import annotations

import hashlib
import io
import json
import math
import statistics
import urllib.request
import wave
import zipfile
from pathlib import Path

import numpy as np
import torch
from beat_this.inference import File2Beats

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
AUDIO_DIR = OUT / "runtime-audio"
UA = "ziro-lab-audio-loop-benchmark/0.1 (+https://github.com/ziro-lab/chat-native-work-lab-001)"
EXPECTED_SMALL0_SHA256 = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"
TOLERANCE = 0.070
MARGIN_THRESHOLD = 0.05
CONTEXT_BARS = (2, 4, 8)
BEATS_PER_BAR = 4
DOWNSAMPLE = 4
MIDI_LOW = 42
MIDI_HIGH = 83

SOURCES = [
    {
        "id": "arcade",
        "url": "https://cdn.colorosse.com/downloads/arcade-music-loop/arcade-music-loop.zip",
        "sha256": "9a75c3760957401c66554fecca4140d77c3a6a66cd173e890009558970af951a",
        "prefix": "arcade-music-loop",
        "license": "CC0-1.0",
        "author": "Oğuzhan Girgin",
        "source_page": "https://www.colorosse.com/assets/audio/music/arcade-music-loop",
        "tempo_bpm": 151.999,
        "beats_per_loop": 16,
    },
    {
        "id": "vellum",
        "url": "https://cdn.colorosse.com/downloads/vellum-music-loop/vellum-music-loop.zip",
        "sha256": "bd670a72dbdf8e97a804eb6a9181726df1f7d9c3c6095deb283225a3bc0e9ab1",
        "prefix": "vellum-music-loop",
        "license": "CC0-1.0",
        "author": "Oğuzhan Girgin",
        "source_page": "https://www.colorosse.com/assets/audio/music/vellum-music-loop",
        "tempo_bpm": 127.999,
        "beats_per_loop": 16,
    },
    {
        "id": "anvil",
        "url": "https://cdn.colorosse.com/downloads/anvil-music-loop/anvil-music-loop.zip",
        "sha256": "ac1654cc228e5f776f3458c310cc1725743cb6b3af05816dc7e771cd25fbea51",
        "prefix": "anvil-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "source_page": "https://www.colorosse.com/assets/audio/music/anvil-music-loop",
        "tempo_bpm": 62.002,
        "beats_per_loop": 16,
    },
    {
        "id": "prism",
        "url": "https://cdn.colorosse.com/downloads/prism-music-loop/prism-music-loop.zip",
        "sha256": "38483c451b674148ee10d980aca8a00ab70eb815a82684db3879af72c5fc645d",
        "prefix": "prism-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "source_page": "https://www.colorosse.com/assets/audio/music/prism-music-loop",
        "tempo_bpm": 52.001,
        "beats_per_loop": 8,
    },
    {
        "id": "rust",
        "url": "https://cdn.colorosse.com/downloads/rust-music-loop/rust-music-loop.zip",
        "sha256": "dd2f6e2f59289a84b952aa85e298bfeeec8d3fc2215003f85d20baebb7328f12",
        "prefix": "rust-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "source_page": "https://www.colorosse.com/assets/audio/music/rust-music-loop",
        "tempo_bpm": 108.0,
        "beats_per_loop": 16,
    },
    {
        "id": "timber",
        "url": "https://cdn.colorosse.com/downloads/timber-music-loop/timber-music-loop.zip",
        "sha256": "f0efbb99ffeeb0f4e61d04c518fac792ca2afc228cab495e1f7bf62cfc14a3af",
        "prefix": "timber-music-loop",
        "license": "CC-BY-4.0",
        "author": "Oğuzhan Girgin",
        "source_page": "https://www.colorosse.com/assets/audio/music/timber-music-loop",
        "tempo_bpm": 84.0,
        "beats_per_loop": 16,
    },
]


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def request_bytes(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"})
    with urllib.request.urlopen(req, timeout=90) as response:
        return response.read()


def locate_checkpoint() -> Path:
    root = Path(torch.hub.get_dir()) / "checkpoints"
    found = sorted(root.glob("*small0*.ckpt"))
    if len(found) != 1:
        raise RuntimeError(f"expected one small0 checkpoint, found {found}")
    return found[0]


def read_wav_bytes(data: bytes) -> tuple[int, np.ndarray]:
    with wave.open(io.BytesIO(data), "rb") as wf:
        if wf.getnchannels() != 1 or wf.getsampwidth() != 2:
            raise ValueError("expected mono 16-bit PCM WAV")
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    samples = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    return sr, samples


def write_wav(path: Path, sr: int, samples: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    clipped = np.clip(samples, -0.999, 0.999)
    pcm = np.rint(clipped * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(pcm.tobytes())


def download_pack(source: dict) -> dict:
    data = request_bytes(source["url"])
    digest = sha256_bytes(data)
    if digest != source["sha256"]:
        raise RuntimeError(f"{source['id']}: ZIP digest drift: expected {source['sha256']}, got {digest}")
    if not zipfile.is_zipfile(io.BytesIO(data)):
        raise RuntimeError(f"{source['id']}: pinned download is not a ZIP")
    with zipfile.ZipFile(io.BytesIO(data), "r") as zf:
        loop_name = f"{source['prefix']}/wav/loop.wav"
        if loop_name not in zf.namelist():
            raise RuntimeError(f"{source['id']}: missing {loop_name}")
        sr, loop = read_wav_bytes(zf.read(loop_name))
        stem_names = sorted(
            n for n in zf.namelist()
            if n.startswith(f"{source['prefix']}/wav/stem-") and n.lower().endswith(".wav")
        )
        if not stem_names:
            raise RuntimeError(f"{source['id']}: no WAV stems")
        stems = []
        for name in stem_names:
            stem_sr, samples = read_wav_bytes(zf.read(name))
            if stem_sr != sr or len(samples) != len(loop):
                raise RuntimeError(f"{source['id']}: stem shape mismatch: {name}")
            stems.append((Path(name).stem.removeprefix("stem-"), samples))
    return {"sr": sr, "loop": loop, "stems": stems, "zip_size": len(data), "zip_sha256": digest}


def weights_for(stems: list[tuple[str, np.ndarray]], profile: int) -> list[float]:
    a = [0.58, 1.18, 0.76, 1.30, 0.66]
    b = [1.24, 0.64, 1.12, 0.72, 1.08]
    base = a if profile == 1 else b
    return [base[i % len(base)] for i in range(len(stems))]


def remix(stems: list[tuple[str, np.ndarray]], profile: int) -> tuple[np.ndarray, dict]:
    weights = weights_for(stems, profile)
    mixed = np.zeros_like(stems[0][1], dtype=np.float32)
    stem_weights = {}
    for (name, samples), weight in zip(stems, weights):
        mixed += samples * float(weight)
        stem_weights[name] = weight
    # Mild nonlinear dynamics + deterministic low-level noise make the target a different render.
    mixed = np.tanh(mixed * (1.05 if profile == 1 else 1.18)).astype(np.float32)
    rng = np.random.default_rng(1000 + profile * 137 + len(stems))
    mixed += rng.normal(0.0, 0.00045 if profile == 1 else 0.00070, size=len(mixed)).astype(np.float32)
    peak = float(np.max(np.abs(mixed))) if len(mixed) else 1.0
    if peak > 0:
        mixed *= np.float32(0.88 / peak)
    return mixed, {"stem_weights": stem_weights, "post": "tanh + deterministic low-level noise + peak normalization"}


def circular_rotate(samples: np.ndarray, frames: int) -> np.ndarray:
    frames %= len(samples)
    if frames == 0:
        return samples.copy()
    return np.concatenate([samples[frames:], samples[:frames]]).astype(np.float32, copy=False)


def clean_inside(times, duration: float) -> list[float]:
    return [float(t) for t in times if -TOLERANCE <= float(t) < duration - TOLERANCE]


def goertzel_power(samples: np.ndarray, frequency: float, sr: float) -> float:
    coeff = 2.0 * math.cos(2.0 * math.pi * frequency / sr)
    s1 = s2 = 0.0
    for x in samples:
        s0 = float(x) + coeff * s1 - s2
        s2 = s1
        s1 = s0
    return max(s1 * s1 + s2 * s2 - coeff * s1 * s2, 0.0)


def frequency_bank(sr: float) -> list[tuple[int, float]]:
    out = []
    for midi in range(MIDI_LOW, MIDI_HIGH + 1):
        f = 440.0 * (2.0 ** ((midi - 69) / 12.0))
        if f < sr * 0.475:
            out.append((midi % 12, f))
    return out


def chroma(frame: np.ndarray, sr: float, bank: list[tuple[int, float]]) -> list[float]:
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


def nearest_index(values: list[float], target: float) -> int:
    return min(range(len(values)), key=lambda i: abs(values[i] - target))


def build_grid(samples: np.ndarray, sr: int, beats: list[float], downbeats: list[float]) -> dict:
    if len(beats) < 4 or not downbeats:
        raise RuntimeError(f"insufficient detected grid beats={len(beats)} downbeats={len(downbeats)}")
    diffs = [b - a for a, b in zip(beats, beats[1:]) if 0.15 <= b - a <= 2.0]
    if not diffs:
        raise RuntimeError("no plausible beat intervals")
    beat_period = statistics.median(diffs)
    bank = frequency_bank(sr / DOWNSAMPLE)
    features = []
    for beat_time in beats:
        start = int(round(beat_time * sr)) % len(samples)
        count = max(1, int(round(beat_period * sr)))
        idx = (start + np.arange(count)) % len(samples)
        frame = samples[idx][::DOWNSAMPLE]
        features.append(chroma(frame, sr / DOWNSAMPLE, bank))
    return {"beats": beats, "downbeats": downbeats, "features": features, "beat_period": beat_period}


def context_at(grid: dict, downbeat_time: float, bars: int) -> list[list[float]]:
    start = nearest_index(grid["beats"], downbeat_time)
    feats = grid["features"]
    count = bars * BEATS_PER_BAR
    return [feats[(start + i) % len(feats)] for i in range(count)]


def context_similarity(a: list[list[float]], b: list[list[float]]) -> float:
    return sum(cosine(x, y) for x, y in zip(a, b)) / len(a)


def top_margin(candidates: list[dict], key: str) -> tuple[float, dict, dict]:
    ranked = sorted(candidates, key=lambda c: (-float(c[key]), float(c["time_seconds"])))
    if len(ranked) < 2:
        raise RuntimeError("fewer than two candidates")
    return float(ranked[0][key]) - float(ranked[1][key]), ranked[0], ranked[1]


def analyze_query(model: File2Beats, source_path: Path, target_path: Path, source_samples: np.ndarray, target_samples: np.ndarray, sr: int, ground_truth_seconds: float) -> dict:
    duration = len(source_samples) / sr
    source_beats_raw, source_downbeats_raw = model(source_path)
    target_beats_raw, target_downbeats_raw = model(target_path)
    source_beats = clean_inside(source_beats_raw, duration)
    source_downbeats = clean_inside(source_downbeats_raw, duration)
    target_beats = clean_inside(target_beats_raw, duration)
    target_downbeats = clean_inside(target_downbeats_raw, duration)
    detector = {
        "source_beats": len(source_beats),
        "source_downbeats": len(source_downbeats),
        "target_beats": len(target_beats),
        "target_downbeats": len(target_downbeats),
    }
    if len(source_beats) < 4 or not source_downbeats:
        return {"status": "abstained", "reason": "insufficient_source_rhythm_grid", "detector": detector}
    if len(target_beats) < 4 or len(target_downbeats) < 2:
        return {"status": "abstained", "reason": "insufficient_target_rhythm_grid", "detector": detector}
    try:
        source_grid = build_grid(source_samples, sr, source_beats, source_downbeats)
        target_grid = build_grid(target_samples, sr, target_beats, target_downbeats)
    except RuntimeError as exc:
        return {"status": "abstained", "reason": str(exc), "detector": detector}

    source_anchor = min(source_downbeats, key=abs)
    source_contexts = {bars: context_at(source_grid, source_anchor, bars) for bars in CONTEXT_BARS}
    candidates = []
    for i, t in enumerate(target_downbeats):
        row = {"id": f"db{i+1}", "time_seconds": float(t)}
        for bars in CONTEXT_BARS:
            row[f"score_{bars}bar"] = context_similarity(source_contexts[bars], context_at(target_grid, t, bars))
        candidates.append(row)

    decisions = []
    chosen = None
    selected_bars = None
    for bars in CONTEXT_BARS:
        margin, first, second = top_margin(candidates, f"score_{bars}bar")
        decisions.append({
            "context_bars": bars,
            "top1_margin": margin,
            "top_candidate": first["id"],
            "top_time_seconds": first["time_seconds"],
            "runner_up": second["id"],
        })
        if margin >= MARGIN_THRESHOLD or bars == CONTEXT_BARS[-1]:
            chosen = first
            selected_bars = bars
            break
    assert chosen is not None and selected_bars is not None

    # Hidden Ground Truth is consulted only after the policy has selected a candidate.
    positive_candidates = [c for c in candidates if abs(c["time_seconds"] - ground_truth_seconds) <= TOLERANCE]
    coverage = len(positive_candidates) >= 1
    correct = abs(float(chosen["time_seconds"]) - ground_truth_seconds) <= TOLERANCE
    return {
        "status": "evaluated",
        "detector": detector,
        "source_anchor_seconds": source_anchor,
        "ground_truth_seconds": ground_truth_seconds,
        "candidate_count": len(candidates),
        "candidate_coverage": coverage,
        "selected_context_bars": selected_bars,
        "selected_candidate": chosen["id"],
        "selected_time_seconds": chosen["time_seconds"],
        "correct": correct,
        "decisions": decisions,
        "candidates": candidates,
    }


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    model = File2Beats(checkpoint_path="small0", device="cpu", dbn=False)
    source_reports = []
    query_reports = []

    for source in SOURCES:
        pack = download_pack(source)
        sr = int(pack["sr"])
        loop = pack["loop"]
        bars = int(source["beats_per_loop"] // BEATS_PER_BAR)
        if source["beats_per_loop"] % BEATS_PER_BAR or len(loop) % bars:
            raise RuntimeError(f"{source['id']}: source loop is not an exact integer-bar frame length")
        bar_frames = len(loop) // bars
        beat_estimate = (len(loop) / sr) * float(source["tempo_bpm"]) / 60.0
        source_path = AUDIO_DIR / f"{source['id']}-source.wav"
        write_wav(source_path, sr, loop)
        target_meta = []
        rotations = [1, 2 if bars >= 4 else 1]
        for profile, rotation in enumerate(rotations, start=1):
            mixed, mix_meta = remix(pack["stems"], profile)
            rotated = circular_rotate(mixed, rotation * bar_frames)
            target_path = AUDIO_DIR / f"{source['id']}-target-{profile}.wav"
            write_wav(target_path, sr, rotated)
            gt_frames = (bars - rotation) * bar_frames
            gt_seconds = gt_frames / sr
            result = analyze_query(model, source_path, target_path, loop, rotated, sr, gt_seconds)
            query_id = f"{source['id']}-profile-{profile}"
            query_reports.append({
                "id": query_id,
                "source_id": source["id"],
                "profile": profile,
                "rotation_bars": rotation,
                "bars_per_loop": bars,
                "ground_truth_frame": gt_frames,
                "mix": mix_meta,
                "result": result,
            })
            target_meta.append({"id": query_id, "rotation_bars": rotation, "ground_truth_frame": gt_frames, "mix": mix_meta})
        source_reports.append({
            "id": source["id"],
            "license": source["license"],
            "author": source["author"],
            "source_page": source["source_page"],
            "zip_url": source["url"],
            "zip_sha256": pack["zip_sha256"],
            "zip_size": pack["zip_size"],
            "sample_rate": sr,
            "frames": len(loop),
            "duration_seconds": len(loop) / sr,
            "declared_tempo_bpm": source["tempo_bpm"],
            "beats_per_loop": source["beats_per_loop"],
            "beat_count_from_duration_and_tempo": beat_estimate,
            "bars_per_loop": bars,
            "bar_frames": bar_frames,
            "stem_names": [name for name, _ in pack["stems"]],
            "loop_pcm_sha256": hashlib.sha256(np.rint(np.clip(loop, -0.999, 0.999) * 32767.0).astype("<i2").tobytes()).hexdigest(),
            "targets": target_meta,
        })

    evaluated = [q for q in query_reports if q["result"]["status"] == "evaluated"]
    abstained = [q for q in query_reports if q["result"]["status"] == "abstained"]
    covered = [q for q in evaluated if q["result"]["candidate_coverage"]]
    correct = [q for q in evaluated if q["result"]["correct"]]
    context_counts = {str(b): 0 for b in CONTEXT_BARS}
    for q in evaluated:
        context_counts[str(q["result"]["selected_context_bars"])] += 1
    per_source = {}
    for source in SOURCES:
        rows = [q for q in query_reports if q["source_id"] == source["id"]]
        eval_rows = [q for q in rows if q["result"]["status"] == "evaluated"]
        per_source[source["id"]] = {
            "queries": len(rows),
            "evaluated": len(eval_rows),
            "candidate_covered": sum(q["result"].get("candidate_coverage", False) for q in eval_rows),
            "correct": sum(q["result"].get("correct", False) for q in eval_rows),
            "statuses": [q["result"]["status"] for q in rows],
            "selected_context_bars": [q["result"].get("selected_context_bars") for q in rows],
            "detectors": [q["result"].get("detector") for q in rows],
        }

    checkpoint = locate_checkpoint()
    report = {
        "schema": "audio-real-loop-ranking/v1",
        "query_count": len(query_reports),
        "evaluated_queries": len(evaluated),
        "abstained_queries": len(abstained),
        "candidate_coverage": len(covered) / len(query_reports),
        "covered_hit_at_1": len([q for q in covered if q["result"]["correct"]]) / len(covered) if covered else 0.0,
        "end_to_end_accuracy": len(correct) / len(query_reports),
        "selected_context_counts": context_counts,
        "per_source": per_source,
        "sources": source_reports,
        "queries": query_reports,
        "policy": {"contexts_bars": list(CONTEXT_BARS), "top1_margin_threshold": MARGIN_THRESHOLD, "tolerance_seconds": TOLERANCE},
        "checkpoint": {
            "name": checkpoint.name,
            "size": checkpoint.stat().st_size,
            "sha256": hashlib.sha256(checkpoint.read_bytes()).hexdigest(),
            "expected_sha256": EXPECTED_SMALL0_SHA256,
        },
    }
    (OUT / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({
        "query_count": report["query_count"],
        "evaluated_queries": report["evaluated_queries"],
        "abstained_queries": report["abstained_queries"],
        "candidate_coverage": report["candidate_coverage"],
        "covered_hit_at_1": report["covered_hit_at_1"],
        "end_to_end_accuracy": report["end_to_end_accuracy"],
        "selected_context_counts": report["selected_context_counts"],
        "per_source": report["per_source"],
    }, indent=2))


if __name__ == "__main__":
    main()
