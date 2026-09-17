#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"


def main() -> None:
    subprocess.run([sys.executable, "run_dense_phase.py"], cwd=ROOT, check=True)
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))
    rows = report["queries"]
    assert report["query_count"] == 12, report["query_count"]
    assert len(rows) == 12
    assert 0.0 <= report["hit_at_1_at_70ms"] <= 1.0
    assert report["baseline_downbeat_only"]["end_to_end_accuracy"] == 5 / 12
    assert set(report["per_source"]) == {"arcade", "vellum", "anvil", "prism", "rust", "timber"}
    assert all(float(r["grid_hop_seconds"]) <= 0.0400001 for r in rows)
    assert all(int(r["grid_frames"]) >= 32 for r in rows)
    assert all(float(r["nearest_grid_quantization_error_seconds"]) <= 0.0400001 for r in rows)
    assert all(0.0 <= float(r["selected_time_seconds"]) < float(r["duration_seconds"]) for r in rows)

    evidence = {
        "status": "PASS_AUDIO_REAL_LOOP_DENSE_PHASE_EXECUTED_V1",
        "quality_is_diagnostic": True,
        "query_count": report["query_count"],
        "hit_at_1_at_70ms": report["hit_at_1_at_70ms"],
        "max_candidate_grid_quantization_error_seconds": report["max_candidate_grid_quantization_error_seconds"],
        "baseline_downbeat_only": report["baseline_downbeat_only"],
        "per_source": report["per_source"],
        "interpretation": "Dense beat-independent chroma phase search executed on all 12 pinned real-audio variants. Quality is reported diagnostically against the 5/12 downbeat-only baseline.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PASS_AUDIO_REAL_LOOP_DENSE_PHASE_EXECUTED_V1")


if __name__ == "__main__":
    main()
