#!/usr/bin/env python3
"""Evaluate Beat/Downbeat predictions with one-to-one matching inside a timing tolerance."""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

TOLERANCE_SECONDS = 0.070


def match_events(reference: list[float], estimated: list[float], tolerance: float) -> dict:
    # Globally prefer the smallest timing errors, with each ref/estimate used at most once.
    possible = []
    for ri, r in enumerate(reference):
        for ei, e in enumerate(estimated):
            error = abs(e - r)
            if error <= tolerance:
                possible.append((error, ri, ei))
    possible.sort()
    used_r: set[int] = set()
    used_e: set[int] = set()
    errors: list[float] = []
    for error, ri, ei in possible:
        if ri in used_r or ei in used_e:
            continue
        used_r.add(ri)
        used_e.add(ei)
        errors.append(error)

    tp = len(errors)
    precision = tp / len(estimated) if estimated else 0.0
    recall = tp / len(reference) if reference else 0.0
    f1 = 2.0 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {
        "reference_events": len(reference),
        "estimated_events": len(estimated),
        "matched_events": tp,
        "precision": precision,
        "recall": recall,
        "f1": f1,
        "mean_abs_error_seconds": sum(errors) / len(errors) if errors else None,
        "max_abs_error_seconds": max(errors) if errors else None,
    }


def aggregate(rows: list[dict]) -> dict:
    refs = sum(r["reference_events"] for r in rows)
    ests = sum(r["estimated_events"] for r in rows)
    tp = sum(r["matched_events"] for r in rows)
    precision = tp / ests if ests else 0.0
    recall = tp / refs if refs else 0.0
    f1 = 2.0 * precision * recall / (precision + recall) if precision + recall else 0.0
    weighted_mean_num = sum(
        (r["mean_abs_error_seconds"] or 0.0) * r["matched_events"] for r in rows
    )
    max_errors = [r["max_abs_error_seconds"] for r in rows if r["max_abs_error_seconds"] is not None]
    return {
        "reference_events": refs,
        "estimated_events": ests,
        "matched_events": tp,
        "precision": precision,
        "recall": recall,
        "f1": f1,
        "mean_abs_error_seconds": weighted_mean_num / tp if tp else None,
        "max_abs_error_seconds": max(max_errors) if max_errors else None,
    }


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("predictions", type=Path)
    p.add_argument("--output", type=Path, required=True)
    args = p.parse_args()
    data = json.loads(args.predictions.read_text(encoding="utf-8"))
    fixture = data["fixture"]
    bpm = float(fixture["bpm"])
    beats_per_bar = int(fixture["beats_per_bar"])
    bars = int(fixture["bars_per_loop"])
    beat_interval = 60.0 / bpm
    total_beats = beats_per_bar * bars
    gt_beats = [i * beat_interval for i in range(total_beats)]
    gt_downbeats = [i * beat_interval * beats_per_bar for i in range(bars)]

    per_take = []
    beat_rows = []
    downbeat_rows = []
    for take in data["takes"]:
        beat = match_events(gt_beats, [float(x) for x in take["beats"]], TOLERANCE_SECONDS)
        downbeat = match_events(gt_downbeats, [float(x) for x in take["downbeats"]], TOLERANCE_SECONDS)
        beat_rows.append(beat)
        downbeat_rows.append(downbeat)
        per_take.append({"id": take["id"], "beat": beat, "downbeat": downbeat})

    report = {
        "schema": "audio-beatthis-grid-evaluation/v1",
        "tolerance_seconds": TOLERANCE_SECONDS,
        "ground_truth": {
            "beat_interval_seconds": beat_interval,
            "beat_events_per_take": len(gt_beats),
            "downbeat_events_per_take": len(gt_downbeats),
        },
        "aggregate": {
            "beat": aggregate(beat_rows),
            "downbeat": aggregate(downbeat_rows),
        },
        "per_take": per_take,
    }
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
