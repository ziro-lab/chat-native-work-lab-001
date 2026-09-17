#!/usr/bin/env python3
"""Generate the rotated fixture, score baselines, and assert the experiment PASS boundary."""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
FOUNDATION = ROOT.parent / "audio-loop-recovery-001"
OUT = ROOT / "out"


def run(*args: str) -> None:
    subprocess.run([sys.executable, *args], cwd=ROOT, check=True)


def load(name: str) -> dict:
    return json.loads((OUT / name).read_text(encoding="utf-8"))


def main() -> None:
    OUT.mkdir(exist_ok=True)
    run("build_phase_fixture.py", "--output", str(OUT))
    manifest = load("manifest.json")

    run("score_dsp.py", str(OUT / "manifest.json"), "--mode", "seam", "--output", str(OUT / "predictions-seam.json"))
    run("score_dsp.py", str(OUT / "manifest.json"), "--mode", "context", "--output", str(OUT / "predictions-context.json"))

    # Shared evaluator and deterministic random baseline from the benchmark foundation.
    run(str(FOUNDATION / "make_demo_predictions.py"), str(OUT / "manifest.json"), "--output-dir", str(OUT))
    run(str(FOUNDATION / "evaluate.py"), str(OUT / "manifest.json"), str(OUT / "predictions-seam.json"), "--output", str(OUT / "report-seam.json"))
    run(str(FOUNDATION / "evaluate.py"), str(OUT / "manifest.json"), str(OUT / "predictions-context.json"), "--output", str(OUT / "report-context.json"))
    run(str(FOUNDATION / "evaluate.py"), str(OUT / "manifest.json"), str(OUT / "predictions-random.json"), "--output", str(OUT / "report-random.json"))

    seam = load("report-seam.json")
    context = load("report-context.json")
    random_report = load("report-random.json")

    assert len(manifest["variants"]) == 6
    assert len(manifest["queries"]) == 24
    assert len({v["sha256"] for v in manifest["variants"]}) == 6
    positive_bars = {int(q["positive_physical_bar"]) for q in manifest["queries"]}
    assert len(positive_bars) >= 6, positive_bars
    assert all(sum(c["label"] == "positive" for c in q["candidates"]) == 1 for q in manifest["queries"])
    assert all(len(q["candidates"]) == 9 for q in manifest["queries"])

    assert context["hit_at_1"] >= 0.90, context
    assert context["pairwise_ranking_accuracy"] >= 0.95, context
    assert context["hit_at_1"] > seam["hit_at_1"], (context, seam)
    assert context["pairwise_ranking_accuracy"] > seam["pairwise_ranking_accuracy"], (context, seam)
    assert context["pairwise_ranking_accuracy"] > random_report["pairwise_ranking_accuracy"], (context, random_report)

    evidence = {
        "status": "PASS_AUDIO_LOOP_DSP_BASELINE_V1",
        "variants": len(manifest["variants"]),
        "queries": len(manifest["queries"]),
        "positive_physical_bars": sorted(positive_bars),
        "seam_only": seam,
        "context_recurrence": context,
        "random_baseline": random_report,
        "interpretation": "Two-bar beat-synchronous chroma context materially outperforms single-seam tonal similarity on this synthetic phase-recovery task.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_AUDIO_LOOP_DSP_BASELINE_V1")


if __name__ == "__main__":
    main()
