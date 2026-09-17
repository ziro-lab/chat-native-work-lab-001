#!/usr/bin/env python3
"""Discover loop period/start/end from one pseudo-song with no rhythm/period hints."""
from __future__ import annotations

import hashlib
import io
import json
import math
import shutil
import urllib.request
import wave
import zipfile
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
AUDIO_DIR = OUT / "runtime-audio"
UA = "ziro-lab-audio-loop-benchmark/0.1 (+https://github.com/ziro-lab/chat-native-work-lab-001)"
GRID_SECONDS = 0.040
WINDOW_FRAMES = 8192
TOLERANCE = 0.070
MIN_PERIOD_SECONDS = 2.0

SOURCES = [
    {"id":"arcade","url":"https://cdn.colorosse.com/downloads/arcade-music-loop/arcade-music-loop.zip","sha256":"9a75c3760957401c66554fecca4140d77c3a6a66cd173e890009558970af951a","prefix":"arcade-music-loop","license":"CC0-1.0","beats_per_loop":16},
    {"id":"vellum","url":"https://cdn.colorosse.com/downloads/vellum-music-loop/vellum-music-loop.zip","sha256":"bd670a72dbdf8e97a804eb6a9181726df1f7d9c3c6095deb283225a3bc0e9ab1","prefix":"vellum-music-loop","license":"CC0-1.0","beats_per_loop":16},
    {"id":"anvil","url":"https://cdn.colorosse.com/downloads/anvil-music-loop/anvil-music-loop.zip","sha256":"ac1654cc228e5f776f3458c310cc1725743cb6b3af05816dc7e771cd25fbea51","prefix":"anvil-music-loop","license":"CC-BY-4.0","beats_per_loop":16},
    {"id":"prism","url":"https://cdn.colorosse.com/downloads/prism-music-loop/prism-music-loop.zip","sha256":"38483c451b674148ee10d980aca8a00ab70eb815a82684db3879af72c5fc645d","prefix":"prism-music-loop","license":"CC-BY-4.0","beats_per_loop":8},
    {"id":"rust","url":"https://cdn.colorosse.com/downloads/rust-music-loop/rust-music-loop.zip","sha256":"dd2f6e2f59289a84b952aa85e298bfeeec8d3fc2215003f85d20baebb7328f12","prefix":"rust-music-loop","license":"CC-BY-4.0","beats_per_loop":16},
    {"id":"timber","url":"https://cdn.colorosse.com/downloads/timber-music-loop/timber-music-loop.zip","sha256":"f0efbb99ffeeb0f4e61d04c518fac792ca2afc228cab495e1f7bf62cfc14a3af","prefix":"timber-music-loop","license":"CC-BY-4.0","beats_per_loop":16},
]


def request_bytes(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"})
    with urllib.request.urlopen(req, timeout=90) as response:
        return response.read()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_wav_bytes(data: bytes) -> tuple[int, np.ndarray]:
    with wave.open(io.BytesIO(data), "rb") as wf:
        if wf.getnchannels() != 1 or wf.getsampwidth() != 2:
            raise ValueError("expected mono 16-bit PCM WAV")
        sr = wf.getframerate()
        raw = wf.readframes(wf.getnframes())
    return sr, np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0


def write_wav(path: Path, sr: int, samples: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.rint(np.clip(samples, -0.999, 0.999) * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(pcm.tobytes())


def load_pack(source: dict) -> dict:
    archive = request_bytes(source["url"])
    digest = sha256_bytes(archive)
    if digest != source["sha256"]:
        raise RuntimeError(f"{source['id']}: ZIP digest drift expected={source['sha256']} got={digest}")
    with zipfile.ZipFile(io.BytesIO(archive), "r") as zf:
        loop_name = f"{source['prefix']}/wav/loop.wav"
        sr, loop = read_wav_bytes(zf.read(loop_name))
        stem_names = sorted(n for n in zf.namelist() if n.startswith(f"{source['prefix']}/wav/stem-") and n.lower().endswith(".wav"))
        stems = []
        for name in stem_names:
            stem_sr, samples = read_wav_bytes(zf.read(name))
            if stem_sr != sr or len(samples) != len(loop):
                raise RuntimeError(f"{source['id']}: stem shape mismatch {name}")
            stems.append((Path(name).stem.removeprefix("stem-"), samples))
    if not stems:
        raise RuntimeError(f"{source['id']}: no stems")
    return {"sr": sr, "loop": loop, "stems": stems, "zip_sha256": digest}


def normalize(samples: np.ndarray, peak: float = 0.82) -> np.ndarray:
    result = samples.astype(np.float32, copy=True)
    m = float(np.max(np.abs(result))) if len(result) else 0.0
    if m > 1e-9:
        result *= np.float32(peak / m)
    return result


def stem_weight(name: str, profile: int, index: int) -> float:
    profiles = [
        [1.00, 0.72, 1.16, 0.84, 0.96],
        [0.66, 1.24, 0.80, 1.20, 0.74],
        [1.18, 0.90, 0.70, 1.04, 1.22],
    ]
    if profile < 3:
        return profiles[profile][index % len(profiles[profile])]
    lower = name.lower()
    if profile == 3:  # sparse intro
        if "drum" in lower: return 0.0
        if "bass" in lower: return 0.22
        if "chord" in lower: return 0.82
        if "lead" in lower: return 0.20
        if "bell" in lower: return 0.46
        if "texture" in lower: return 0.58
        return 0.35
    # sparse outro
    if "drum" in lower: return 0.0
    if "bass" in lower: return 0.12
    if "chord" in lower: return 0.70
    if "lead" in lower: return 0.46
    if "bell" in lower: return 0.38
    if "texture" in lower: return 0.62
    return 0.32


def mix_stems(stems: list[tuple[str, np.ndarray]], profile: int, peak: float = 0.82) -> np.ndarray:
    mixed = np.zeros_like(stems[0][1], dtype=np.float32)
    for i, (name, samples) in enumerate(stems):
        mixed += samples * np.float32(stem_weight(name, profile, i))
    # Different body renders are intentionally not byte-identical.
    if profile in (0, 1, 2):
        drive = [1.02, 1.14, 1.08][profile]
        mixed = np.tanh(mixed * np.float32(drive)).astype(np.float32)
    return normalize(mixed, peak)


def build_pseudo_song(source: dict, pack: dict) -> tuple[np.ndarray, dict]:
    loop = pack["loop"]
    sr = int(pack["sr"])
    bars = int(source["beats_per_loop"] // 4)
    if source["beats_per_loop"] % 4 or len(loop) % bars:
        raise RuntimeError(f"{source['id']}: loop frames do not divide into integer bars")
    bar_frames = len(loop) // bars

    body = [mix_stems(pack["stems"], p) for p in (0, 1, 2)]
    intro_mix = mix_stems(pack["stems"], 3, peak=0.62)
    outro_mix = mix_stems(pack["stems"], 4, peak=0.62)
    intro = intro_mix[:bar_frames].copy()
    outro = outro_mix[-bar_frames:].copy()
    intro *= np.linspace(0.0, 1.0, len(intro), dtype=np.float32)
    outro *= np.linspace(1.0, 0.0, len(outro), dtype=np.float32)

    song = np.concatenate([intro, body[0], body[1], body[2], outro]).astype(np.float32)
    song = normalize(song, 0.88)
    body_start = len(intro)
    body_end = body_start + 3 * len(loop)
    meta = {
        "sample_rate": sr,
        "period_frames": len(loop),
        "period_seconds": len(loop) / sr,
        "bars_per_loop": bars,
        "bar_frames": bar_frames,
        "body_start_frame": body_start,
        "body_end_frame": body_end,
        "body_start_seconds": body_start / sr,
        "body_end_seconds": body_end / sr,
        "song_frames": len(song),
        "song_seconds": len(song) / sr,
    }
    return song, meta


def pitch_class_map(sr: int, n_fft: int) -> tuple[np.ndarray, np.ndarray]:
    freqs = np.fft.rfftfreq(n_fft, d=1.0 / sr)
    valid = (freqs >= 55.0) & (freqs <= 4000.0)
    bins = np.where(valid)[0]
    midi = np.rint(69.0 + 12.0 * np.log2(freqs[bins] / 440.0)).astype(np.int32)
    return bins, midi % 12


def linear_features(samples: np.ndarray, sr: int) -> tuple[np.ndarray, float]:
    hop_frames = max(1, int(round(GRID_SECONDS * sr)))
    frame_count = max(1, int(math.ceil(len(samples) / hop_frames)))
    starts = np.arange(frame_count, dtype=np.int64) * hop_frames
    hann = np.hanning(WINDOW_FRAMES).astype(np.float32)
    fft_bins, pitch_classes = pitch_class_map(sr, WINDOW_FRAMES)
    chroma = np.zeros((frame_count, 12), dtype=np.float32)
    log_energy = np.zeros(frame_count, dtype=np.float32)

    for i, start in enumerate(starts):
        frame = np.zeros(WINDOW_FRAMES, dtype=np.float32)
        end = min(len(samples), int(start) + WINDOW_FRAMES)
        if end > start:
            frame[:end-int(start)] = samples[int(start):end]
        log_energy[i] = np.float32(math.log(1e-8 + float(np.sqrt(np.mean(frame * frame)))))
        spectrum = np.fft.rfft(frame * hann)
        mag = np.sqrt(np.maximum(spectrum.real * spectrum.real + spectrum.imag * spectrum.imag, 0.0))
        row = np.bincount(pitch_classes, weights=mag[fft_bins], minlength=12).astype(np.float32)
        norm = float(np.linalg.norm(row))
        if norm > 1e-12:
            row /= norm
        chroma[i] = row

    delta = np.diff(chroma, axis=0, prepend=chroma[[0]])
    # Combine harmonic state, harmonic motion and energy, then globally whiten.
    x = np.concatenate([chroma, delta * np.float32(0.75), log_energy[:, None] * np.float32(0.20)], axis=1).astype(np.float64)
    mean = np.mean(x, axis=0, keepdims=True)
    std = np.std(x, axis=0, keepdims=True)
    x = (x - mean) / np.maximum(std, 1e-5)
    return x, hop_frames / sr


def moving_sum(values: np.ndarray, window: int) -> np.ndarray:
    if window <= 0 or len(values) < window:
        return np.empty(0, dtype=np.float64)
    c = np.concatenate([[0.0], np.cumsum(values, dtype=np.float64)])
    return c[window:] - c[:-window]


def recurrence_for_lag(x: np.ndarray, lag: int) -> tuple[float, int]:
    a = x[:-lag]
    b = x[lag:]
    if len(a) < lag:
        return -1.0, -1
    numerator = np.sum(a * b, axis=1)
    norm_a = np.sum(a * a, axis=1)
    norm_b = np.sum(b * b, axis=1)
    n = moving_sum(numerator, lag)
    na = moving_sum(norm_a, lag)
    nb = moving_sum(norm_b, lag)
    denom = np.sqrt(np.maximum(na * nb, 1e-12))
    scores = n / denom
    idx = int(np.argmax(scores))
    return float(scores[idx]), idx


def discover(samples: np.ndarray, sr: int) -> dict:
    x, hop_seconds = linear_features(samples, sr)
    n = len(x)
    min_lag = max(2, int(math.ceil(MIN_PERIOD_SECONDS / hop_seconds)))
    max_lag = min(n // 2 - 1, int(math.floor((len(samples) / sr) * 0.45 / hop_seconds)))
    if max_lag <= min_lag:
        raise RuntimeError("track too short for recurrence search")

    peaks = []
    for lag in range(min_lag, max_lag + 1):
        score, start = recurrence_for_lag(x, lag)
        if start >= 0:
            peaks.append({"lag_frames": lag, "score": score, "start_frame": start})
    ranked = sorted(peaks, key=lambda p: (-p["score"], p["lag_frames"]))
    best = ranked[0]
    # Report a non-neighbouring runner-up so confidence is not dominated by the same peak ±1 frame.
    runner_up = next((p for p in ranked[1:] if abs(p["lag_frames"] - best["lag_frames"]) >= 3), ranked[1])
    period = best["lag_frames"] * hop_seconds
    start = best["start_frame"] * hop_seconds
    end = start + period
    return {
        "grid_hop_seconds": hop_seconds,
        "feature_frames": n,
        "search_min_seconds": min_lag * hop_seconds,
        "search_max_seconds": max_lag * hop_seconds,
        "selected_period_seconds": period,
        "selected_start_seconds": start,
        "selected_end_seconds": end,
        "selected_score": best["score"],
        "runner_up_period_seconds": runner_up["lag_frames"] * hop_seconds,
        "runner_up_score": runner_up["score"],
        "top1_minus_runner_up": best["score"] - runner_up["score"],
        "top_peaks": [
            {
                "period_seconds": p["lag_frames"] * hop_seconds,
                "start_seconds": p["start_frame"] * hop_seconds,
                "score": p["score"],
            }
            for p in ranked[:10]
        ],
    }


def main() -> None:
    if OUT.exists():
        shutil.rmtree(OUT)
    AUDIO_DIR.mkdir(parents=True)
    rows = []
    for source in SOURCES:
        pack = load_pack(source)
        song, hidden = build_pseudo_song(source, pack)
        song_path = AUDIO_DIR / f"{source['id']}-pseudo-song.wav"
        write_wav(song_path, pack["sr"], song)

        # Discovery sees waveform only. Hidden metadata is evaluated afterwards.
        result = discover(song, pack["sr"])
        period_error = abs(result["selected_period_seconds"] - hidden["period_seconds"])
        period_hit = period_error <= TOLERANCE
        body_contains_pair = (
            result["selected_start_seconds"] >= hidden["body_start_seconds"] - TOLERANCE
            and result["selected_end_seconds"] <= hidden["body_end_seconds"] + TOLERANCE
        )
        row = {
            "id": source["id"],
            "license": source["license"],
            "zip_sha256": pack["zip_sha256"],
            "discovery": result,
            "hidden_ground_truth": hidden,
            "period_error_seconds": period_error,
            "period_ratio_to_ground_truth": result["selected_period_seconds"] / hidden["period_seconds"],
            "period_hit_at_70ms": period_hit,
            "pair_inside_repeated_body": body_contains_pair,
            "pair_valid": period_hit and body_contains_pair,
        }
        rows.append(row)
        print(source["id"], json.dumps({
            "period_gt": hidden["period_seconds"],
            "period_found": result["selected_period_seconds"],
            "period_error": period_error,
            "start": result["selected_start_seconds"],
            "end": result["selected_end_seconds"],
            "inside_body": body_contains_pair,
            "score": result["selected_score"],
        }))

    report = {
        "schema": "audio-real-loop-self-discovery/v1",
        "track_count": len(rows),
        "period_hit_at_70ms": sum(r["period_hit_at_70ms"] for r in rows) / len(rows),
        "pair_inside_body_rate": sum(r["pair_inside_repeated_body"] for r in rows) / len(rows),
        "pair_valid_rate": sum(r["pair_valid"] for r in rows) / len(rows),
        "mean_period_error_seconds": sum(r["period_error_seconds"] for r in rows) / len(rows),
        "max_period_error_seconds": max(r["period_error_seconds"] for r in rows),
        "tracks": rows,
        "algorithm": {
            "grid_seconds": GRID_SECONDS,
            "window_frames": WINDOW_FRAMES,
            "min_period_seconds": MIN_PERIOD_SECONDS,
            "uses_bpm": False,
            "uses_beat_grid": False,
            "uses_ground_truth_during_discovery": False,
        },
    }
    (OUT / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("track_count", "period_hit_at_70ms", "pair_inside_body_rate", "pair_valid_rate", "mean_period_error_seconds", "max_period_error_seconds")}, indent=2))


if __name__ == "__main__":
    main()
