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
    subprocess.run([sys.executable, "run_adaptive.py", "--output", str(OUT)], cwd=ROOT, check=True)
    report = json.loads((OUT / "report.json").read_text(encoding="utf-8"))
    easy = report["easy_family"]
    structural = report["structural_family"]

    assert report["margin_threshold"] == 0.05
    assert easy["queries"] == 24, easy
    assert easy["hit_at_1"] == 1.0, easy
    assert easy["stop_at_2bar_rate"] >= 0.95, easy
    assert easy["min_margin_2bar"] >= 0.05, easy
    assert easy["checkpoint"]["sha256"] == EXPECTED_SHA, easy["checkpoint"]

    assert structural["queries"] == 18, structural
    assert structural["hit_at_1"] >= 0.90, structural
    assert structural["reach_8bar_rate"] >= 0.80, structural
    assert structural["checkpoint"]["sha256"] == EXPECTED_SHA, structural["checkpoint"]

    evidence = {
        "status": "PASS_AUDIO_LOOP_ADAPTIVE_CONTEXT_V1",
        "margin_threshold": report["margin_threshold"],
        "easy": {
            "queries": easy["queries"],
            "hit_at_1": easy["hit_at_1"],
            "stop_at_2bar_rate": easy["stop_at_2bar_rate"],
            "min_margin_2bar": easy["min_margin_2bar"],
        },
        "structural": {
            "queries": structural["queries"],
            "hit_at_1": structural["hit_at_1"],
            "selected_context_counts": structural["selected_context_counts"],
            "reach_8bar_rate": structural["reach_8bar_rate"],
        },
        "checkpoint_sha256": EXPECTED_SHA,
        "interpretation": "The provisional score-margin rule stops early on easy transformed material and escalates structurally ambiguous material without using Ground Truth in the context-selection decision.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_AUDIO_LOOP_ADAPTIVE_CONTEXT_V1")


if __name__ == "__main__":
    main()
