#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"


def main() -> None:
    subprocess.run([sys.executable, "inventory_sources.py"], cwd=ROOT, check=True)
    inventory = json.loads((OUT / "inventory.json").read_text(encoding="utf-8"))
    sources = inventory["sources"]
    assert inventory["source_count"] == 6, inventory["source_count"]
    assert len(sources) == 6
    assert {s["license"] for s in sources} <= {"CC0-1.0", "CC-BY-4.0"}
    assert sum(s["license"] == "CC0-1.0" for s in sources) == 2
    assert sum(s["license"] == "CC-BY-4.0" for s in sources) == 4
    for source in sources:
        assert source["resolved_download_url"].startswith("https://"), source
        assert int(source["zip_size"]) > 1000, source
        assert len(source["zip_sha256"]) == 64, source
        assert int(source["audio_member_count"]) >= 1, source
        assert int(source["wav_member_count"]) >= 1, source
        readable_wavs = [m for m in source["archive_members"] if m.get("wav")]
        assert readable_wavs, source
        assert all(m["wav"]["sample_rate"] > 0 for m in readable_wavs)
        assert all(m["wav"]["frames"] > 0 for m in readable_wavs)

    evidence = {
        "status": "PASS_AUDIO_REAL_LOOP_SOURCE_INVENTORY_V1",
        "source_count": len(sources),
        "sources": [
            {
                "id": s["id"],
                "license": s["license"],
                "page": s["page"],
                "resolved_download_url": s["resolved_download_url"],
                "zip_size": s["zip_size"],
                "zip_sha256": s["zip_sha256"],
                "wav_member_count": s["wav_member_count"],
                "wav_names": [m["name"] for m in s["archive_members"] if Path(m["name"]).suffix.lower() == ".wav"],
            }
            for s in sources
        ],
        "interpretation": "Six permissively licensed real seamless music-loop packs were resolved and cryptographically inventoried at runtime without committing or re-uploading their audio bytes.",
    }
    (OUT / "evidence.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("PASS_AUDIO_REAL_LOOP_SOURCE_INVENTORY_V1")


if __name__ == "__main__":
    main()
