from __future__ import annotations

import argparse
import json
import math
import re
import statistics
from collections import Counter
from pathlib import Path


def _quantile(values: list[float], q: float) -> float | None:
    if not values:
        return None
    xs = sorted(values)
    if len(xs) == 1:
        return xs[0]
    pos = (len(xs) - 1) * q
    lo = math.floor(pos)
    hi = math.ceil(pos)
    if lo == hi:
        return xs[lo]
    return xs[lo] * (hi - pos) + xs[hi] * (pos - lo)


def summarize(values: list[float]) -> dict:
    values = [float(v) for v in values if v > 0 and math.isfinite(v)]
    return {
        "count": len(values),
        "median_seconds": statistics.median(values) if values else None,
        "p25_seconds": _quantile(values, 0.25),
        "p75_seconds": _quantile(values, 0.75),
        "p90_seconds": _quantile(values, 0.90),
    }


def parse_points(path: Path) -> list[tuple[float, str]]:
    points = []
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        if not line:
            continue
        parts = re.split(r"\s+", line, maxsplit=1)
        if len(parts) != 2:
            continue
        try:
            t = float(parts[0])
        except ValueError:
            continue
        points.append((t, parts[1].strip().lower()))
    return points


def collect(files: list[Path], source: str) -> dict:
    durations: list[float] = []
    by_label: dict[str, list[float]] = {}
    transitions: Counter[str] = Counter()
    ending_predecessors: Counter[str] = Counter()
    file_count = 0

    for path in files:
        pts = parse_points(path)
        if len(pts) < 2:
            continue
        file_count += 1
        for (t0, label), (t1, next_label) in zip(pts, pts[1:]):
            duration = t1 - t0
            if duration > 0 and label not in {"silence", "end"}:
                durations.append(duration)
                by_label.setdefault(label, []).append(duration)
            transitions[f"{label}->{next_label}"] += 1
            if next_label == "end":
                ending_predecessors[label] += 1

    top_transitions = [
        {"transition": key, "count": count}
        for key, count in transitions.most_common(40)
    ]
    return {
        "source": source,
        "files": file_count,
        "section_duration": summarize(durations),
        "top_label_durations": {
            label: summarize(values)
            for label, values in sorted(by_label.items(), key=lambda kv: (-len(kv[1]), kv[0]))[:40]
        },
        "top_transitions": top_transitions,
        "ending_predecessors": dict(ending_predecessors.most_common(30)),
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--salami", type=Path, required=True)
    ap.add_argument("--harmonix", type=Path, required=True)
    ap.add_argument("--output", type=Path, required=True)
    args = ap.parse_args()

    salami_files = sorted(args.salami.glob("annotations/*/parsed/*_uppercase.txt"))
    harmonix_files = sorted((args.harmonix / "dataset" / "segments").glob("*.txt"))
    if not salami_files:
        raise RuntimeError("no SALAMI uppercase annotation files found")
    if not harmonix_files:
        raise RuntimeError("no Harmonix segment files found")

    report = {
        "schema": "music-fit-annotation-priors/v1",
        "audio_used": False,
        "sources": {
            "salami": {
                "license": "CC0-1.0",
                "pinned_commit": "8e4f95d18a3ab628c53011fa5a43e9d3be27965d",
            },
            "harmonix": {
                "license": "MIT",
                "pinned_commit": "64abeb509429e73d74559fb98e621dac866efea1",
                "note": "annotation text only; original audio/melspec data excluded",
            },
        },
        "priors": {
            "salami_uppercase": collect(salami_files, "SALAMI uppercase structure"),
            "harmonix_functional": collect(harmonix_files, "Harmonix functional segments"),
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps({
        "salami_files": report["priors"]["salami_uppercase"]["files"],
        "harmonix_files": report["priors"]["harmonix_functional"]["files"],
        "audio_used": False,
    }, indent=2))


if __name__ == "__main__":
    main()
