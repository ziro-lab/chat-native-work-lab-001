from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path

ABS_ACCEPTABLE = {"good", "acceptable"}
ABS_VALID = ABS_ACCEPTABLE | {"minor", "reject"}
REL_PASS = {"same_or_better", "minor_added"}
REL_CLEAN = {"same_or_better"}
REL_VALID = REL_PASS | {"major_added", "unclear_source"}
REL_DETERMINATE = REL_VALID - {"unclear_source"}
ISSUES = {"loop", "seam", "flow", "ending", "other"}


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def rate(x, d):
    return (x / d) if d else None


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
                malformed.append({
                    "file": str(rating_path),
                    "reason": "unknown_task",
                    "id": tid,
                })
                continue
            candidate_rows = []
            for c in task.get("candidates", []):
                bid = c.get("blind_id")
                key = (tid, bid)
                overall = c.get("overall")
                edit_impact = c.get("edit_impact")
                if key not in mapping or overall not in ABS_VALID:
                    continue
                if edit_impact not in REL_VALID:
                    edit_impact = None
                issues = sorted(set(c.get("issues") or []) & ISSUES)
                candidate_rows.append({
                    "blind_id": bid,
                    "rank": mapping[key]["algorithm_rank"],
                    "overall": overall,
                    "absolute_acceptable": overall in ABS_ACCEPTABLE,
                    "edit_impact": edit_impact,
                    "relative_determinate": edit_impact in REL_DETERMINATE,
                    "relative_pass": edit_impact in REL_PASS,
                    "relative_clean": edit_impact in REL_CLEAN,
                    "source_confounded": edit_impact == "unclear_source",
                    "issues": issues,
                })
            best = task.get("best")
            best_rank = (
                mapping[(tid, best)]["algorithm_rank"]
                if (tid, best) in mapping else None
            )
            rows.append({
                "rater": rater,
                "task": tid,
                "source": task_map[tid]["source_id"],
                "target_kind": task_map[tid]["target_kind"],
                "candidate_rows": candidate_rows,
                "best_rank": best_rank,
            })

    complete = [
        r for r in rows
        if len(r["candidate_rows"]) == len(task_map[r["task"]]["candidates"])
    ]

    abs_top1_ok = abs_top3_ok = abs_no_ok = 0
    rel_top1_pass = rel_top1_clean = rel_top1_den = 0
    rel_top3_pass = rel_top3_clean = rel_top3_den = 0
    source_confounded = 0
    candidate_total = 0
    best_ranks = Counter()
    introduced_issue_counts = Counter()
    introduced_issue_den = 0

    by_kind = defaultdict(lambda: {
        "n": 0,
        "abs_top1": 0,
        "abs_top3": 0,
        "abs_none": 0,
        "rel_top1_den": 0,
        "rel_top1_pass": 0,
        "rel_top3_den": 0,
        "rel_top3_pass": 0,
    })

    for row in complete:
        by_rank = {c["rank"]: c for c in row["candidate_rows"]}

        # Absolute usability: material quality + Music Fit together.
        a1 = bool(by_rank.get(1) and by_rank[1]["absolute_acceptable"])
        a3 = any(c["absolute_acceptable"] for c in row["candidate_rows"])
        abs_top1_ok += a1
        abs_top3_ok += a3
        abs_no_ok += not a3

        # Relative edit quality: only judge Music Fit-induced degradation.
        rank1 = by_rank.get(1)
        if rank1 and rank1["relative_determinate"]:
            rel_top1_den += 1
            rel_top1_pass += rank1["relative_pass"]
            rel_top1_clean += rank1["relative_clean"]

        determinate = [
            c for c in row["candidate_rows"] if c["relative_determinate"]
        ]
        if determinate:
            rel_top3_den += 1
            rel_top3_pass += any(c["relative_pass"] for c in determinate)
            rel_top3_clean += any(c["relative_clean"] for c in determinate)

        kind = by_kind[row["target_kind"]]
        kind["n"] += 1
        kind["abs_top1"] += a1
        kind["abs_top3"] += a3
        kind["abs_none"] += not a3
        if rank1 and rank1["relative_determinate"]:
            kind["rel_top1_den"] += 1
            kind["rel_top1_pass"] += rank1["relative_pass"]
        if determinate:
            kind["rel_top3_den"] += 1
            kind["rel_top3_pass"] += any(c["relative_pass"] for c in determinate)

        if row["best_rank"] is not None:
            best_ranks[row["best_rank"]] += 1

        for c in row["candidate_rows"]:
            candidate_total += 1
            source_confounded += c["source_confounded"]
            # Issue flags are defined as issues newly introduced or worsened by Music Fit.
            if c["edit_impact"] in {"minor_added", "major_added"}:
                introduced_issue_den += 1
                introduced_issue_counts.update(c["issues"])

    n = len(complete)
    report = {
        "schema": "musicfit-human-listening-report/v2",
        "rating_files": [str(p) for p in args.ratings],
        "task_ratings_seen": len(rows),
        "complete_task_ratings": n,
        "expected_tasks_per_rater": len(task_map),

        "absolute_usability": {
            "top1_acceptable_rate": rate(abs_top1_ok, n),
            "top3_any_acceptable_rate": rate(abs_top3_ok, n),
            "no_acceptable_rate": rate(abs_no_ok, n),
            "interpretation": (
                "Would the listener use the result as-is? This includes pre-existing "
                "source-material quality and therefore is not an algorithm-only score."
            ),
        },

        "relative_edit_quality": {
            "top1_no_major_regression_rate": rate(rel_top1_pass, rel_top1_den),
            "top1_no_new_issue_rate": rate(rel_top1_clean, rel_top1_den),
            "top1_determinate_tasks": rel_top1_den,
            "top3_any_no_major_regression_rate": rate(rel_top3_pass, rel_top3_den),
            "top3_any_no_new_issue_rate": rate(rel_top3_clean, rel_top3_den),
            "top3_determinate_tasks": rel_top3_den,
            "interpretation": (
                "Primary Music Fit quality metric. 'same_or_better' and 'minor_added' "
                "count as no-major-regression. 'unclear_source' is excluded rather "
                "than counted as failure."
            ),
        },

        "source_confound": {
            "candidate_count": candidate_total,
            "unclear_source_candidate_count": source_confounded,
            "unclear_source_candidate_rate": rate(source_confounded, candidate_total),
        },

        "best_candidate_rank_counts": dict(sorted(best_ranks.items())),
        "introduced_issue_counts": dict(introduced_issue_counts.most_common()),
        "introduced_issue_candidate_denominator": introduced_issue_den,

        "by_target_kind": {
            key: {
                "complete_tasks": v["n"],
                "absolute_top1_acceptable_rate": rate(v["abs_top1"], v["n"]),
                "absolute_top3_any_acceptable_rate": rate(v["abs_top3"], v["n"]),
                "absolute_no_acceptable_rate": rate(v["abs_none"], v["n"]),
                "relative_top1_no_major_regression_rate": rate(
                    v["rel_top1_pass"], v["rel_top1_den"]
                ),
                "relative_top1_determinate_tasks": v["rel_top1_den"],
                "relative_top3_any_no_major_regression_rate": rate(
                    v["rel_top3_pass"], v["rel_top3_den"]
                ),
                "relative_top3_determinate_tasks": v["rel_top3_den"],
            }
            for key, v in sorted(by_kind.items())
        },

        # Legacy aliases keep existing downstream summaries from breaking.
        "top1_acceptable_rate": rate(abs_top1_ok, n),
        "top3_any_acceptable_rate": rate(abs_top3_ok, n),
        "no_acceptable_rate": rate(abs_no_ok, n),

        "malformed_or_ignored": malformed,
        "interpretation": (
            "Report both absolute usability and source-normalized edit quality. "
            "A candidate can be absolutely rejected while still being a successful "
            "Music Fit edit if the objection already exists in the source."
        ),
    }

    text = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.write_text(text, encoding="utf-8")
    print(text, end="")


if __name__ == "__main__":
    main()
