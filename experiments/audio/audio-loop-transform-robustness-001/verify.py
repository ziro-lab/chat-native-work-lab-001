#!/usr/bin/env python3
"""Assert transformed-loop robustness of the detected-grid context ranker."""
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
    subprocess.run([sys.executable, "run_robustness.py", "--output", str(OUT)], cwd=ROOT, check=True)
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))

    hashes = [d["audio_sha256"] for d in report["detector"]]
    assert len(hashes) == 6
    assert len(set(hashes)) == 6, hashes
    assert report["checkpoint"]["sha256"] == EXPECTED_SMALL0_SHA256, report["checkpoint"]
    assert report["query_count"] == 24, report
    assert report["candidate_coverage"] >= 0.95, report
    assert report["hit_at_1"] >= 0.80, report
    assert report["hit_at_3"] >= 0.95, report
    assert report["pairwise_ranking_accuracy"] >= 0.90, report

    evidence = {
        "status": "PASS_AUDIO_LOOP_TRANSFORM_ROBUSTNESS_V1",
        "query_count": report["query_count"],
        "covered_queries": report["covered_queries"],
        "candidate_coverage": report["candidate_coverage"],
        "hit_at_1": report["hit_at_1"],
        "hit_at_3": report["hit_at_3"],
        "mrr": report["mrr"],
        "pairwise_ranking_accuracy": report["pairwise_ranking_accuracy"],
        "processed_audio_sha256": hashes,
        "checkpoint": report["checkpoint"],
        "interpretation": "Beat This plus two-bar chroma-context ranking remains above threshold when every take receives a different Ground-Truth-preserving spectral/dynamics/reverb/noise transformation.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_AUDIO_LOOP_TRANSFORM_ROBUSTNESS_V1")


if __name__ == "__main__":
    main()
