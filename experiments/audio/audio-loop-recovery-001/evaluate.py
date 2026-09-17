#!/usr/bin/env python3
"""Evaluate ranked loop candidates against benchmark ground truth."""
from __future__ import annotations

import argparse
import json
from pathlib import Path


def evaluate(manifest: dict, predictions: dict) -> dict:
    gt = {q["id"]: q for q in manifest["queries"]}
    pred = {q["id"]: q for q in predictions["queries"]}
    if set(gt) != set(pred):
        missing = sorted(set(gt) - set(pred))
        extra = sorted(set(pred) - set(gt))
        raise ValueError(f"query mismatch: missing={missing}, extra={extra}")

    hit1 = hit3 = 0
    reciprocal_rank = 0.0
    pairwise_ok = pairwise_total = 0
    details = []

    for qid, q in gt.items():
        labels = {c["id"]: c["label"] for c in q["candidates"]}
        scored = pred[qid]["candidates"]
        if {c["id"] for c in scored} != set(labels):
            raise ValueError(f"candidate mismatch in {qid}")
        ranked = sorted(scored, key=lambda x: (-float(x["score"]), x["id"]))
        positive_ids = {cid for cid, label in labels.items() if label == "positive"}
        rank = next(i + 1 for i, c in enumerate(ranked) if c["id"] in positive_ids)
        hit1 += rank <= 1
        hit3 += rank <= 3
        reciprocal_rank += 1.0 / rank

        pos_scores = [float(c["score"]) for c in scored if c["id"] in positive_ids]
        neg_scores = [float(c["score"]) for c in scored if c["id"] not in positive_ids]
        for ps in pos_scores:
            for ns in neg_scores:
                pairwise_total += 1
                if ps > ns:
                    pairwise_ok += 1
                elif ps == ns:
                    pairwise_ok += 0.5

        details.append({"id": qid, "positive_rank": rank, "top_candidate": ranked[0]["id"]})

    n = len(gt)
    return {
        "queries": n,
        "hit_at_1": hit1 / n,
        "hit_at_3": hit3 / n,
        "mrr": reciprocal_rank / n,
        "pairwise_ranking_accuracy": pairwise_ok / pairwise_total if pairwise_total else 0.0,
        "details": details,
    }


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("manifest", type=Path)
    p.add_argument("predictions", type=Path)
    p.add_argument("--output", type=Path)
    args = p.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    predictions = json.loads(args.predictions.read_text(encoding="utf-8"))
    report = evaluate(manifest, predictions)
    text = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    print(text, end="")


if __name__ == "__main__":
    main()
