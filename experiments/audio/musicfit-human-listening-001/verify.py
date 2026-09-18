from __future__ import annotations

import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
PACK = OUT / "listening-pack"


def score(ratings_path: Path, output_name: str):
    report_path = OUT / output_name
    subprocess.run(
        [
            sys.executable,
            str(ROOT / "score_ratings.py"),
            str(PACK / "manifest.json"),
            str(ratings_path),
            "--output",
            str(report_path),
        ],
        check=True,
    )
    return json.loads(report_path.read_text(encoding="utf-8"))


def main():
    manifest = json.loads((PACK / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["schema"] == "musicfit-human-listening-benchmark/v2"
    assert manifest["source_count"] == 6
    assert manifest["task_count"] == 18
    assert manifest["candidate_count"] == 54
    assert len(manifest["tasks"]) == 18

    for task in manifest["tasks"]:
        assert [c["blind_id"] for c in task["candidates"]] == ["A", "B", "C"]
        assert sorted(c["algorithm_rank"] for c in task["candidates"]) == [1, 2, 3]
        assert len({c["audio"] for c in task["candidates"]}) == 3
        assert (PACK / task["source_audio"]).is_file()
        assert (PACK / task["source_ending_audio"]).is_file()
        for candidate in task["candidates"]:
            assert (PACK / candidate["audio"]).is_file()
            assert any(p["kind"] == "ending" for p in candidate["previews"])
            for preview in candidate["previews"]:
                assert (PACK / preview["audio"]).is_file()

    page = (PACK / "review.html").read_text(encoding="utf-8")
    assert "algorithm_rank" not in page
    assert page.count('class="task"') == 18
    assert page.count('class="candidate"') == 54
    assert page.count('class="edit-impact"') == 54
    assert "元曲 Ending 5秒" in page
    assert "元曲と同等 / 改善" in page

    scripts = re.findall(r"<script>(.*?)</script>", page, flags=re.S)
    assert len(scripts) == 1
    with tempfile.TemporaryDirectory() as td:
        js = Path(td) / "review.js"
        js.write_text(scripts[0], encoding="utf-8")
        subprocess.run(["node", "--check", str(js)], check=True)

    # v2 smoke: absolutely usable and source-normalized clean.
    smoke_v2 = {
        "schema": "musicfit-human-listening-ratings/v2",
        "rater": "smoke-v2",
        "tasks": [
            {
                "id": task["id"],
                "best": task["candidates"][0]["blind_id"],
                "candidates": [
                    {
                        "blind_id": c["blind_id"],
                        "overall": "acceptable",
                        "edit_impact": "same_or_better",
                        "issues": [],
                    }
                    for c in task["candidates"]
                ],
            }
            for task in manifest["tasks"]
        ],
    }
    v2_path = OUT / "smoke-ratings-v2.json"
    v2_path.write_text(json.dumps(smoke_v2, indent=2) + "\n", encoding="utf-8")
    report_v2 = score(v2_path, "smoke-score-v2.json")
    assert report_v2["complete_task_ratings"] == 18
    assert report_v2["absolute_usability"]["top1_acceptable_rate"] == 1.0
    assert report_v2["absolute_usability"]["top3_any_acceptable_rate"] == 1.0
    assert report_v2["relative_edit_quality"]["top1_no_major_regression_rate"] == 1.0
    assert report_v2["relative_edit_quality"]["top3_any_no_major_regression_rate"] == 1.0
    assert report_v2["relative_edit_quality"]["top1_no_new_issue_rate"] == 1.0

    # v1 compatibility: old partial ratings still retain absolute metrics.
    smoke_v1 = {
        "schema": "musicfit-human-listening-ratings/v1",
        "rater": "smoke-v1",
        "tasks": [
            {
                "id": task["id"],
                "best": task["candidates"][0]["blind_id"],
                "candidates": [
                    {
                        "blind_id": c["blind_id"],
                        "overall": "acceptable",
                        "issues": [],
                    }
                    for c in task["candidates"]
                ],
            }
            for task in manifest["tasks"]
        ],
    }
    v1_path = OUT / "smoke-ratings-v1.json"
    v1_path.write_text(json.dumps(smoke_v1, indent=2) + "\n", encoding="utf-8")
    report_v1 = score(v1_path, "smoke-score-v1.json")
    assert report_v1["absolute_usability"]["top1_acceptable_rate"] == 1.0
    assert report_v1["relative_edit_quality"]["top1_determinate_tasks"] == 0
    assert report_v1["relative_edit_quality"]["top1_no_major_regression_rate"] is None

    evidence = json.loads((OUT / "evidence.json").read_text(encoding="utf-8"))
    evidence["verification"] = {
        "blind_html_contains_algorithm_rank": False,
        "node_js_syntax": "PASS",
        "source_ending_reference": "PASS",
        "relative_rating_fields": 54,
        "smoke_v2_absolute_top1": report_v2["absolute_usability"]["top1_acceptable_rate"],
        "smoke_v2_relative_top1": report_v2["relative_edit_quality"]["top1_no_major_regression_rate"],
        "legacy_v1_absolute_compatibility": report_v1["absolute_usability"]["top1_acceptable_rate"],
        "legacy_v1_relative_denominator": report_v1["relative_edit_quality"]["top1_determinate_tasks"],
    }
    (OUT / "evidence.json").write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print("PASS_MUSICFIT_HUMAN_LISTENING_PACK_VERIFY_V2")


if __name__ == "__main__":
    main()
