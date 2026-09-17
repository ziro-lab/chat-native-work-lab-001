#!/usr/bin/env python3
"""Run Beat This and assert the synthetic rhythm-grid PASS boundary."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
EXPECTED_SMALL0_SHA256 = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"
EXPECTED_SMALL0_SIZE = 8451101


def run(*args: str) -> None:
    subprocess.run([sys.executable, *args], cwd=ROOT, check=True)


def load(name: str) -> dict:
    return json.loads((OUT / name).read_text(encoding="utf-8"))


def main() -> None:
    OUT.mkdir(exist_ok=True)
    run("run_beatthis.py", "--output", str(OUT))
    run("evaluate_grid.py", str(OUT / "predictions.json"), "--output", str(OUT / "report.json"))

    predictions = load("predictions.json")
    report = load("report.json")
    beat = report["aggregate"]["beat"]
    downbeat = report["aggregate"]["downbeat"]
    per_take = report["per_take"]
    runtime = predictions["runtime"]

    assert runtime["beat_this"] == "1.1.0", runtime
    assert runtime["checkpoint_name"] == "beat_this-small0.ckpt", runtime
    assert int(runtime["checkpoint_size"]) == EXPECTED_SMALL0_SIZE, runtime
    assert runtime["checkpoint_sha256"] == EXPECTED_SMALL0_SHA256, runtime
    assert len(predictions["takes"]) == 3

    strong_beat_takes = sum(t["beat"]["f1"] >= 0.90 for t in per_take)
    assert beat["f1"] >= 0.90, beat
    assert downbeat["f1"] >= 0.80, downbeat
    assert strong_beat_takes >= 2, per_take

    evidence = {
        "status": "PASS_AUDIO_BEATTHIS_GRID_V1",
        "runtime": runtime,
        "aggregate": report["aggregate"],
        "per_take": per_take,
        "strong_beat_takes_at_f1_0_90": strong_beat_takes,
        "interpretation": "Beat This small0 recovered the synthetic beat/downbeat grid within the experiment's ±70 ms tolerance strongly enough to replace the oracle rhythm grid in a follow-up loop-ranking experiment.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_AUDIO_BEATTHIS_GRID_V1")


if __name__ == "__main__":
    main()
