#!/usr/bin/env python3
"""Correct recurrence feature timestamps from STFT window start to window center.

This does not change period scores, period ranking, or selected lag. It only fixes the
semantic time attached to the already-selected analysis frame.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
WINDOW_FRAMES = 8192
TOLERANCE = 0.070


def summarize(rows: list[dict]) -> tuple[dict, dict, float, float, float]:
    per_source: dict[str, dict] = {}
    per_scenario: dict[str, dict] = {}
    for row in rows:
        source = per_source.setdefault(row["source_id"], {"cases":0,"fundamental_hits":0,"valid_multiple":0})
        source["cases"] += 1
        source["fundamental_hits"] += int(row["fundamental_hit_at_70ms"])
        source["valid_multiple"] += int(row["valid_integer_multiple_loop"])
        sid = row["scenario"]["id"]
        scenario = per_scenario.setdefault(sid, {"cases":0,"fundamental_hits":0,"valid_multiple":0})
        scenario["cases"] += 1
        scenario["fundamental_hits"] += int(row["fundamental_hit_at_70ms"])
        scenario["valid_multiple"] += int(row["valid_integer_multiple_loop"])
    for bucket in [*per_source.values(), *per_scenario.values()]:
        bucket["fundamental_rate"] = bucket["fundamental_hits"] / bucket["cases"]
        bucket["valid_multiple_rate"] = bucket["valid_multiple"] / bucket["cases"]
    fundamental = sum(r["fundamental_hit_at_70ms"] for r in rows) / len(rows)
    valid = sum(r["valid_integer_multiple_loop"] for r in rows) / len(rows)
    inside = sum(r["pair_inside_repeated_body"] for r in rows) / len(rows)
    return per_source, per_scenario, fundamental, valid, inside


def main() -> None:
    raw_path = OUT / "report.json"
    report = json.loads(raw_path.read_text(encoding="utf-8"))
    raw_metrics = {
        "fundamental_hit_at_70ms": report["fundamental_hit_at_70ms"],
        "valid_integer_multiple_rate": report["valid_integer_multiple_rate"],
        "pair_inside_body_rate": report["pair_inside_body_rate"],
        "two_repeat_valid_rate": report["two_repeat_valid_rate"],
        "per_source": report["per_source"],
        "per_scenario": report["per_scenario"],
    }

    rows = report["cases"]
    for row in rows:
        d = row["discovery"]
        h = row["hidden_ground_truth"]
        sr = float(h["sample_rate"])
        offset = WINDOW_FRAMES / (2.0 * sr)
        d["raw_selected_start_seconds"] = d["selected_start_seconds"]
        d["raw_selected_end_seconds"] = d["selected_end_seconds"]
        d["feature_window_center_offset_seconds"] = offset
        d["selected_start_seconds"] = float(d["selected_start_seconds"]) + offset
        d["selected_end_seconds"] = float(d["selected_end_seconds"]) + offset
        for peak in d.get("top_peaks", []):
            peak["raw_start_seconds"] = peak["start_seconds"]
            peak["start_seconds"] = float(peak["start_seconds"]) + offset

        pair_inside = (
            d["selected_start_seconds"] >= float(h["body_start_seconds"]) - TOLERANCE
            and d["selected_end_seconds"] <= float(h["body_end_seconds"]) + TOLERANCE
        )
        fundamental = float(h["fundamental_period_seconds"])
        selected = float(d["selected_period_seconds"])
        k = max(1, int(round(selected / fundamental)))
        multiple_error = abs(selected - k * fundamental)
        valid = multiple_error <= TOLERANCE and pair_inside and k <= int(h["repeat_count"]) - 1
        row["pair_inside_repeated_body"] = pair_inside
        row["valid_integer_multiple_loop"] = valid
        row["nearest_integer_multiple"] = k
        row["integer_multiple_error_seconds"] = multiple_error

    per_source, per_scenario, fundamental, valid, inside = summarize(rows)
    two_repeat = [r for r in rows if int(r["scenario"]["repeats"]) == 2]
    report["schema"] = "audio-real-loop-self-discovery-stress-centered/v1"
    report["timestamp_semantics"] = {
        "raw": "STFT window start",
        "corrected": "STFT window center",
        "window_frames": WINDOW_FRAMES,
        "score_or_lag_changed": False,
    }
    report["raw_metrics_before_timestamp_fix"] = raw_metrics
    report["fundamental_hit_at_70ms"] = fundamental
    report["valid_integer_multiple_rate"] = valid
    report["pair_inside_body_rate"] = inside
    report["two_repeat_valid_rate"] = sum(r["valid_integer_multiple_loop"] for r in two_repeat) / len(two_repeat)
    report["per_source"] = per_source
    report["per_scenario"] = per_scenario

    corrected = OUT / "report-centered.json"
    corrected.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({
        "raw_valid_integer_multiple_rate": raw_metrics["valid_integer_multiple_rate"],
        "corrected_valid_integer_multiple_rate": report["valid_integer_multiple_rate"],
        "corrected_pair_inside_body_rate": report["pair_inside_body_rate"],
        "corrected_two_repeat_valid_rate": report["two_repeat_valid_rate"],
        "fundamental_hit_at_70ms": report["fundamental_hit_at_70ms"],
        "per_source": report["per_source"],
    }, indent=2))


if __name__ == "__main__":
    main()
