from __future__ import annotations

import hashlib
import importlib.util
import io
import json
import math
import shutil
import sys
import tempfile
import time
from collections import Counter
from dataclasses import asdict
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent
AUDIO_ROOT = ROOT.parent
CORE_ROOT = AUDIO_ROOT / "music-fit-core-001"
BENCH_PATH = AUDIO_ROOT / "fsld-musicfit-benchmark-001" / "run_benchmark.py"
INDEX_ROOT = AUDIO_ROOT / "fsld-commercial-safe-index-001"
SAFE_ROOT = AUDIO_ROOT / "commercial-safe-corpus-001"
HOLDOUT_PATH = AUDIO_ROOT / "fsld-holdout-label-audit-001" / "holdout-baseline.json"

sys.path.insert(0, str(CORE_ROOT))
sys.path.insert(0, str(INDEX_ROOT))
sys.path.insert(0, str(SAFE_ROOT))

from musicfit.core import Budget, Config, analyze  # noqa: E402
from remote_zip import RemoteZip  # noqa: E402
from filter_fsld_metadata import filter_metadata  # noqa: E402

URL = "https://zenodo.org/records/3967852/files/FSL10K.zip?download=1"
TRAIN_PER_LICENSE = 32
TRAIN_SEED = "musicfit-fsld-audition-tuning-v1"
MAX_MEMBER_BYTES = 16 * 1024 * 1024
MAX_NETWORK_BYTES = 512 * 1024 * 1024
FALLBACKS = [None, 0.76, 0.74, 0.72, 0.70]
DIVERSITY = [0.0, 0.04, 0.08, 0.12, 0.18]
TOLERANCE_SECONDS = 0.070


def load_benchmark_module():
    spec = importlib.util.spec_from_file_location("fsld_benchmark_fixture", BENCH_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


BUILDER = load_benchmark_module()


def first(d, *keys):
    if not isinstance(d, dict):
        return None
    for key in keys:
        value = d.get(key)
        if value not in (None, "", 0, "0"):
            return value
    return None


def positive_number(v):
    try:
        x = float(v)
    except (TypeError, ValueError):
        return None
    return x if math.isfinite(x) and x > 0 else None


def bpm_for(row):
    ann = row.get("annotations") if isinstance(row, dict) else {}
    ann = ann if isinstance(ann, dict) else {}
    return positive_number(first(ann, "bpm", "tempo") or first(row, "bpm", "tempo"))


def metadata_map(payload):
    result = {}
    if isinstance(payload, dict):
        for key, row in payload.items():
            if isinstance(row, dict):
                result[str(key)] = row
        values = payload.values()
    elif isinstance(payload, list):
        values = payload
    else:
        raise ValueError("unsupported metadata root")
    for row in values:
        if not isinstance(row, dict):
            continue
        sid = first(row, "id", "sound_id", "fs_id", "freesound_id")
        if sid is not None:
            result[str(sid)] = row
    return result


def stable_key(source_id: str) -> str:
    return hashlib.sha256(f"{TRAIN_SEED}:{source_id}".encode()).hexdigest()


def period_seconds(edge, sr):
    return (edge.end - edge.start) / sr


def near_integer_beats(period: float, bpm: float | None):
    if not bpm:
        return None
    beats = period * bpm / 60.0
    n = round(beats)
    if n >= 4 and abs(beats - n) <= max(0.08, 0.04 * n):
        return n
    return None


def edge_labels(edge, sr, publisher_period, bpm):
    p = period_seconds(edge, sr)
    strict = abs(p - publisher_period) <= TOLERANCE_SECONDS
    beat_count = near_integer_beats(p, bpm)
    return {
        "period_seconds": p,
        "strict": strict,
        "integer_beats": beat_count,
        "audition_worthy": strict or beat_count is not None,
    }


def select_diverse(edges, sr, rel, count=3):
    if not edges:
        return []
    selected = []
    rejected = []
    for edge in edges:
        p = period_seconds(edge, sr)
        distinct = all(
            abs(p - period_seconds(other, sr))
            > max(0.180, rel * min(p, period_seconds(other, sr)))
            for other in selected
        )
        if distinct and len(selected) < count:
            selected.append(edge)
        else:
            rejected.append(edge)
    if len(selected) < count:
        for edge in rejected:
            if edge not in selected:
                selected.append(edge)
                if len(selected) == count:
                    break
    return selected[:count]


def evaluate_tracks(tracks, rel, fallback, *, raw_order=False):
    top1_strict = top3_strict = 0
    top1_audition = top3_audition = 0
    no_candidates = 0
    unique_period_counts = []
    fallback_used = 0
    per_track = []
    for row in tracks:
        edges = row["baseline_edges"]
        source = "baseline"
        if not edges and fallback is not None:
            edges = row["fallback_edges"].get(str(fallback), [])
            source = f"fallback_{fallback}"
            fallback_used += bool(edges)
        chosen = list(edges[:3]) if raw_order else select_diverse(
            edges, row["sample_rate"], rel
        )
        labels = [
            edge_labels(e, row["sample_rate"], row["publisher_period"], row["bpm"])
            for e in chosen
        ]
        if not chosen:
            no_candidates += 1
        top1_strict += bool(labels and labels[0]["strict"])
        top3_strict += any(x["strict"] for x in labels)
        top1_audition += bool(labels and labels[0]["audition_worthy"])
        top3_audition += any(x["audition_worthy"] for x in labels)
        unique_period_counts.append(
            len({round(x["period_seconds"], 3) for x in labels})
        )
        per_track.append({
            "id": row["id"],
            "candidate_source": source,
            "labels": labels,
        })
    n = len(tracks)
    return {
        "count": n,
        "strict_top1": top1_strict / n,
        "strict_top3": top3_strict / n,
        "audition_top1": top1_audition / n,
        "audition_top3": top3_audition / n,
        "no_candidate_rate": no_candidates / n,
        "fallback_used_count": fallback_used,
        "mean_unique_periods_in_top3": float(np.mean(unique_period_counts)),
        "per_track": per_track,
    }


def strategy_sort_key(item):
    strategy, metrics = item
    fallback = strategy["fallback_threshold"]
    # Higher threshold / disabled is preferred after quality ties.
    fallback_pref = 1.0 if fallback is None else float(fallback)
    return (
        metrics["audition_top3"],
        metrics["audition_top1"],
        metrics["strict_top3"],
        -metrics["no_candidate_rate"],
        metrics["mean_unique_periods_in_top3"],
        fallback_pref,
        -strategy["diversity_rel"],
    )


def config_for_threshold(value):
    base = asdict(Config())
    base["min_similarity"] = value
    return Config(**base)


def analyze_source(path, fallback_thresholds):
    baseline = analyze(path, Config(), Budget(60))
    fallback = {}
    if not baseline.edges:
        for threshold in fallback_thresholds:
            if threshold is None:
                continue
            a = analyze(path, config_for_threshold(threshold), Budget(60))
            fallback[str(threshold)] = a.edges
    return baseline, fallback


def prepare_track(rz, meta, member, temp: Path):
    source_id = str(meta["id"])
    data = rz.read(member, max_uncompressed=MAX_MEMBER_BYTES)
    sr, loop = BUILDER.decode_wav(data)
    duration = len(loop) / sr
    if not (2.05 <= duration <= 30.0 and 8000 <= sr <= 96000):
        return None
    song, hidden = BUILDER.build_pseudo_song(loop, sr, source_id)
    path = temp / f"{source_id}.wav"
    sf.write(path, song, sr, subtype="PCM_24")
    baseline, fallback = analyze_source(path, FALLBACKS)
    path.unlink(missing_ok=True)
    bpm = bpm_for(meta["raw"])
    if not bpm:
        return None
    return {
        "id": source_id,
        "creator": meta["creator"],
        "license": meta["canonical_license"],
        "sample_rate": sr,
        "publisher_period": hidden["period_seconds"],
        "bpm": bpm,
        "baseline_edges": baseline.edges,
        "fallback_edges": fallback,
        "baseline_warnings": baseline.warnings,
    }


def make_safe_rows(filtered, raw_by_id, audio_map):
    rows = []
    for row in filtered["accepted"]:
        sid = str(row["id"])
        raw = raw_by_id.get(sid)
        member = audio_map.get(sid)
        if raw is None or member is None:
            continue
        entry = RZ.entries[member]
        if not (64 * 1024 <= entry.uncompressed_size <= MAX_MEMBER_BYTES):
            continue
        rows.append({**row, "raw": raw, "member": member})
    return rows


def edge_json(edge):
    return {
        "start": edge.start,
        "end": edge.end,
        "score": edge.score,
        "period_score": edge.period_score,
        "kind": edge.kind,
    }


def serializable_track(row):
    return {
        **{k: v for k, v in row.items() if k not in ("baseline_edges", "fallback_edges")},
        "baseline_edges": [edge_json(e) for e in row["baseline_edges"]],
        "fallback_edges": {
            k: [edge_json(e) for e in edges]
            for k, edges in row["fallback_edges"].items()
        },
    }


def main():
    global RZ
    out = ROOT / "out"
    if out.exists():
        shutil.rmtree(out)
    out.mkdir()

    holdout = json.loads(HOLDOUT_PATH.read_text(encoding="utf-8"))
    holdout_ids = {str(x["id"]) for x in holdout["tracks"]}
    holdout_creators = {x["creator"] for x in holdout["tracks"]}

    started = time.monotonic()
    RZ = RemoteZip(URL)
    metadata_name = next(
        n for n in RZ.entries
        if n.lower().endswith("/metadata.json") or n.lower() == "metadata.json"
    )
    payload = json.loads(
        RZ.read(metadata_name, max_uncompressed=48 * 1024 * 1024).decode("utf-8")
    )
    raw_by_id = metadata_map(payload)
    filtered = filter_metadata(payload)
    audio_map = BUILDER.audio_entries(RZ)
    safe_rows = make_safe_rows(filtered, raw_by_id, audio_map)

    groups = {"CC0-1.0": [], "CC-BY-3.0": []}
    for row in safe_rows:
        if row["canonical_license"] not in groups:
            continue
        if row["id"] in holdout_ids or row["creator"] in holdout_creators:
            continue
        if bpm_for(row["raw"]) is None:
            continue
        groups[row["canonical_license"]].append(row)
    for lic in groups:
        groups[lic].sort(key=lambda r: stable_key(str(r["id"])))

    tune_tracks = []
    holdout_tracks = []
    with tempfile.TemporaryDirectory(prefix="fsld-audition-tuning-") as td:
        temp = Path(td)
        for lic, candidates in groups.items():
            for row in candidates:
                if sum(x["license"] == lic for x in tune_tracks) >= TRAIN_PER_LICENSE:
                    break
                prepared = prepare_track(RZ, row, row["member"], temp)
                if prepared is not None:
                    tune_tracks.append(prepared)
                if RZ.fetched_bytes > MAX_NETWORK_BYTES:
                    raise RuntimeError("network_budget_exceeded_during_tuning")

        if len(tune_tracks) != 2 * TRAIN_PER_LICENSE:
            raise RuntimeError(f"insufficient_tuning_tracks:{len(tune_tracks)}")

        # Evaluate all strategy combinations on tuning data only.
        grid = []
        for rel in DIVERSITY:
            for fallback in FALLBACKS:
                strategy = {"diversity_rel": rel, "fallback_threshold": fallback}
                metrics = evaluate_tracks(tune_tracks, rel, fallback)
                grid.append((strategy, metrics))
        grid.sort(key=strategy_sort_key, reverse=True)
        chosen_strategy, tuning_metrics = grid[0]

        # Only after strategy selection, load and evaluate the frozen holdout.
        safe_by_id = {str(r["id"]): r for r in safe_rows}
        for frozen in holdout["tracks"]:
            sid = str(frozen["id"])
            row = safe_by_id.get(sid)
            if row is None:
                raise RuntimeError(f"holdout_not_safe_or_missing:{sid}")
            prepared = prepare_track(RZ, row, row["member"], temp)
            if prepared is None:
                raise RuntimeError(f"holdout_prepare_failed:{sid}")
            holdout_tracks.append(prepared)
            if RZ.fetched_bytes > MAX_NETWORK_BYTES:
                raise RuntimeError("network_budget_exceeded_during_holdout")

    if {x["id"] for x in tune_tracks} & holdout_ids:
        raise RuntimeError("id_leakage")
    if {x["creator"] for x in tune_tracks} & holdout_creators:
        raise RuntimeError("creator_leakage")

    # Raw baseline means the analyzer's original score order, exactly edges[:3].
    # It must not silently include the 180 ms diversity floor.
    raw_holdout = evaluate_tracks(
        holdout_tracks, 0.0, None, raw_order=True
    )
    period_diverse_holdout = evaluate_tracks(
        holdout_tracks, 0.0, None
    )
    holdout_metrics = evaluate_tracks(
        holdout_tracks,
        chosen_strategy["diversity_rel"],
        chosen_strategy["fallback_threshold"],
    )

    report = {
        "schema": "fsld-audition-tuning/v1",
        "chosen_strategy": chosen_strategy,
        "data_separation": {
            "tuning_count": len(tune_tracks),
            "holdout_count": len(holdout_tracks),
            "id_overlap": 0,
            "creator_overlap": 0,
            "tuning_ids": [x["id"] for x in tune_tracks],
            "holdout_ids": [x["id"] for x in holdout_tracks],
        },
        "tuning_metrics": tuning_metrics,
        "holdout_raw_baseline": raw_holdout,
        "holdout_period_diverse_180ms": period_diverse_holdout,
        "holdout_selected_strategy": holdout_metrics,
        "holdout_delta_vs_raw": {
            key: holdout_metrics[key] - raw_holdout[key]
            for key in (
                "strict_top1",
                "strict_top3",
                "audition_top1",
                "audition_top3",
                "no_candidate_rate",
                "mean_unique_periods_in_top3",
            )
        },
        "top_strategy_grid": [
            {"strategy": s, "metrics": {k: v for k, v in m.items() if k != "per_track"}}
            for s, m in grid[:10]
        ],
        "network": {
            "range_requests": RZ.range_requests,
            "fetched_bytes": RZ.fetched_bytes,
            "max_bytes": MAX_NETWORK_BYTES,
        },
        "elapsed_seconds": time.monotonic() - started,
        "quality_note": (
            "audition_top* is an annotation-derived soft candidate-coverage metric, "
            "not human naturalness acceptance"
        ),
        "tuning_tracks": [serializable_track(x) for x in tune_tracks],
        "holdout_tracks": [serializable_track(x) for x in holdout_tracks],
    }
    if RZ.fetched_bytes > MAX_NETWORK_BYTES:
        raise RuntimeError("network_budget_exceeded")
    (out / "report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    print(json.dumps({
        "chosen_strategy": chosen_strategy,
        "tuning": {k: v for k, v in tuning_metrics.items() if k != "per_track"},
        "holdout_raw_baseline": {k: v for k, v in raw_holdout.items() if k != "per_track"},
        "holdout_period_diverse_180ms": {k: v for k, v in period_diverse_holdout.items() if k != "per_track"},
        "holdout_selected": {k: v for k, v in holdout_metrics.items() if k != "per_track"},
        "holdout_delta_vs_raw": report["holdout_delta_vs_raw"],
        "network_bytes": RZ.fetched_bytes,
        "elapsed_seconds": report["elapsed_seconds"],
    }, indent=2))


if __name__ == "__main__":
    main()
