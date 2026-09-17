#!/usr/bin/env python3
"""Validate a Top1-margin driven 2 -> 4 -> 8 bar context policy."""
from __future__ import annotations

import argparse
import importlib.util
import json
import shutil
import subprocess
import sys
from pathlib import Path

from beat_this.inference import File2Beats

ROOT = Path(__file__).resolve().parent
AUDIO_ROOT = ROOT.parent
TRANSFORM_RUNNER = AUDIO_ROOT / "audio-loop-transform-robustness-001" / "run_robustness.py"
STRUCT_DIR = AUDIO_ROOT / "audio-loop-structural-ambiguity-001"
STRUCT_RUNNER = STRUCT_DIR / "run_benchmark.py"
STRUCT_GENERATOR = STRUCT_DIR / "generate_fixture.py"
MARGIN_THRESHOLD = 0.05
CONTEXT_BARS = (2, 4, 8)
EXPECTED_SMALL0_SHA256 = "6074be2c4d490c5f6101fcc374a1ec72ae93456e23bb6019783b849f5dc7d47b"


def load_struct_module():
    spec = importlib.util.spec_from_file_location("structural_benchmark", STRUCT_RUNNER)
    if spec is None or spec.loader is None:
        raise RuntimeError("could not load structural benchmark module")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def top_margin(candidates: list[dict], score_key: str) -> tuple[float, dict, dict]:
    ranked = sorted(candidates, key=lambda c: (-float(c[score_key]), float(c["time_seconds"])))
    if len(ranked) < 2:
        raise RuntimeError("need at least two candidates for margin policy")
    return float(ranked[0][score_key]) - float(ranked[1][score_key]), ranked[0], ranked[1]


def run_easy_family(out: Path) -> dict:
    simple_out = out / "easy"
    subprocess.run([sys.executable, str(TRANSFORM_RUNNER), "--output", str(simple_out)], check=True)
    report = json.loads((simple_out / "report.json").read_text(encoding="utf-8"))
    rows = []
    for query in report["queries"]:
        margin, first, second = top_margin(query["candidates"], "score")
        rows.append(
            {
                "id": query["id"],
                "margin_2bar": margin,
                "stops_at_2bar": margin >= MARGIN_THRESHOLD,
                "top_candidate": first["id"],
                "runner_up": second["id"],
                "correct": first["label"] == "positive",
            }
        )
    return {
        "queries": len(rows),
        "hit_at_1": sum(r["correct"] for r in rows) / len(rows),
        "stop_at_2bar_rate": sum(r["stops_at_2bar"] for r in rows) / len(rows),
        "min_margin_2bar": min(r["margin_2bar"] for r in rows),
        "rows": rows,
        "checkpoint": report["checkpoint"],
    }


def run_structural_family(out: Path) -> dict:
    struct = load_struct_module()
    fixture = out / "structural-fixture"
    if fixture.exists():
        shutil.rmtree(fixture)
    fixture.mkdir(parents=True)
    subprocess.run([sys.executable, str(STRUCT_GENERATOR), "--output", str(fixture)], check=True)
    manifest = json.loads((fixture / "manifest.json").read_text(encoding="utf-8"))
    bar_seconds = float(manifest["generator"]["bar_seconds"])
    duration = float(manifest["generator"]["bars"]) * bar_seconds

    model = File2Beats(checkpoint_path="small0", device="cpu", dbn=False)
    grids = {}
    for item in [*manifest["sources"], *manifest["targets"]]:
        wav = fixture / item["path"]
        beats_raw, downbeats_raw = model(wav)
        beats = struct.clean_inside(beats_raw, duration)
        downbeats = struct.clean_inside(downbeats_raw, duration)
        grids[item["id"]] = struct.build_grid(wav, beats, downbeats)

    targets = {x["id"]: x for x in manifest["targets"]}
    rows = []
    selected_counts = {str(b): 0 for b in CONTEXT_BARS}

    for query in manifest["queries"]:
        source = grids[query["source"]]
        target = grids[query["target"]]
        source_anchor = source["downbeats"][0]
        source_contexts = {
            bars: struct.context_at(source, source_anchor, bars * struct.BEATS_PER_BAR)
            for bars in CONTEXT_BARS
        }
        candidates = []
        for i, t in enumerate(target["downbeats"]):
            candidate = {"id": f"{query['id']}:db{i+1}", "time_seconds": float(t)}
            for bars in CONTEXT_BARS:
                candidate[f"score_{bars}bar"] = struct.context_similarity(
                    source_contexts[bars],
                    struct.context_at(target, t, bars * struct.BEATS_PER_BAR),
                )
            candidates.append(candidate)

        # Context selection is based only on candidate score margin.
        decisions = []
        selected_bars = 8
        selected_top = None
        for bars in CONTEXT_BARS:
            margin, first, second = top_margin(candidates, f"score_{bars}bar")
            decisions.append(
                {
                    "context_bars": bars,
                    "top1_margin": margin,
                    "top_candidate": first["id"],
                    "runner_up": second["id"],
                }
            )
            if margin >= MARGIN_THRESHOLD or bars == 8:
                selected_bars = bars
                selected_top = first
                break
        assert selected_top is not None
        selected_counts[str(selected_bars)] += 1

        # Hidden Ground Truth is consulted only after the policy has stopped.
        meta = targets[query["target"]]
        positive_time = (int(meta["hidden_positive_physical_bar"]) - 1) * bar_seconds
        correct = abs(float(selected_top["time_seconds"]) - positive_time) <= struct.TOLERANCE
        rows.append(
            {
                "id": query["id"],
                "selected_context_bars": selected_bars,
                "selected_candidate": selected_top["id"],
                "selected_time_seconds": selected_top["time_seconds"],
                "correct": correct,
                "decisions": decisions,
            }
        )

    checkpoint = struct.locate_checkpoint()
    checkpoint_info = {
        "name": checkpoint.name,
        "size": checkpoint.stat().st_size,
        "sha256": struct.sha256(checkpoint),
        "expected_sha256": EXPECTED_SMALL0_SHA256,
    }
    return {
        "queries": len(rows),
        "hit_at_1": sum(r["correct"] for r in rows) / len(rows),
        "selected_context_counts": selected_counts,
        "reach_8bar_rate": selected_counts["8"] / len(rows),
        "rows": rows,
        "checkpoint": checkpoint_info,
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--output", type=Path, default=ROOT / "out")
    args = ap.parse_args()
    out = args.output.resolve()
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)

    easy = run_easy_family(out)
    structural = run_structural_family(out)
    report = {
        "schema": "audio-loop-adaptive-context/v1",
        "margin_threshold": MARGIN_THRESHOLD,
        "easy_family": easy,
        "structural_family": structural,
        "interpretation": "A fixed score-margin policy keeps transformed/easy queries at 2 bars while escalating structurally ambiguous queries to longer context without using Ground Truth for the decision.",
    }
    (out / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "margin_threshold": MARGIN_THRESHOLD,
        "easy_hit_at_1": easy["hit_at_1"],
        "easy_stop_at_2bar_rate": easy["stop_at_2bar_rate"],
        "structural_hit_at_1": structural["hit_at_1"],
        "structural_selected_context_counts": structural["selected_context_counts"],
    }, indent=2))


if __name__ == "__main__":
    main()
