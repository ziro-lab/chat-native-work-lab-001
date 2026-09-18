from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SAFE = ROOT.parent / "commercial-safe-corpus-001"
sys.path.insert(0, str(SAFE))

from filter_fsld_metadata import filter_metadata  # noqa: E402
from remote_zip import RemoteZip  # noqa: E402

URL = "https://zenodo.org/records/3967852/files/FSL10K.zip?download=1"
MAX_FETCHED_BYTES = 64 * 1024 * 1024


def main() -> None:
    out = ROOT / "out"
    out.mkdir(exist_ok=True)

    rz = RemoteZip(URL)
    metadata_names = sorted(
        n for n in rz.entries
        if n.lower().endswith("/metadata.json") or n.lower() == "metadata.json"
    )
    if len(metadata_names) != 1:
        raise RuntimeError(f"expected_one_metadata_json:{metadata_names[:10]}")
    metadata_name = metadata_names[0]
    raw = rz.read(metadata_name, max_uncompressed=48 * 1024 * 1024)
    payload = json.loads(raw.decode("utf-8"))
    filtered = filter_metadata(payload)

    accepted_index = [
        {
            "id": row["id"],
            "creator": row["creator"],
            "canonical_license": row["canonical_license"],
            "source_url": row["source_url"],
            "audio_member": row["audio_member"],
        }
        for row in filtered["accepted"]
    ]
    (out / "accepted-index.json").write_text(
        json.dumps(
            {
                "schema": "fsld-commercial-safe-index/v1",
                "source_record": "https://zenodo.org/records/3967852",
                "archive_url": URL,
                "archive_size": rz.size,
                "metadata_member": metadata_name,
                "accepted": accepted_index,
            },
            indent=2,
            ensure_ascii=False,
        ) + "\n",
        encoding="utf-8",
    )

    total = filtered["accepted_count"] + filtered["rejected_count"]
    report = {
        "status": "PASS_FSLD_COMMERCIAL_SAFE_INDEX_V1",
        "archive_size_bytes": rz.size,
        "zip_entry_count": len(rz.entries),
        "metadata_member": metadata_name,
        "metadata_uncompressed_bytes": len(raw),
        "metadata_record_count": total,
        "accepted_count": filtered["accepted_count"],
        "rejected_count": filtered["rejected_count"],
        "canonical_license_counts": filtered["canonical_license_counts"],
        "range_requests": rz.range_requests,
        "fetched_bytes": rz.fetched_bytes,
        "max_fetched_bytes": MAX_FETCHED_BYTES,
        "audio_members_read": 0,
        "sample_accepted_ids": [row["id"] for row in accepted_index[:20]],
        "interpretation": (
            "FSLD metadata was extracted by bounded HTTP Range ZIP access and gated "
            "to the conservative commercial-safe CC0/CC-BY allowlist. No WAV member was fetched."
        ),
    }
    if total < 9000:
        raise RuntimeError(f"unexpected_metadata_record_count:{total}")
    if filtered["accepted_count"] <= 0:
        raise RuntimeError("no_commercial_safe_records_accepted")
    if rz.fetched_bytes > MAX_FETCHED_BYTES:
        raise RuntimeError(f"metadata_range_budget_exceeded:{rz.fetched_bytes}")
    (out / "evidence.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(json.dumps(report, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
