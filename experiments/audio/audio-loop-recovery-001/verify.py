#!/usr/bin/env python3
"""Machine-checkable assertions for the experiment PASS boundary."""
from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"


def run(*args: str) -> None:
    subprocess.run([sys.executable, *args], cwd=ROOT, check=True)


def pcm_digest(path: Path) -> str:
    with wave.open(str(path), "rb") as wf:
        return hashlib.sha256(wf.readframes(wf.getnframes())).hexdigest()


def main() -> None:
    OUT.mkdir(exist_ok=True)
    run("generate_benchmark.py", "--output", str(OUT))
    run("make_demo_predictions.py", str(OUT / "manifest.json"), "--output-dir", str(OUT))
    run("evaluate.py", str(OUT / "manifest.json"), str(OUT / "predictions-oracle.json"), "--output", str(OUT / "report-oracle.json"))
    run("evaluate.py", str(OUT / "manifest.json"), str(OUT / "predictions-random.json"), "--output", str(OUT / "report-random.json"))

    manifest = json.loads((OUT / "manifest.json").read_text(encoding="utf-8"))
    oracle = json.loads((OUT / "report-oracle.json").read_text(encoding="utf-8"))
    random_report = json.loads((OUT / "report-random.json").read_text(encoding="utf-8"))

    assert len(manifest["takes"]) == 3
    assert len(manifest["queries"]) == 3
    assert all(sum(c["label"] == "positive" for c in q["candidates"]) == 1 for q in manifest["queries"])
    assert all(sum(c["label"] == "hard_negative" for c in q["candidates"]) >= 5 for q in manifest["queries"])

    # Corresponding takes must not be byte-identical; otherwise a trivial exact-match detector would pass.
    digests = [pcm_digest(OUT / t["path"]) for t in manifest["takes"]]
    assert len(set(digests)) == len(digests), digests

    assert oracle["hit_at_1"] == 1.0
    assert oracle["hit_at_3"] == 1.0
    assert oracle["mrr"] == 1.0
    assert oracle["pairwise_ranking_accuracy"] == 1.0
    assert random_report["pairwise_ranking_accuracy"] < oracle["pairwise_ranking_accuracy"]

    evidence = {
        "status": "PASS_AUDIO_LOOP_RECOVERY_FIXTURE_V1",
        "take_pcm_sha256": digests,
        "oracle": oracle,
        "random_baseline": random_report,
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_AUDIO_LOOP_RECOVERY_FIXTURE_V1")


if __name__ == "__main__":
    main()
