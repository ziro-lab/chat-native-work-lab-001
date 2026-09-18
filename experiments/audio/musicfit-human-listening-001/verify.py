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


def main():
    manifest = json.loads((PACK / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["source_count"] == 6
    assert manifest["task_count"] == 18
    assert manifest["candidate_count"] == 54
    assert len(manifest["tasks"]) == 18

    for task in manifest["tasks"]:
        assert [c["blind_id"] for c in task["candidates"]] == ["A", "B", "C"]
        assert sorted(c["algorithm_rank"] for c in task["candidates"]) == [1, 2, 3]
        assert len({c["audio"] for c in task["candidates"]}) == 3
        for candidate in task["candidates"]:
            assert (PACK / candidate["audio"]).is_file()
            assert any(p["kind"] == "ending" for p in candidate["previews"])
            for preview in candidate["previews"]:
                assert (PACK / preview["audio"]).is_file()

    page = (PACK / "review.html").read_text(encoding="utf-8")
    assert "algorithm_rank" not in page
    assert page.count('class="task"') == 18
    assert page.count('class="candidate"') == 54

    scripts = re.findall(r"<script>(.*?)</script>", page, flags=re.S)
    assert len(scripts) == 1
    with tempfile.TemporaryDirectory() as td:
        js = Path(td) / "review.js"
        js.write_text(scripts[0], encoding="utf-8")
        subprocess.run(["node", "--check", str(js)], check=True)

    smoke = {
        "schema": "musicfit-human-listening-ratings/v1",
        "rater": "smoke",
        "tasks": [
            {
                "id": task["id"],
                "best": task["candidates"][0]["blind_id"],
                "candidates": [
                    {"blind_id": c["blind_id"], "overall": "acceptable", "issues": []}
                    for c in task["candidates"]
                ],
            }
            for task in manifest["tasks"]
        ],
    }
    smoke_path = OUT / "smoke-ratings.json"
    smoke_path.write_text(json.dumps(smoke, indent=2) + "\n", encoding="utf-8")
    report_path = OUT / "smoke-score.json"
    subprocess.run(
        [
            sys.executable,
            str(ROOT / "score_ratings.py"),
            str(PACK / "manifest.json"),
            str(smoke_path),
            "--output",
            str(report_path),
        ],
        check=True,
    )
    report = json.loads(report_path.read_text(encoding="utf-8"))
    assert report["complete_task_ratings"] == 18
    assert report["top1_acceptable_rate"] == 1.0
    assert report["top3_any_acceptable_rate"] == 1.0

    evidence = json.loads((OUT / "evidence.json").read_text(encoding="utf-8"))
    evidence["verification"] = {
        "blind_html_contains_algorithm_rank": False,
        "node_js_syntax": "PASS",
        "smoke_scorer_top1": report["top1_acceptable_rate"],
        "smoke_scorer_top3": report["top3_any_acceptable_rate"],
    }
    (OUT / "evidence.json").write_text(
        json.dumps(evidence, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print("PASS_MUSICFIT_HUMAN_LISTENING_PACK_VERIFY_V1")


if __name__ == "__main__":
    main()
