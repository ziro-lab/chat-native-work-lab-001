from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path

VALID = {"none", "minor", "major"}


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def rate(value, denominator):
    return value / denominator if denominator else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("ratings", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    manifest = load(args.manifest)
    ratings = load(args.ratings)
    task_map = {
        task["id"]: task
        for task in manifest["tasks"]
        if task.get("status") == "ok"
    }
    rank_map = {
        (task["id"], candidate["blind_id"]): candidate["algorithm_rank"]
        for task in task_map.values()
        for candidate in task["candidates"]
    }

    rows = []
    ignored = []
    for task_rating in ratings.get("tasks", []):
        task_id = task_rating.get("id")
        if task_id not in task_map:
            ignored.append({"task": task_id, "reason": "unknown_or_failed_task"})
            continue
        candidates = []
        for candidate in task_rating.get("candidates", []):
            blind = candidate.get("blind_id")
            key = (task_id, blind)
            overall = candidate.get("overall")
            ending = candidate.get("ending")
            if key not in rank_map or overall not in VALID or ending not in VALID:
                continue
            source_same = bool(candidate.get("source_same", False))
            candidates.append({
                "blind_id": blind,
                "rank": rank_map[key],
                "overall": overall,
                "ending": ending,
                "source_same": source_same,
                # Source-native problems are not charged to Music Fit.
                "fit_major": (overall == "major" or ending == "major") and not source_same,
                "fit_any_issue": (overall != "none" or ending != "none") and not source_same,
            })
        best = task_rating.get("best")
        rows.append({
            "id": task_id,
            "source_name": task_map[task_id]["source_name"],
            "target_seconds": task_map[task_id]["target_seconds"],
            "candidates": candidates,
            "best_rank": rank_map.get((task_id, best)),
        })

    complete = [
        row for row in rows
        if len(row["candidates"]) == len(task_map[row["id"]]["candidates"])
    ]

    top1_no_major = top3_no_major = 0
    top1_clean = top3_clean = 0
    source_confounded_candidates = 0
    candidate_count = 0
    overall_counts = Counter()
    ending_counts = Counter()
    best_ranks = Counter()
    by_target = defaultdict(lambda: {
        "n": 0,
        "top1_no_major": 0,
        "top3_no_major": 0,
        "top1_clean": 0,
        "top3_clean": 0,
    })

    for row in complete:
        by_rank = {candidate["rank"]: candidate for candidate in row["candidates"]}
        rank1 = by_rank.get(1)
        no_major_top1 = bool(rank1 and not rank1["fit_major"])
        no_major_top3 = any(not candidate["fit_major"] for candidate in row["candidates"])
        clean_top1 = bool(rank1 and not rank1["fit_any_issue"])
        clean_top3 = any(not candidate["fit_any_issue"] for candidate in row["candidates"])

        top1_no_major += no_major_top1
        top3_no_major += no_major_top3
        top1_clean += clean_top1
        top3_clean += clean_top3

        bucket = by_target[round(float(row["target_seconds"]), 3)]
        bucket["n"] += 1
        bucket["top1_no_major"] += no_major_top1
        bucket["top3_no_major"] += no_major_top3
        bucket["top1_clean"] += clean_top1
        bucket["top3_clean"] += clean_top3

        if row["best_rank"] is not None:
            best_ranks[row["best_rank"]] += 1

        for candidate in row["candidates"]:
            candidate_count += 1
            overall_counts[candidate["overall"]] += 1
            ending_counts[candidate["ending"]] += 1
            source_confounded_candidates += candidate["source_same"]

    n = len(complete)
    report = {
        "schema": "musicfit-local-realworld-report/v1",
        "complete_tasks": n,
        "expected_success_tasks": len(task_map),
        "fit_attributed_quality": {
            "top1_no_major_issue_rate": rate(top1_no_major, n),
            "top3_any_no_major_issue_rate": rate(top3_no_major, n),
            "top1_no_issue_rate": rate(top1_clean, n),
            "top3_any_no_issue_rate": rate(top3_clean, n),
        },
        "raw_listener_ratings": {
            "overall_counts": dict(overall_counts),
            "ending_counts": dict(ending_counts),
        },
        "source_confound": {
            "candidate_count": candidate_count,
            "source_same_count": source_confounded_candidates,
            "source_same_rate": rate(source_confounded_candidates, candidate_count),
        },
        "best_candidate_rank_counts": dict(sorted(best_ranks.items())),
        "by_target_seconds": {
            str(target): {
                "tasks": values["n"],
                "top1_no_major_issue_rate": rate(
                    values["top1_no_major"], values["n"]
                ),
                "top3_any_no_major_issue_rate": rate(
                    values["top3_no_major"], values["n"]
                ),
                "top1_no_issue_rate": rate(
                    values["top1_clean"], values["n"]
                ),
                "top3_any_no_issue_rate": rate(
                    values["top3_clean"], values["n"]
                ),
            }
            for target, values in sorted(by_target.items())
        },
        "ignored": ignored,
        "interpretation": (
            "Ratings marked source_same are preserved as listener observations but are "
            "not charged to Music Fit when computing fit-attributed issue rates."
        ),
    }

    text = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    print(text, end="")


if __name__ == "__main__":
    main()
