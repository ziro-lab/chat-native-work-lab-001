from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path

ACCEPTABLE = {"good", "acceptable"}
VALID = ACCEPTABLE | {"minor", "reject"}
ISSUES = {"loop", "seam", "flow", "ending", "other"}


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("manifest", type=Path)
    ap.add_argument("ratings", type=Path, nargs="+")
    ap.add_argument("--output", type=Path)
    args = ap.parse_args()

    manifest = load(args.manifest)
    task_map = {t["id"]: t for t in manifest["tasks"]}
    mapping = {
        (t["id"], c["blind_id"]): c
        for t in manifest["tasks"]
        for c in t["candidates"]
    }

    rows = []
    malformed = []
    for rating_path in args.ratings:
        payload = load(rating_path)
        rater = payload.get("rater") or rating_path.stem
        for task in payload.get("tasks", []):
            tid = task.get("id")
            if tid not in task_map:
                malformed.append({"file": str(rating_path), "reason": "unknown_task", "id": tid})
                continue
            candidate_rows = []
            for c in task.get("candidates", []):
                bid = c.get("blind_id")
                key = (tid, bid)
                overall = c.get("overall")
                if key not in mapping or overall not in VALID:
                    continue
                issues = sorted(set(c.get("issues") or []) & ISSUES)
                candidate_rows.append({
                    "blind_id": bid,
                    "rank": mapping[key]["algorithm_rank"],
                    "overall": overall,
                    "acceptable": overall in ACCEPTABLE,
                    "issues": issues,
                })
            best = task.get("best")
            best_rank = mapping[(tid, best)]["algorithm_rank"] if (tid, best) in mapping else None
            rows.append({
                "rater": rater,
                "task": tid,
                "source": task_map[tid]["source_id"],
                "target_kind": task_map[tid]["target_kind"],
                "candidate_rows": candidate_rows,
                "best_rank": best_rank,
            })

    complete = [r for r in rows if len(r["candidate_rows"]) == len(task_map[r["task"]]["candidates"])]
    top1_ok = 0
    top3_ok = 0
    no_ok = 0
    best_ranks = Counter()
    issue_counts = Counter()
    issue_den = 0
    by_kind = defaultdict(lambda: {"n": 0, "top1": 0, "top3": 0, "none": 0})

    for row in complete:
        by_rank = {c["rank"]: c for c in row["candidate_rows"]}
        t1 = bool(by_rank.get(1) and by_rank[1]["acceptable"])
        t3 = any(c["acceptable"] for c in row["candidate_rows"])
        top1_ok += t1
        top3_ok += t3
        no_ok += not t3
        kind = by_kind[row["target_kind"]]
        kind["n"] += 1
        kind["top1"] += t1
        kind["top3"] += t3
        kind["none"] += not t3
        if row["best_rank"] is not None:
            best_ranks[row["best_rank"]] += 1
        for c in row["candidate_rows"]:
            if not c["acceptable"]:
                issue_den += 1
                issue_counts.update(c["issues"])

    n = len(complete)
    def rate(x, d):
        return (x / d) if d else None

    report = {
        "schema": "musicfit-human-listening-report/v1",
        "rating_files": [str(p) for p in args.ratings],
        "task_ratings_seen": len(rows),
        "complete_task_ratings": n,
        "expected_tasks_per_rater": len(task_map),
        "top1_acceptable_rate": rate(top1_ok, n),
        "top3_any_acceptable_rate": rate(top3_ok, n),
        "no_acceptable_rate": rate(no_ok, n),
        "best_candidate_rank_counts": dict(sorted(best_ranks.items())),
        "issue_counts_on_nonacceptable_candidates": dict(issue_counts.most_common()),
        "nonacceptable_candidate_issue_denominator": issue_den,
        "by_target_kind": {
            key: {
                "complete_tasks": v["n"],
                "top1_acceptable_rate": rate(v["top1"], v["n"]),
                "top3_any_acceptable_rate": rate(v["top3"], v["n"]),
                "no_acceptable_rate": rate(v["none"], v["n"]),
            }
            for key, v in sorted(by_kind.items())
        },
        "malformed_or_ignored": malformed,
        "interpretation": (
            "Human ratings of generated Phase-4 candidates. With one rater these are "
            "that listener's acceptance rates; add independent rating files for replication."
        ),
    }

    text = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    print(text, end="")


if __name__ == "__main__":
    main()
