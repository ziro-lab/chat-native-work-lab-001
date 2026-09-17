#!/usr/bin/env python3
"""Stress the single-track loop discovery engine across 48 real-audio layouts."""
from __future__ import annotations

import importlib.util
import json
import math
import shutil
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
CORE_PATH = ROOT.parent / "audio-real-loop-self-discovery-001" / "run_self_discovery.py"
OUT = ROOT / "out"
AUDIO_DIR = OUT / "runtime-audio"
TOLERANCE = 0.070

SCENARIOS = [
    {"id":"two-clean",      "repeats":2, "intro_bars":0, "outro_bars":0, "phase_bars":0},
    {"id":"two-framed",     "repeats":2, "intro_bars":1, "outro_bars":1, "phase_bars":1},
    {"id":"two-long-intro", "repeats":2, "intro_bars":2, "outro_bars":0, "phase_bars":1},
    {"id":"two-long-outro", "repeats":2, "intro_bars":0, "outro_bars":2, "phase_bars":1},
    {"id":"three-open",     "repeats":3, "intro_bars":0, "outro_bars":1, "phase_bars":1},
    {"id":"three-framed",   "repeats":3, "intro_bars":2, "outro_bars":2, "phase_bars":0},
    {"id":"four-open",      "repeats":4, "intro_bars":1, "outro_bars":0, "phase_bars":1},
    {"id":"four-framed",    "repeats":4, "intro_bars":1, "outro_bars":1, "phase_bars":0},
]


def load_core():
    spec = importlib.util.spec_from_file_location("self_discovery_core", CORE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("could not load self-discovery core")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def rotate(samples: np.ndarray, frames: int) -> np.ndarray:
    if not len(samples):
        return samples.copy()
    frames %= len(samples)
    if frames == 0:
        return samples.copy()
    return np.concatenate([samples[frames:], samples[:frames]]).astype(np.float32, copy=False)


def perturb_cycle(samples: np.ndarray, seed: int, cycle_index: int) -> np.ndarray:
    rng = np.random.default_rng(seed + cycle_index * 104729)
    gain = np.float32(0.94 + 0.12 * rng.random())
    drive = np.float32(1.00 + 0.12 * rng.random())
    noise_std = np.float32(0.00020 + 0.00035 * rng.random())
    out = np.tanh(samples * gain * drive).astype(np.float32)
    out += rng.normal(0.0, float(noise_std), size=len(out)).astype(np.float32)
    return out


def build_song(core, source: dict, pack: dict, scenario: dict, scenario_index: int) -> tuple[np.ndarray, dict]:
    loop = pack["loop"]
    sr = int(pack["sr"])
    bars = int(source["beats_per_loop"] // 4)
    if source["beats_per_loop"] % 4 or len(loop) % bars:
        raise RuntimeError(f"{source['id']}: invalid bar division")
    bar_frames = len(loop) // bars
    phase_bars = int(scenario["phase_bars"]) % bars
    phase_frames = phase_bars * bar_frames

    body_cycles = []
    for cycle in range(int(scenario["repeats"])):
        base = core.mix_stems(pack["stems"], (scenario_index + cycle) % 3)
        base = rotate(base, phase_frames)
        base = perturb_cycle(base, 20260917 + 1000 * scenario_index + len(source["id"]), cycle)
        body_cycles.append(core.normalize(base, 0.80 + 0.02 * (cycle % 2)))

    intro_bars = int(scenario["intro_bars"])
    outro_bars = int(scenario["outro_bars"])
    intro_len = intro_bars * bar_frames
    outro_len = outro_bars * bar_frames

    sparse_intro = rotate(core.mix_stems(pack["stems"], 3, peak=0.58), phase_frames)
    sparse_outro = rotate(core.mix_stems(pack["stems"], 4, peak=0.58), phase_frames)
    intro = sparse_intro[:intro_len].copy() if intro_len else np.empty(0, dtype=np.float32)
    outro = sparse_outro[-outro_len:].copy() if outro_len else np.empty(0, dtype=np.float32)
    if len(intro):
        intro *= np.linspace(0.0, 1.0, len(intro), dtype=np.float32)
    if len(outro):
        outro *= np.linspace(1.0, 0.0, len(outro), dtype=np.float32)

    song = np.concatenate([intro, *body_cycles, outro]).astype(np.float32)
    song = core.normalize(song, 0.88)
    body_start = len(intro)
    body_end = body_start + len(body_cycles) * len(loop)
    hidden = {
        "sample_rate": sr,
        "fundamental_period_frames": len(loop),
        "fundamental_period_seconds": len(loop) / sr,
        "bars_per_loop": bars,
        "bar_frames": bar_frames,
        "phase_bars": phase_bars,
        "repeat_count": len(body_cycles),
        "body_start_seconds": body_start / sr,
        "body_end_seconds": body_end / sr,
        "song_seconds": len(song) / sr,
    }
    return song, hidden


def discover_general(core, samples: np.ndarray, sr: int) -> dict:
    x, hop_seconds = core.linear_features(samples, sr)
    n = len(x)
    min_lag = max(2, int(math.ceil(core.MIN_PERIOD_SECONDS / hop_seconds)))
    max_lag = n // 2
    if max_lag <= min_lag:
        raise RuntimeError("track too short for generalized recurrence search")
    peaks = []
    for lag in range(min_lag, max_lag + 1):
        score, start = core.recurrence_for_lag(x, lag)
        if start >= 0:
            peaks.append({"lag_frames":lag, "score":score, "start_frame":start})
    ranked = sorted(peaks, key=lambda p: (-p["score"], p["lag_frames"]))
    best = ranked[0]
    runner = next((p for p in ranked[1:] if abs(p["lag_frames"] - best["lag_frames"]) >= 3), ranked[1])
    period = best["lag_frames"] * hop_seconds
    start = best["start_frame"] * hop_seconds
    return {
        "grid_hop_seconds": hop_seconds,
        "selected_period_seconds": period,
        "selected_start_seconds": start,
        "selected_end_seconds": start + period,
        "selected_score": best["score"],
        "runner_up_period_seconds": runner["lag_frames"] * hop_seconds,
        "runner_up_score": runner["score"],
        "top1_minus_runner_up": best["score"] - runner["score"],
        "top_peaks": [
            {"period_seconds":p["lag_frames"]*hop_seconds, "start_seconds":p["start_frame"]*hop_seconds, "score":p["score"]}
            for p in ranked[:8]
        ],
    }


def main() -> None:
    core = load_core()
    if OUT.exists():
        shutil.rmtree(OUT)
    AUDIO_DIR.mkdir(parents=True)
    rows = []
    packs = {}

    for source in core.SOURCES:
        packs[source["id"]] = core.load_pack(source)
        for scenario_index, scenario in enumerate(SCENARIOS):
            song, hidden = build_song(core, source, packs[source["id"]], scenario, scenario_index)
            path = AUDIO_DIR / f"{source['id']}-{scenario['id']}.wav"
            core.write_wav(path, int(packs[source["id"]]["sr"]), song)

            # Discovery sees only waveform + sample rate.
            found = discover_general(core, song, int(packs[source["id"]]["sr"]))
            fundamental = float(hidden["fundamental_period_seconds"])
            selected = float(found["selected_period_seconds"])
            fundamental_error = abs(selected - fundamental)
            fundamental_hit = fundamental_error <= TOLERANCE
            k = max(1, int(round(selected / fundamental)))
            multiple_error = abs(selected - k * fundamental)
            pair_inside = (
                found["selected_start_seconds"] >= hidden["body_start_seconds"] - TOLERANCE
                and found["selected_end_seconds"] <= hidden["body_end_seconds"] + TOLERANCE
            )
            valid_multiple = multiple_error <= TOLERANCE and pair_inside and k <= int(hidden["repeat_count"]) - 1
            rows.append({
                "source_id": source["id"],
                "scenario": scenario,
                "discovery": found,
                "hidden_ground_truth": hidden,
                "fundamental_error_seconds": fundamental_error,
                "fundamental_hit_at_70ms": fundamental_hit,
                "nearest_integer_multiple": k,
                "integer_multiple_error_seconds": multiple_error,
                "pair_inside_repeated_body": pair_inside,
                "valid_integer_multiple_loop": valid_multiple,
            })

    per_source = {}
    per_scenario = {}
    for row in rows:
        s = per_source.setdefault(row["source_id"], {"cases":0,"fundamental_hits":0,"valid_multiple":0})
        s["cases"] += 1
        s["fundamental_hits"] += int(row["fundamental_hit_at_70ms"])
        s["valid_multiple"] += int(row["valid_integer_multiple_loop"])
        qid = row["scenario"]["id"]
        q = per_scenario.setdefault(qid, {"cases":0,"fundamental_hits":0,"valid_multiple":0})
        q["cases"] += 1
        q["fundamental_hits"] += int(row["fundamental_hit_at_70ms"])
        q["valid_multiple"] += int(row["valid_integer_multiple_loop"])
    for bucket in [*per_source.values(), *per_scenario.values()]:
        bucket["fundamental_rate"] = bucket["fundamental_hits"] / bucket["cases"]
        bucket["valid_multiple_rate"] = bucket["valid_multiple"] / bucket["cases"]

    two_repeat = [r for r in rows if int(r["scenario"]["repeats"]) == 2]
    report = {
        "schema": "audio-real-loop-self-discovery-stress/v1",
        "case_count": len(rows),
        "fundamental_hit_at_70ms": sum(r["fundamental_hit_at_70ms"] for r in rows) / len(rows),
        "valid_integer_multiple_rate": sum(r["valid_integer_multiple_loop"] for r in rows) / len(rows),
        "pair_inside_body_rate": sum(r["pair_inside_repeated_body"] for r in rows) / len(rows),
        "two_repeat_valid_rate": sum(r["valid_integer_multiple_loop"] for r in two_repeat) / len(two_repeat),
        "per_source": per_source,
        "per_scenario": per_scenario,
        "cases": rows,
        "algorithm": {
            "uses_bpm": False,
            "uses_beat_grid": False,
            "uses_source_reference": False,
            "uses_hidden_period_during_discovery": False,
            "max_lag": "half observed track",
        },
    }
    (OUT / "report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({
        "case_count": report["case_count"],
        "fundamental_hit_at_70ms": report["fundamental_hit_at_70ms"],
        "valid_integer_multiple_rate": report["valid_integer_multiple_rate"],
        "pair_inside_body_rate": report["pair_inside_body_rate"],
        "two_repeat_valid_rate": report["two_repeat_valid_rate"],
        "per_source": report["per_source"],
        "per_scenario": report["per_scenario"],
    }, indent=2))


if __name__ == "__main__":
    main()
