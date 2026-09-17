#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
EXPECTED_IDS = {"arcade", "vellum", "anvil", "prism", "rust", "timber"}


def main() -> None:
    subprocess.run([sys.executable, "run_self_discovery.py"], cwd=ROOT, check=True)
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))
    rows = report["tracks"]
    assert report["track_count"] == 6
    assert len(rows) == 6
    assert {r["id"] for r in rows} == EXPECTED_IDS
    assert report["algorithm"]["uses_bpm"] is False
    assert report["algorithm"]["uses_beat_grid"] is False
    assert report["algorithm"]["uses_ground_truth_during_discovery"] is False
    for row in rows:
        d = row["discovery"]
        h = row["hidden_ground_truth"]
        assert len(row["zip_sha256"]) == 64
        assert 0.0 < float(d["selected_period_seconds"]) < float(h["song_seconds"])
        assert 0.0 <= float(d["selected_start_seconds"]) < float(d["selected_end_seconds"]) <= float(h["song_seconds"])
        assert float(d["grid_hop_seconds"]) <= 0.0401
        assert len(d["top_peaks"]) >= 2
        assert float(h["body_start_seconds"]) < float(h["body_end_seconds"])
        assert float(h["period_seconds"]) > 0
    for key in ("period_hit_at_70ms", "pair_inside_body_rate", "pair_valid_rate"):
        assert 0.0 <= float(report[key]) <= 1.0

    evidence = {
        "status": "PASS_AUDIO_REAL_LOOP_SELF_DISCOVERY_EXECUTED_V1",
        "quality_is_diagnostic": True,
        "track_count": report["track_count"],
        "period_hit_at_70ms": report["period_hit_at_70ms"],
        "pair_inside_body_rate": report["pair_inside_body_rate"],
        "pair_valid_rate": report["pair_valid_rate"],
        "mean_period_error_seconds": report["mean_period_error_seconds"],
        "max_period_error_seconds": report["max_period_error_seconds"],
        "per_track": [
            {
                "id": r["id"],
                "ground_truth_period_seconds": r["hidden_ground_truth"]["period_seconds"],
                "selected_period_seconds": r["discovery"]["selected_period_seconds"],
                "period_error_seconds": r["period_error_seconds"],
                "period_ratio_to_ground_truth": r["period_ratio_to_ground_truth"],
                "period_hit_at_70ms": r["period_hit_at_70ms"],
                "pair_inside_repeated_body": r["pair_inside_repeated_body"],
                "pair_valid": r["pair_valid"],
                "selected_start_seconds": r["discovery"]["selected_start_seconds"],
                "selected_end_seconds": r["discovery"]["selected_end_seconds"],
                "selected_score": r["discovery"]["selected_score"],
                "runner_up_period_seconds": r["discovery"]["runner_up_period_seconds"],
                "top1_minus_runner_up": r["discovery"]["top1_minus_runner_up"],
            }
            for r in rows
        ],
        "interpretation": "Single-track recurrence discovery executed without BPM, beat grid, source-reference audio or hidden loop length. Quality remains diagnostic on this first run.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PASS_AUDIO_REAL_LOOP_SELF_DISCOVERY_EXECUTED_V1")


if __name__ == "__main__":
    main()
