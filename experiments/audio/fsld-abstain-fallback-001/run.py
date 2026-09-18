from __future__ import annotations

import hashlib
import importlib.util
import json
import math
import shutil
import sys
import tempfile
import time
from collections import Counter
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

from musicfit.core import Budget, Config, Edge, _refine_period, analyze, transition_score
from remote_zip import RemoteZip
from filter_fsld_metadata import filter_metadata

URL = "https://zenodo.org/records/3967852/files/FSL10K.zip?download=1"
TRAIN_PER_BUCKET = 3
CANDIDATES_PER_BUCKET = 10
SIZE_SPLIT_BYTES = 1024 * 1024
MAX_MEMBER_BYTES = 10 * 1024 * 1024
MAX_NETWORK_BYTES = 768 * 1024 * 1024
TRAIN_SEED = "musicfit-abstain-fallback-train-v1"
PROPOSAL_FLOOR = 0.52
SEAM_GATE = 0.78
PERIOD_SEPARATION_SECONDS = 0.18
TOL = 0.070

STRATEGIES = {
    "joint8": ["joint8"],
    "harmonic8": ["harmonic8"],
    "rhythm4": ["rhythm4"],
    "union": ["joint8", "harmonic8", "rhythm4"],
}
COMPLEXITY = {"joint8": 1, "harmonic8": 1, "rhythm4": 1, "union": 3}


def load_benchmark_module():
    spec = importlib.util.spec_from_file_location("fsld_benchmark_fixture", BENCH_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


BENCH = load_benchmark_module()


def first(d, *keys):
    if not isinstance(d, dict):
        return None
    for key in keys:
        value = d.get(key)
        if value not in (None, "", 0, "0"):
            return value
    return None


def posnum(v):
    try:
        x = float(v)
    except (TypeError, ValueError):
        return None
    return x if math.isfinite(x) and x > 0 else None


def bpm_for(row):
    ann = row.get("annotations") if isinstance(row, dict) else {}
    ann = ann if isinstance(ann, dict) else {}
    return posnum(first(ann, "bpm", "tempo") or first(row, "bpm", "tempo"))


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
        raise ValueError("unsupported_metadata")
    for row in values:
        if not isinstance(row, dict):
            continue
        sid = first(row, "id", "sound_id", "fs_id", "freesound_id")
        if sid is not None:
            result[str(sid)] = row
    return result


def stable_key(source_id):
    return hashlib.sha256(f"{TRAIN_SEED}:{source_id}".encode()).hexdigest()


def window_sum(x, width):
    cs = np.concatenate(([0.0], np.cumsum(x, dtype=np.float64)))
    return cs[width:] - cs[:-width]


def zscore(v):
    v = np.asarray(v, dtype=np.float64)
    return (v - np.mean(v, axis=0)) / np.maximum(np.std(v, axis=0), 1e-5)


def rhythm_view(a):
    loge = np.log(np.maximum(a.energy.astype(np.float64), 1e-9))
    onset = np.maximum(np.diff(loge, prepend=loge[:1]), 0.0)
    flux = np.linalg.norm(
        np.diff(a.features[:, :25], axis=0, prepend=a.features[:1, :25]),
        axis=1,
    )
    return zscore(np.stack([loge, onset, flux], axis=1)).astype(np.float32)


def lag_scores(view, energy, lag, width):
    left, right = view[:-lag], view[lag:]
    if len(left) < width:
        return np.empty(0, dtype=np.float64)
    num = window_sum(np.sum(left * right, axis=1), width)
    den = np.sqrt(
        np.maximum(
            window_sum(np.sum(left * left, axis=1), width)
            * window_sum(np.sum(right * right, axis=1), width),
            1e-12,
        )
    )
    scores = num / den
    floor = max(1e-5, float(np.percentile(energy, 65)) * 0.12)
    e1 = window_sum(energy[:-lag], width) / width
    e2 = window_sum(energy[lag:], width) / width
    scores[(e1 <= floor) | (e2 <= floor)] = -1.0
    return scores


def proposal_edges(a, samples, view, context_seconds, kind):
    h = a.hop_seconds
    sr = a.sample_rate
    lo = max(2, math.ceil(Config().min_loop_seconds / h))
    hi = min(len(view) // 2 - 1, math.floor(Config().max_loop_seconds / h))
    cap = max(2, round(context_seconds / h))
    if hi <= lo:
        return []

    peaks = []
    for lag in range(lo, hi + 1):
        width = min(lag, cap)
        scores = lag_scores(view, a.energy, lag, width)
        if len(scores):
            peaks.append((float(np.max(scores)), lag, width))
    peaks.sort(reverse=True)

    used_lags = []
    candidates = []
    channel = int(np.argmax(np.einsum("ij,ij->j", samples, samples, dtype=np.float64)))
    mono = samples[:, channel]

    for recurrence, lag, width in peaks:
        if recurrence < PROPOSAL_FLOOR:
            break
        if any(abs(lag - other) * h < PERIOD_SEPARATION_SECONDS for other in used_lags):
            continue
        used_lags.append(lag)
        scores = lag_scores(view, a.energy, lag, width)
        indices = np.argsort(-scores)
        accepted_starts = []
        examined = 0
        for idx_np in indices:
            idx = int(idx_np)
            if scores[idx] < PROPOSAL_FLOOR or examined >= 30:
                break
            if any(abs(idx - other) * h < 0.24 for other in accepted_starts):
                continue
            examined += 1
            start = round(idx * h * sr)
            end = round((idx + lag) * h * sr)
            if start < round(0.2 * sr) or end > len(samples) - round(0.2 * sr):
                continue
            start, end = _refine_period(
                mono,
                sr,
                start,
                end,
                max_shift=min(0.12, max(0.04, h * 0.55)),
            )
            seam = transition_score(a, end, start)
            if seam < SEAM_GATE:
                continue
            score = 0.55 * float(scores[idx]) + 0.45 * seam
            candidates.append(
                Edge(start, end, float(score), float(scores[idx]), kind)
            )
            accepted_starts.append(idx)
            if len(accepted_starts) >= 3 or len(candidates) >= 18:
                break
        if len(candidates) >= 18:
            break

    candidates.sort(key=lambda e: (-e.score, e.start, e.end))
    return candidates


def diverse(edges, sr, count=3):
    selected = []
    deferred = []
    for edge in edges:
        period = (edge.end - edge.start) / sr
        if all(
            abs(period - (other.end - other.start) / sr) > PERIOD_SEPARATION_SECONDS
            for other in selected
        ) and len(selected) < count:
            selected.append(edge)
        else:
            deferred.append(edge)
    for edge in deferred:
        if len(selected) >= count:
            break
        if edge not in selected:
            selected.append(edge)
    return selected[:count]


def merge_edges(groups):
    merged = []
    seen = set()
    for edges in groups:
        for edge in edges:
            key = (
                round((edge.end - edge.start) / max(1, edge.end) * 100000),
                round(edge.start / 512),
                round(edge.end / 512),
            )
            if key in seen:
                continue
            seen.add(key)
            merged.append(edge)
    merged.sort(key=lambda e: (-e.score, e.start, e.end))
    return merged


def labels(edge, sr, publisher_period, bpm):
    p = (edge.end - edge.start) / sr
    strict = abs(p - publisher_period) <= TOL
    beats = p * bpm / 60 if bpm else None
    beat_int = round(beats) if beats is not None else None
    beat_ok = (
        beat_int is not None
        and beat_int >= 4
        and abs(beats - beat_int) <= max(0.08, 0.04 * beat_int)
    )
    return {
        "period_seconds": p,
        "strict": strict,
        "integer_beats": beat_int if beat_ok else None,
        "audition_worthy": strict or beat_ok,
        "score": edge.score,
        "period_score": edge.period_score,
        "kind": edge.kind,
    }


def fallback_views(a, samples):
    raw = {
        "joint8": proposal_edges(a, samples, a.features, 8.0, "abstain_joint8"),
        "harmonic8": proposal_edges(
            a, samples, a.features[:, :12], 8.0, "abstain_harmonic8"
        ),
        "rhythm4": proposal_edges(a, samples, rhythm_view(a), 4.0, "abstain_rhythm4"),
    }
    return {
        name: diverse(
            merge_edges([raw[key] for key in members]), a.sample_rate, 3
        )
        for name, members in STRATEGIES.items()
    }


def evaluate(tracks, strategy):
    top1_strict = top3_strict = 0
    top1_audition = top3_audition = 0
    available = 0
    rows = []
    for track in tracks:
        edges = track["fallback"][strategy]
        labs = [
            labels(
                edge,
                track["sample_rate"],
                track["publisher_period"],
                track["bpm"],
            )
            for edge in edges
        ]
        available += bool(labs)
        top1_strict += bool(labs and labs[0]["strict"])
        top3_strict += any(x["strict"] for x in labs)
        top1_audition += bool(labs and labs[0]["audition_worthy"])
        top3_audition += any(x["audition_worthy"] for x in labs)
        rows.append({"id": track["id"], "labels": labs})
    n = len(tracks)
    return {
        "count": n,
        "availability": available / n if n else None,
        "strict_top1": top1_strict / n if n else None,
        "strict_top3": top3_strict / n if n else None,
        "audition_top1": top1_audition / n if n else None,
        "audition_top3": top3_audition / n if n else None,
        "tracks": rows,
    }


def strategy_key(item):
    name, m = item
    return (
        m["audition_top3"],
        m["audition_top1"],
        m["availability"],
        m["strict_top3"],
        -COMPLEXITY[name],
    )


def prepare_track(rz, row, temp):
    data = rz.read(row["member"], max_uncompressed=MAX_MEMBER_BYTES)
    sr, loop = BENCH.decode_wav(data)
    duration = len(loop) / sr
    if not (2.05 <= duration <= 30.0 and 8000 <= sr <= 96000):
        return None
    song, hidden = BENCH.build_pseudo_song(loop, sr, str(row["id"]))
    path = temp / f"{row['id']}.wav"
    sf.write(path, song, sr, subtype="PCM_24")
    analysis = analyze(path, Config(), Budget(45))
    samples, read_sr = sf.read(path, dtype="float32", always_2d=True)
    assert read_sr == sr
    fallback = fallback_views(analysis, samples)
    path.unlink(missing_ok=True)
    return {
        "abstain": not bool(analysis.edges),
        "id": str(row["id"]),
        "creator": row["creator"],
        "license": row["canonical_license"],
        "sample_rate": sr,
        "publisher_period": hidden["period_seconds"],
        "bpm": bpm_for(row["raw"]),
        "fallback": fallback,
        "warnings": analysis.warnings,
    }


def edge_json(edge):
    return {
        "start": edge.start,
        "end": edge.end,
        "score": edge.score,
        "period_score": edge.period_score,
        "kind": edge.kind,
    }


def serial_track(track):
    return {
        **{k: v for k, v in track.items() if k != "fallback"},
        "fallback": {
            name: [edge_json(e) for e in edges]
            for name, edges in track["fallback"].items()
        },
    }


def main():
    out = ROOT / "out"
    if out.exists():
        shutil.rmtree(out)
    out.mkdir()

    holdout = json.loads(HOLDOUT_PATH.read_text(encoding="utf-8"))
    holdout_ids = {str(x["id"]) for x in holdout["tracks"]}
    holdout_creators = {x["creator"] for x in holdout["tracks"]}
    frozen_abstain_ids = [
        str(x["id"]) for x in holdout["tracks"] if not x["top"]
    ]
    if len(frozen_abstain_ids) != 4:
        raise RuntimeError(f"unexpected_frozen_abstain_count:{frozen_abstain_ids}")

    started = time.monotonic()
    rz = RemoteZip(URL)
    metadata_name = next(
        n for n in rz.entries
        if n.lower().endswith("/metadata.json") or n.lower() == "metadata.json"
    )
    payload = json.loads(
        rz.read(metadata_name, max_uncompressed=48 * 1024 * 1024).decode("utf-8")
    )
    raw_by_id = metadata_map(payload)
    filtered = filter_metadata(payload)
    audio_map = BENCH.audio_entries(rz)

    safe_rows = []
    for row in filtered["accepted"]:
        sid = str(row["id"])
        raw = raw_by_id.get(sid)
        member = audio_map.get(sid)
        if raw is None or member is None or bpm_for(raw) is None:
            continue
        if rz.entries[member].uncompressed_size > MAX_MEMBER_BYTES:
            continue
        safe_rows.append({**row, "raw": raw, "member": member})

    train_pool = [
        row for row in safe_rows
        if str(row["id"]) not in holdout_ids
        and row["creator"] not in holdout_creators
    ]
    train_pool.sort(key=lambda row: stable_key(str(row["id"])))

    # Fixed, cheap tuning cohort: 4 buckets by license and archive member size.
    buckets = {
        ("CC0-1.0", "small"): [],
        ("CC0-1.0", "large"): [],
        ("CC-BY-3.0", "small"): [],
        ("CC-BY-3.0", "large"): [],
    }
    for row in train_pool:
        lic = row["canonical_license"]
        size_class = (
            "small"
            if rz.entries[row["member"]].uncompressed_size < SIZE_SPLIT_BYTES
            else "large"
        )
        key = (lic, size_class)
        if key in buckets and len(buckets[key]) < CANDIDATES_PER_BUCKET:
            buckets[key].append(row)
        if all(len(v) >= CANDIDATES_PER_BUCKET for v in buckets.values()):
            break
    if not all(len(v) >= TRAIN_PER_BUCKET for v in buckets.values()):
        raise RuntimeError(
            "insufficient_stratified_training_rows:"
            + repr({str(k): len(v) for k, v in buckets.items()})
        )
    train = []
    selected_bucket_ids = {}
    holdout_tracks = []
    with tempfile.TemporaryDirectory(prefix="fsld-abstain-fallback-") as td:
        temp = Path(td)
        for key in sorted(buckets):
            chosen_ids = []
            for row in buckets[key]:
                if len(chosen_ids) >= TRAIN_PER_BUCKET:
                    break
                prepared = prepare_track(rz, row, temp)
                if prepared is None:
                    continue
                train.append(prepared)
                chosen_ids.append(str(row["id"]))
                if rz.fetched_bytes > MAX_NETWORK_BYTES:
                    raise RuntimeError("network_budget_exceeded_training")
            if len(chosen_ids) != TRAIN_PER_BUCKET:
                raise RuntimeError(
                    f"insufficient_valid_training_bucket:{key}:{chosen_ids}"
                )
            selected_bucket_ids[f"{key[0]}-{key[1]}"] = chosen_ids

        train_metrics = {
            name: evaluate(train, name) for name in STRATEGIES
        }
        chosen = max(train_metrics.items(), key=strategy_key)[0]

        # Only after strategy selection, evaluate the four frozen normal-path abstains.
        for sid in frozen_abstain_ids:
            row = by_id.get(sid)
            if row is None:
                raise RuntimeError(f"holdout_missing:{sid}")
            prepared = prepare_track(rz, row, temp)
            if prepared is None or not prepared.get("abstain"):
                raise RuntimeError(f"holdout_no_longer_abstains:{sid}")
            holdout_tracks.append(prepared)
            if rz.fetched_bytes > MAX_NETWORK_BYTES:
                raise RuntimeError("network_budget_exceeded_holdout")

    holdout_metrics = evaluate(holdout_tracks, chosen)
    report = {
        "schema": "fsld-abstain-fallback/v1",
        "normal_path_modified": False,
        "proposal_floor": PROPOSAL_FLOOR,
        "final_seam_gate": SEAM_GATE,
        "chosen_strategy": chosen,
        "training": {
            "training_count": len(train),
            "normal_path_abstain_count": sum(x["abstain"] for x in train),
            "stratification": {
                "per_bucket": TRAIN_PER_BUCKET,
                "size_split_bytes": SIZE_SPLIT_BYTES,
                "candidate_pool_per_bucket": CANDIDATES_PER_BUCKET,
                "selected_ids": selected_bucket_ids,
            },
            "id_overlap_with_holdout": 0,
            "creator_overlap_with_holdout": 0,
            "strategy_metrics": {
                name: {k: v for k, v in m.items() if k != "tracks"}
                for name, m in train_metrics.items()
            },
            "selected_metrics": train_metrics[chosen],
            "tracks": [serial_track(t) for t in train],
        },
        "frozen_holdout_abstains": {
            "ids": frozen_abstain_ids,
            "count": len(holdout_tracks),
            "selected_strategy_metrics": holdout_metrics,
            "tracks": [serial_track(t) for t in holdout_tracks],
        },
        "network": {
            "range_requests": rz.range_requests,
            "fetched_bytes": rz.fetched_bytes,
            "max_bytes": MAX_NETWORK_BYTES,
        },
        "elapsed_seconds": time.monotonic() - started,
        "interpretation": (
            "Exploratory abstain-only fallback. A proposal may use a broad recurrence "
            "floor, but an accepted edge still passes the normal 0.78 local seam gate. "
            "Human naturalness remains unmeasured."
        ),
    }
    if {x["id"] for x in train} & holdout_ids:
        raise RuntimeError("holdout_id_leakage")
    if {x["creator"] for x in train} & holdout_creators:
        raise RuntimeError("holdout_creator_leakage")
    if rz.fetched_bytes > MAX_NETWORK_BYTES:
        raise RuntimeError("network_budget_exceeded")

    (out / "report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False),
        encoding="utf-8",
    )
    print(json.dumps({
        "chosen_strategy": chosen,
        "training_count": len(train),
        "training_normal_abstains": sum(x["abstain"] for x in train),
        "training_selected": {
            k: v for k, v in train_metrics[chosen].items() if k != "tracks"
        },
        "holdout_selected": {
            k: v for k, v in holdout_metrics.items() if k != "tracks"
        },
        "network_bytes": rz.fetched_bytes,
        "elapsed_seconds": report["elapsed_seconds"],
    }, indent=2))


if __name__ == "__main__":
    main()
