from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

from filter_fsld_metadata import filter_metadata
from license_gate import allowed_licenses, gate_record, normalize_license

ROOT = Path(__file__).resolve().parent


def main() -> None:
    assert normalize_license("https://creativecommons.org/publicdomain/zero/1.0/") == "CC0-1.0"
    assert normalize_license("https://creativecommons.org/licenses/by/4.0/") == "CC-BY-4.0"
    assert normalize_license("CC BY 3.0") == "CC-BY-3.0"
    assert normalize_license("https://creativecommons.org/licenses/by-nc/4.0/") == "CC-BY-NC"
    assert normalize_license("https://creativecommons.org/licenses/by-sa/4.0/") == "CC-BY-SA"

    assert gate_record({"id": 1, "license": "CC0 1.0"}).allowed
    assert gate_record({"id": 2, "license": "CC BY 4.0", "creator": "Artist"}).allowed
    assert not gate_record({"id": 3, "license": "CC BY 4.0"}).allowed
    assert not gate_record({"id": 4, "license": "CC BY-NC 4.0", "creator": "Artist"}).allowed
    assert not gate_record({"id": 5, "license": "CC BY-SA 4.0", "creator": "Artist"}).allowed
    assert not gate_record({"id": 6, "license": "custom"}).allowed

    sample = {
        "100": {"license": "https://creativecommons.org/publicdomain/zero/1.0/", "username": "zero"},
        "101": {"license": "https://creativecommons.org/licenses/by/4.0/", "username": "by-user"},
        "102": {"license": "https://creativecommons.org/licenses/by-nc/4.0/", "username": "nc-user"},
        "103": {"license": "https://creativecommons.org/licenses/by-sa/4.0/", "username": "sa-user"},
        "104": {"license": "https://creativecommons.org/licenses/by/4.0/"},
        "105": {"license": "other", "username": "unknown"},
    }
    report = filter_metadata(sample)
    assert report["accepted_count"] == 2, report
    assert report["rejected_count"] == 4, report
    assert {x["id"] for x in report["accepted"]} == {"100", "101"}

    evidence = {
        "status": "PASS_COMMERCIAL_SAFE_CORPUS_GATE_V1",
        "allowlist": list(allowed_licenses()),
        "sample_accepted": report["accepted_count"],
        "sample_rejected": report["rejected_count"],
        "policy": {
            "commercial_use_required": True,
            "derivatives_required": True,
            "attribution_required_when_applicable": True,
            "nc_rejected": True,
            "nd_rejected": True,
            "sa_rejected_in_v1": True,
            "unknown_rejected": True,
        },
    }
    out = ROOT / "out"
    out.mkdir(exist_ok=True)
    (out / "gate-evidence.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    print("PASS_COMMERCIAL_SAFE_CORPUS_GATE_V1")


if __name__ == "__main__":
    main()
