#!/usr/bin/env python3
"""Create deterministic oracle and random-baseline prediction files."""
from __future__ import annotations

import argparse
import json
import random
from pathlib import Path


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("manifest", type=Path)
    p.add_argument("--output-dir", type=Path, default=Path("out"))
    args = p.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    args.output_dir.mkdir(parents=True, exist_ok=True)

    oracle = {"schema": "audio-loop-predictions/v1", "queries": []}
    random_pred = {"schema": "audio-loop-predictions/v1", "queries": []}
    rng = random.Random(77123)

    for q in manifest["queries"]:
        oq = {"id": q["id"], "candidates": []}
        rq = {"id": q["id"], "candidates": []}
        for c in q["candidates"]:
            oq["candidates"].append({"id": c["id"], "score": 1.0 if c["label"] == "positive" else 0.0})
            rq["candidates"].append({"id": c["id"], "score": rng.random()})
        oracle["queries"].append(oq)
        random_pred["queries"].append(rq)

    (args.output_dir / "predictions-oracle.json").write_text(json.dumps(oracle, indent=2) + "\n", encoding="utf-8")
    (args.output_dir / "predictions-random.json").write_text(json.dumps(random_pred, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
