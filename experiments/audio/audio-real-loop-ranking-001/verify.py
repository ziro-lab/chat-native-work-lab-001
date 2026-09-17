#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
EXPECTED_MODEL_SHA = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"
EXPECTED_ZIPS = {
    "arcade": "9a75c3760957401c66554fecca4140d77c3a6a66cd173e890009558970af951a",
    "vellum": "bd670a72dbdf8e97a804eb6a9181726df1f7d9c3c6095deb283225a3bc0e9ab1",
    "anvil": "ac1654cc228e5f776f3458c310cc1725743cb6b3af05816dc7e771cd25fbea51",
    "prism": "38483c451b674148ee10d980aca8a00ab70eb815a82684db3879af72c5fc645d",
    "rust": "dd2f6e2f59289a84b952aa85e298bfeeec8d3fc2215003f85d20baebb7328f12",
    "timber": "f0efbb99ffeeb0f4e61d04c518fac792ca2afc228cab495e1f7bf62cfc14a3af",
}


def main() -> None:
    subprocess.run([sys.executable, "run_real_benchmark.py"], cwd=ROOT, check=True)
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))
    assert report["query_count"] == 12, report["query_count"]
    assert len(report["sources"]) == 6
    assert len(report["queries"]) == 12
    assert report["checkpoint"]["sha256"] == EXPECTED_MODEL_SHA, report["checkpoint"]
    assert 0.0 <= report["candidate_coverage"] <= 1.0
    assert 0.0 <= report["covered_hit_at_1"] <= 1.0
    assert 0.0 <= report["end_to_end_accuracy"] <= 1.0
    assert report["evaluated_queries"] + report["abstained_queries"] == 12

    for source in report["sources"]:
        assert source["id"] in EXPECTED_ZIPS, source["id"]
        assert source["zip_sha256"] == EXPECTED_ZIPS[source["id"]], source
        assert len(source["targets"]) == 2, source
        assert source["frames"] > 0 and source["sample_rate"] > 0, source
        assert source["stem_names"], source
        # Published BPM and exact file length should identify an integer beat-count loop.
        assert abs(float(source["beat_count_from_duration_and_tempo"]) - int(source["beats_per_loop"])) < 0.01, source

    statuses = {q["result"]["status"] for q in report["queries"]}
    assert statuses <= {"evaluated", "abstained"}, statuses

    evidence = {
        "status": "PASS_AUDIO_REAL_LOOP_RANKING_EXECUTED_V1",
        "quality_is_diagnostic": True,
        "query_count": report["query_count"],
        "evaluated_queries": report["evaluated_queries"],
        "abstained_queries": report["abstained_queries"],
        "candidate_coverage": report["candidate_coverage"],
        "covered_hit_at_1": report["covered_hit_at_1"],
        "end_to_end_accuracy": report["end_to_end_accuracy"],
        "selected_context_counts": report["selected_context_counts"],
        "per_source": report["per_source"],
        "checkpoint_sha256": report["checkpoint"]["sha256"],
        "interpretation": "The pinned real-audio benchmark executed end-to-end. Quality metrics are diagnostic and are not part of this experiment's PASS boundary.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PASS_AUDIO_REAL_LOOP_RANKING_EXECUTED_V1")


if __name__ == "__main__":
    main()
