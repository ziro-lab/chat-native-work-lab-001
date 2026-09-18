from __future__ import annotations

import argparse
import json
from collections import Counter, defaultdict
from pathlib import Path


def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("ratings",type=Path)
    ap.add_argument("--unblind",type=Path,default=Path("out/listening-pack/unblind.json"))
    ap.add_argument("--output",type=Path)
    args=ap.parse_args()

    ratings=json.loads(args.ratings.read_text(encoding="utf-8"))
    unblind=json.loads(args.unblind.read_text(encoding="utf-8"))
    mapping={}
    for case in unblind["cases"]:
        for row in case["mapping"]:
            mapping[f'{case["case_id"]}:{row["label"]}']={
                "case_id":case["case_id"],
                "automatic_rank":row["automatic_rank"],
                "ending":row["ending"],
            }

    rows=[]
    for rating in ratings.get("ratings",[]):
        key=rating.get("key")
        if key not in mapping:
            raise ValueError(f"unknown_rating_key:{key}")
        rows.append({**mapping[key],**rating})

    by_case=defaultdict(list)
    for row in rows:
        by_case[row["case_id"]].append(row)

    case_meta={case["case_id"]:case for case in unblind["cases"]}
    expected=set(case_meta)
    unexpected=set(by_case)-expected
    if unexpected:
        raise ValueError(f"unexpected_case_ratings:{sorted(unexpected)}")

    cases=[]
    for cid in sorted(expected):
        meta=case_meta[cid]
        expected_ratings=len(meta["mapping"])
        cr=by_case.get(cid,[])
        if len(cr)!=expected_ratings:
            raise ValueError(
                f"incomplete_case_ratings:{cid}:{len(cr)}!={expected_ratings}"
            )
        if expected_ratings==0:
            cases.append({
                "case_id":cid,
                "candidate_count":0,
                "top1_usable":False,
                "top3_usable":False,
                "usable_count":0,
                "top1_overall":None,
                "best_human_labels":[],
                "top1_ending_mode":None,
                "status":meta.get("status","no_candidate"),
            })
            continue
        rank1=next(x for x in cr if x["automatic_rank"]==1)
        usable=[x for x in cr if x.get("overall")=="usable"]
        cases.append({
            "case_id":cid,
            "candidate_count":expected_ratings,
            "top1_usable":rank1.get("overall")=="usable",
            "top3_usable":bool(usable),
            "usable_count":len(usable),
            "top1_overall":rank1.get("overall"),
            "best_human_labels":[x["key"].split(":")[-1] for x in usable],
            "top1_ending_mode":rank1["ending"],
            "status":meta.get("status","candidates"),
        })

    n=len(cases)
    field_counts={}
    for field in ("overall","seam","flow","ending"):
        field_counts[field]=dict(Counter(x.get(field,"") for x in rows))

    result={
        "schema":"musicfit-human-listening-score/v1",
        "case_count":n,
        "candidate_ratings":len(rows),
        "no_candidate_cases":sum(x["candidate_count"]==0 for x in cases),
        "top1_usable_rate":sum(x["top1_usable"] for x in cases)/n,
        "top3_usable_rate":sum(x["top3_usable"] for x in cases)/n,
        "cases_with_no_usable_candidate":sum(not x["top3_usable"] for x in cases),
        "mean_usable_candidates_per_case":sum(x["usable_count"] for x in cases)/n,
        "field_counts":field_counts,
        "cases":cases,
        "interpretation":"Human listening result for this CC0 review pack only; not a population-wide accuracy claim.",
    }
    text=json.dumps(result,indent=2,ensure_ascii=False)+"\n"
    if args.output:
        args.output.write_text(text,encoding="utf-8")
    print(text,end="")


if __name__=="__main__":
    main()
