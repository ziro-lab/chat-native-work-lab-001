#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
EXPECTED_SHA = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"


def main() -> None:
    subprocess.run([sys.executable, "run_benchmark.py", "--output", str(OUT)], cwd=ROOT, check=True)
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))
    m2 = report["metrics_by_context_bars"]["2"]
    m4 = report["metrics_by_context_bars"]["4"]
    m8 = report["metrics_by_context_bars"]["8"]
    checkpoint = report["checkpoint"]

    assert checkpoint["sha256"] == EXPECTED_SHA, checkpoint
    assert m8["query_count"] == 18, m8
    assert m8["covered_queries"] == 18, m8
    assert m8["candidate_coverage"] == 1.0, m8
    assert m8["hit_at_1"] >= 0.90, m8
    assert m8["pairwise_ranking_accuracy"] >= 0.95, m8
    assert m8["hit_at_1"] - m2["hit_at_1"] >= 0.30, (m2, m8)
    assert m8["hit_at_1"] - m4["hit_at_1"] >= 0.15, (m4, m8)
    assert m4["competitive_decoy_4bar_queries"] >= 9, m4

    evidence = {
        "status": "PASS_AUDIO_LOOP_STRUCTURAL_AMBIGUITY_V1",
        "metrics_by_context_bars": report["metrics_by_context_bars"],
        "checkpoint": checkpoint,
        "interpretation": "Nested 2/4-bar structural decoys materially reduce short-context ranking accuracy, while 8-bar context recovers the intended loop position on the controlled fixture.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_AUDIO_LOOP_STRUCTURAL_AMBIGUITY_V1")


if __name__ == "__main__":
    main()
