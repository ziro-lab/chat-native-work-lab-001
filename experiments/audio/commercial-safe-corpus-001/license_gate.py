from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any

_ALLOWED = {
    "CC0-1.0",
    "CC-BY-2.0",
    "CC-BY-2.5",
    "CC-BY-3.0",
    "CC-BY-4.0",
}

_VERSION_RE = r"(2\.0|2\.5|3\.0|4\.0)"


@dataclass(frozen=True)
class GateResult:
    allowed: bool
    canonical_license: str | None
    reason: str


def normalize_license(value: Any) -> str | None:
    if not isinstance(value, str) or not value.strip():
        return None
    raw = value.strip().lower().replace("_", "-")

    if "publicdomain/zero/1.0" in raw or re.search(r"\bcc0(?:[- ]?1\.0)?\b", raw):
        return "CC0-1.0"

    # Restrictive variants are checked before plain BY.
    if "by-nc" in raw or "noncommercial" in raw or "non-commercial" in raw:
        return "CC-BY-NC"
    if "by-nd" in raw or "noderivatives" in raw or "no-derivatives" in raw:
        return "CC-BY-ND"
    if "by-sa" in raw or "sharealike" in raw or "share-alike" in raw:
        return "CC-BY-SA"

    m = re.search(rf"(?:licenses/)?by[/ -]?{_VERSION_RE}", raw)
    if not m:
        m = re.search(rf"\bcc[- ]?by[- ]?{_VERSION_RE}\b", raw)
    if m:
        return f"CC-BY-{m.group(1)}"

    return None


def gate_record(record: dict[str, Any]) -> GateResult:
    canonical = normalize_license(record.get("license"))
    if canonical not in _ALLOWED:
        return GateResult(False, canonical, "license_not_in_v1_allowlist")

    source_id = record.get("id") or record.get("sound_id") or record.get("fs_id")
    if source_id in (None, ""):
        return GateResult(False, canonical, "missing_source_id")

    if canonical.startswith("CC-BY-"):
        creator = (
            record.get("creator")
            or record.get("username")
            or record.get("author")
            or record.get("user")
        )
        if not isinstance(creator, str) or not creator.strip():
            return GateResult(False, canonical, "missing_required_attribution_creator")

    return GateResult(True, canonical, "allowed")


def allowed_licenses() -> tuple[str, ...]:
    return tuple(sorted(_ALLOWED))
