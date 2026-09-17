#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"


def main() -> None:
    subprocess.run([sys.executable, "run_stress.py"], cwd=ROOT, check=True)
    subprocess.run([sys.executable, "apply_feature_center_timestamp.py"], cwd=ROOT, check=True)
    report = json.loads((OUT / "report-centered.json").read_text(encoding="utf-8"))
    raw = report["raw_metrics_before_timestamp_fix"]

    assert report["case_count"] == 48, report["case_count"]
    assert report["algorithm"]["uses_bpm"] is False
    assert report["algorithm"]["uses_beat_grid"] is False
    assert report["algorithm"]["uses_source_reference"] is False
    assert report["algorithm"]["uses_hidden_period_during_discovery"] is False
    assert report["timestamp_semantics"]["score_or_lag_changed"] is False

    # Preserve the failed-run evidence: only endpoint timestamp semantics may improve.
    assert raw["fundamental_hit_at_70ms"] == report["fundamental_hit_at_70ms"], (raw, report)
    assert raw["valid_integer_multiple_rate"] == 0.75, raw
    assert raw["pair_inside_body_rate"] == 0.75, raw

    # Original fixed thresholds are intentionally unchanged.
    assert report["fundamental_hit_at_70ms"] >= 0.80, report["fundamental_hit_at_70ms"]
    assert report["valid_integer_multiple_rate"] >= 0.95, report["valid_integer_multiple_rate"]
    assert report["two_repeat_valid_rate"] >= 0.90, report["two_repeat_valid_rate"]
    assert all(v["valid_multiple_rate"] >= 0.875 for v in report["per_source"].values()), report["per_source"]

    for row in report["cases"]:
        d = row["discovery"]
        h = row["hidden_ground_truth"]
        assert float(d["grid_hop_seconds"]) <= 0.0401
        assert float(d["feature_window_center_offset_seconds"]) > 0
        assert 0.0 < float(d["selected_period_seconds"]) <= float(h["song_seconds"]) / 2 + 0.041
        assert 0.0 <= float(d["selected_start_seconds"]) < float(d["selected_end_seconds"]) <= float(h["song_seconds"]) + 0.10
        assert len(d["top_peaks"]) >= 2

    evidence = {
        "status": "PASS_AUDIO_REAL_LOOP_SELF_DISCOVERY_STRESS_CENTERED_V1",
        "case_count": report["case_count"],
        "raw_metrics_before_timestamp_fix": raw,
        "fundamental_hit_at_70ms": report["fundamental_hit_at_70ms"],
        "valid_integer_multiple_rate": report["valid_integer_multiple_rate"],
        "pair_inside_body_rate": report["pair_inside_body_rate"],
        "two_repeat_valid_rate": report["two_repeat_valid_rate"],
        "per_source": report["per_source"],
        "per_scenario": report["per_scenario"],
        "timestamp_semantics": report["timestamp_semantics"],
        "interpretation": "The failed stress run exposed a coordinate-semantics bug: recurrence features were timestamped at STFT window start instead of window center. The correction changes no score, selected lag or fixed quality threshold; it only maps the already-selected feature frame to its correct physical time.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PASS_AUDIO_REAL_LOOP_SELF_DISCOVERY_STRESS_CENTERED_V1")


if __name__ == "__main__":
    main()
