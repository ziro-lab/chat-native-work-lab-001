#!/usr/bin/env python3
"""Assert detected-grid loop ranking quality."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
EXPECTED_SMALL0_SHA256 = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"


def main() -> None:
    OUT.mkdir(exist_ok=True)
    subprocess.run([sys.executable, "run_detected_ranking.py", "--output", str(OUT)], cwd=ROOT, check=True)
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))

    assert report["checkpoint"]["sha256"] == EXPECTED_SMALL0_SHA256, report["checkpoint"]
    assert report["query_count"] == 24, report
    assert report["covered_queries"] == 24, report
    assert report["candidate_coverage"] == 1.0, report
    assert report["hit_at_1"] >= 0.90, report
    assert report["hit_at_3"] >= 0.95, report
    assert report["pairwise_ranking_accuracy"] >= 0.95, report

    # The synthetic loop is exactly eight bars, so after excluding the terminal boundary
    # every detector record should expose the expected eight in-file downbeats.
    assert all(len(v["downbeats"]) == 8 for v in report["detector"]), report["detector"]

    evidence = {
        "status": "PASS_AUDIO_BEATTHIS_LOOP_RANKING_V1",
        "query_count": report["query_count"],
        "covered_queries": report["covered_queries"],
        "candidate_coverage": report["candidate_coverage"],
        "hit_at_1": report["hit_at_1"],
        "hit_at_3": report["hit_at_3"],
        "mrr": report["mrr"],
        "pairwise_ranking_accuracy": report["pairwise_ranking_accuracy"],
        "checkpoint": report["checkpoint"],
        "interpretation": "Beat This detected downbeats can replace the oracle candidate grid on this controlled phase-recovery fixture without degrading the two-bar chroma-context ranking below the experiment threshold.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_AUDIO_BEATTHIS_LOOP_RANKING_V1")


if __name__ == "__main__":
    main()
