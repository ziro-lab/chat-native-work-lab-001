from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

from license_gate import gate_record


def _records(payload: Any) -> Iterable[tuple[str | None, dict[str, Any]]]:
    if isinstance(payload, list):
        for row in payload:
            if isinstance(row, dict):
                yield None, row
        return
    if isinstance(payload, dict):
        # FSLD metadata is expected to be keyed by Freesound ID; keep this
        # tolerant so the gate can also test exported list-like forms.
        for key, row in payload.items():
            if isinstance(row, dict):
                yield str(key), row
        return
    raise ValueError("unsupported metadata root; expected list or object")


def _first(row: dict[str, Any], *keys: str) -> Any:
    for key in keys:
        value = row.get(key)
        if value not in (None, ""):
            return value
    return None


def normalize_row(key: str | None, row: dict[str, Any]) -> dict[str, Any]:
    source_id = _first(row, "id", "sound_id", "fs_id", "freesound_id") or key
    creator = _first(row, "creator", "username", "author", "user")
    license_value = _first(row, "license", "license_url", "license_name")
    source_url = _first(row, "url", "freesound_url", "source_url")
    return {
        "id": str(source_id) if source_id is not None else None,
        "creator": creator,
        "license": license_value,
        "source_url": source_url,
        "audio_member": f"FSL10K/audio/{source_id}.wav" if source_id is not None else None,
    }


def filter_metadata(payload: Any) -> dict[str, Any]:
    accepted: list[dict[str, Any]] = []
    rejected: list[dict[str, Any]] = []
    raw_license_counts: Counter[str] = Counter()
    canonical_counts: Counter[str] = Counter()

    for key, row in _records(payload):
        normalized = normalize_row(key, row)
        raw_license_counts[str(normalized.get("license"))] += 1
        result = gate_record(normalized)
        if result.canonical_license:
            canonical_counts[result.canonical_license] += 1
        item = {
            **normalized,
            "canonical_license": result.canonical_license,
            "gate_reason": result.reason,
        }
        (accepted if result.allowed else rejected).append(item)

    return {
        "schema": "commercial-safe-fsld-intake/v1",
        "accepted_count": len(accepted),
        "rejected_count": len(rejected),
        "accepted": accepted,
        "rejected": rejected,
        "raw_license_counts": dict(sorted(raw_license_counts.items())),
        "canonical_license_counts": dict(sorted(canonical_counts.items())),
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("metadata", type=Path)
    ap.add_argument("--output", type=Path, required=True)
    args = ap.parse_args()
    payload = json.loads(args.metadata.read_text(encoding="utf-8"))
    report = filter_metadata(payload)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("accepted_count", "rejected_count", "canonical_license_counts")}, indent=2))


if __name__ == "__main__":
    main()
