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
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))
    assert report["case_count"] == 48, report["case_count"]
    assert report["algorithm"]["uses_bpm"] is False
    assert report["algorithm"]["uses_beat_grid"] is False
    assert report["algorithm"]["uses_source_reference"] is False
    assert report["algorithm"]["uses_hidden_period_during_discovery"] is False
    assert report["fundamental_hit_at_70ms"] >= 0.80, report["fundamental_hit_at_70ms"]
    assert report["valid_integer_multiple_rate"] >= 0.95, report["valid_integer_multiple_rate"]
    assert report["two_repeat_valid_rate"] >= 0.90, report["two_repeat_valid_rate"]
    assert all(v["valid_multiple_rate"] >= 0.875 for v in report["per_source"].values()), report["per_source"]
    for row in report["cases"]:
        d = row["discovery"]
        h = row["hidden_ground_truth"]
        assert float(d["grid_hop_seconds"]) <= 0.0401
        assert 0.0 < float(d["selected_period_seconds"]) <= float(h["song_seconds"]) / 2 + 0.041
        assert 0.0 <= float(d["selected_start_seconds"]) < float(d["selected_end_seconds"]) <= float(h["song_seconds"]) + 0.041
        assert len(d["top_peaks"]) >= 2

    evidence = {
        "status": "PASS_AUDIO_REAL_LOOP_SELF_DISCOVERY_STRESS_V1",
        "case_count": report["case_count"],
        "fundamental_hit_at_70ms": report["fundamental_hit_at_70ms"],
        "valid_integer_multiple_rate": report["valid_integer_multiple_rate"],
        "pair_inside_body_rate": report["pair_inside_body_rate"],
        "two_repeat_valid_rate": report["two_repeat_valid_rate"],
        "per_source": report["per_source"],
        "per_scenario": report["per_scenario"],
        "interpretation": "The lightweight single-track recurrence detector was stress-tested across 48 real-audio layouts with fixed pre-run thresholds. Integer multiples of the fundamental are accepted only when both endpoints remain inside the repeat body.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PASS_AUDIO_REAL_LOOP_SELF_DISCOVERY_STRESS_V1")


if __name__ == "__main__":
    main()
