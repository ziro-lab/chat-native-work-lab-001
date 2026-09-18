from __future__ import annotations

import hashlib
import io
import json
import math
import re
import shutil
import sys
import tempfile
import time
from collections import Counter
from pathlib import Path

import numpy as np
import soundfile as sf
from scipy import signal

ROOT = Path(__file__).resolve().parent
AUDIO_ROOT = ROOT.parent
sys.path.insert(0, str(AUDIO_ROOT / "fsld-commercial-safe-index-001"))
sys.path.insert(0, str(AUDIO_ROOT / "commercial-safe-corpus-001"))
sys.path.insert(0, str(AUDIO_ROOT / "music-fit-core-001"))

from remote_zip import RemoteZip  # noqa: E402
from filter_fsld_metadata import filter_metadata  # noqa: E402
from musicfit.core import Budget, Config, analyze  # noqa: E402

URL = "https://zenodo.org/records/3967852/files/FSL10K.zip?download=1"
PER_LICENSE = 24
MAX_MEMBER_BYTES = 16 * 1024 * 1024
MAX_NETWORK_BYTES = 384 * 1024 * 1024
SEED = "musicfit-fsld-commercial-safe-benchmark-v1"


def stable_key(source_id: str) -> str:
    return hashlib.sha256(f"{SEED}:{source_id}".encode("utf-8")).hexdigest()


def normalize(x: np.ndarray, peak: float = 0.82) -> np.ndarray:
    y = np.asarray(x, dtype=np.float32).copy()
    m = float(np.max(np.abs(y))) if y.size else 0.0
    if m > 1e-9:
        y *= np.float32(peak / m)
    return y


def render_variants(loop: np.ndarray, source_id: str) -> list[np.ndarray]:
    # Structure-preserving transforms. They deliberately break byte identity without
    # changing duration, pitch or musical order.
    seed = int(hashlib.sha256(source_id.encode()).hexdigest()[:8], 16)
    rng = np.random.default_rng(seed)

    a = normalize(loop, 0.82)

    sos = signal.butter(1, 0.86, output="sos")
    b = signal.sosfilt(sos, loop, axis=0).astype(np.float32)
    b = np.tanh(b * np.float32(1.08)).astype(np.float32)
    b += rng.normal(0.0, 1.2e-4, size=b.shape).astype(np.float32)
    b = normalize(b, 0.79)

    c = signal.lfilter(np.array([1.0, -0.035], dtype=np.float32),
                       np.array([1.0], dtype=np.float32), loop, axis=0).astype(np.float32)
    if c.ndim == 1:
        c = c[:, None]
    gains = np.linspace(0.96, 1.04, c.shape[1], dtype=np.float32)
    c *= gains[None, :]
    c = np.tanh(c * np.float32(1.04)).astype(np.float32)
    c = normalize(c, 0.84)
    return [a, b, c]


def build_pseudo_song(loop: np.ndarray, sr: int, source_id: str):
    variants = render_variants(loop, source_id)
    period = len(loop)
    flank = max(1, period // 4)
    intro = variants[1][:flank].copy()
    outro = variants[2][-flank:].copy()
    intro *= np.linspace(0.0, 1.0, len(intro), dtype=np.float32)[:, None]
    outro *= np.linspace(1.0, 0.0, len(outro), dtype=np.float32)[:, None]
    song = np.concatenate([intro, *variants, outro], axis=0)
    song = normalize(song, 0.88)
    return song, {
        "period_frames": period,
        "body_start_frame": flank,
        "body_end_frame": flank + 3 * period,
        "period_seconds": period / sr,
    }


def audio_entries(rz: RemoteZip) -> dict[str, str]:
    result = {}
    rx = re.compile(r"(?:^|/)audio/(\d+)\.wav$", re.IGNORECASE)
    for name in rz.entries:
        m = rx.search(name)
        if m:
            result[m.group(1)] = name
    return result


def decode_wav(data: bytes):
    with sf.SoundFile(io.BytesIO(data)) as f:
        sr = f.samplerate
        channels = f.channels
        frames = f.frames
        audio = f.read(dtype="float32", always_2d=True)
    if len(audio) != frames or channels not in (1, 2):
        raise ValueError("unsupported_shape")
    if not np.all(np.isfinite(audio)):
        raise ValueError("nonfinite_audio")
    return sr, audio


def main() -> None:
    out = ROOT / "out"
    if out.exists():
        shutil.rmtree(out)
    out.mkdir()

    started = time.monotonic()
    rz = RemoteZip(URL)
    metadata_name = next(
        n for n in rz.entries
        if n.lower().endswith("/metadata.json") or n.lower() == "metadata.json"
    )
    payload = json.loads(rz.read(metadata_name, max_uncompressed=48 * 1024 * 1024).decode("utf-8"))
    filtered = filter_metadata(payload)
    amap = audio_entries(rz)

    groups = {
        "CC0-1.0": [],
        "CC-BY-3.0": [],
    }
    for row in filtered["accepted"]:
        lic = row["canonical_license"]
        if lic not in groups:
            continue
        source_id = str(row["id"])
        member = amap.get(source_id)
        if not member:
            continue
        entry = rz.entries[member]
        if 64 * 1024 <= entry.uncompressed_size <= MAX_MEMBER_BYTES:
            groups[lic].append((stable_key(source_id), row, member))
    for lic in groups:
        groups[lic].sort(key=lambda x: x[0])

    rows = []
    picked = Counter()
    config = Config()
    with tempfile.TemporaryDirectory(prefix="musicfit-fsld-") as td:
        temp = Path(td)
        # Walk deterministic candidates until each license bucket has PER_LICENSE
        # valid 2–30s loop WAVs.
        cursors = {lic: 0 for lic in groups}
        while any(picked[lic] < PER_LICENSE for lic in groups):
            progress = False
            for lic, candidates in groups.items():
                if picked[lic] >= PER_LICENSE:
                    continue
                if cursors[lic] >= len(candidates):
                    raise RuntimeError(f"not_enough_valid_sources:{lic}:{picked[lic]}")
                _, meta, member = candidates[cursors[lic]]
                cursors[lic] += 1
                progress = True
                source_id = str(meta["id"])
                try:
                    data = rz.read(member, max_uncompressed=MAX_MEMBER_BYTES)
                    sr, loop = decode_wav(data)
                except Exception:
                    continue
                duration = len(loop) / sr
                if not (2.05 <= duration <= 30.0 and 8000 <= sr <= 96000):
                    continue

                song, hidden = build_pseudo_song(loop, sr, source_id)
                path = temp / f"{source_id}.wav"
                sf.write(path, song, sr, subtype="PCM_24")
                budget = Budget(45)
                before = time.monotonic()
                analysis = analyze(path, config, budget)
                elapsed = time.monotonic() - before

                tol_frames = round(0.070 * sr)
                valid = [
                    edge for edge in analysis.edges
                    if abs((edge.end - edge.start) - hidden["period_frames"]) <= tol_frames
                    and edge.start >= hidden["body_start_frame"]
                    and edge.end <= hidden["body_end_frame"]
                ]
                rows.append({
                    "id": source_id,
                    "license": lic,
                    "creator": meta.get("creator"),
                    "sample_rate": sr,
                    "channels": loop.shape[1],
                    "loop_seconds": duration,
                    "archive_member": member,
                    "archive_member_uncompressed_bytes": rz.entries[member].uncompressed_size,
                    "analysis_seconds": elapsed,
                    "edge_count": len(analysis.edges),
                    "warnings": analysis.warnings,
                    "edge_kinds": sorted({e.kind for e in analysis.edges}),
                    "strict_known_pair_top1": bool(analysis.edges and analysis.edges[0] in valid),
                    "strict_known_pair_top3": any(e in valid for e in analysis.edges[:3]),
                    "pair_valid": bool(valid),
                    "top_periods_seconds": [
                        (e.end - e.start) / sr for e in analysis.edges[:3]
                    ],
                    "top_scores": [e.score for e in analysis.edges[:3]],
                })
                picked[lic] += 1
                path.unlink(missing_ok=True)

                if rz.fetched_bytes > MAX_NETWORK_BYTES:
                    raise RuntimeError(f"network_budget_exceeded:{rz.fetched_bytes}")
            if not progress:
                raise RuntimeError("selection_stalled")

    total = len(rows)
    top1 = sum(r["strict_known_pair_top1"] for r in rows)
    top3 = sum(r["strict_known_pair_top3"] for r in rows)
    valid_pairs = sum(r["pair_valid"] for r in rows)
    no_edges = sum(r["edge_count"] == 0 for r in rows)
    fallback = sum(
        any(kind.endswith("_fallback") for kind in r["edge_kinds"])
        for r in rows
    )
    by_license = {}
    for lic in groups:
        subset = [r for r in rows if r["license"] == lic]
        by_license[lic] = {
            "count": len(subset),
            "top1": sum(r["strict_known_pair_top1"] for r in subset) / len(subset),
            "top3": sum(r["strict_known_pair_top3"] for r in subset) / len(subset),
            "pair_valid_rate": sum(r["pair_valid"] for r in subset) / len(subset),
            "no_edge_rate": sum(r["edge_count"] == 0 for r in subset) / len(subset),
        }

    report = {
        "schema": "fsld-musicfit-benchmark/v1",
        "quality_is_diagnostic": True,
        "sample_policy": {
            "per_license": PER_LICENSE,
            "licenses": list(groups),
            "stable_seed": SEED,
            "duration_seconds": [2.05, 30.0],
            "max_member_bytes": MAX_MEMBER_BYTES,
        },
        "source_count": total,
        "strict_known_pair_top1": top1 / total,
        "strict_known_pair_top3": top3 / total,
        "pair_valid_rate": valid_pairs / total,
        "no_edge_rate": no_edges / total,
        "fallback_usage_rate": fallback / total,
        "by_license": by_license,
        "archive_size_bytes": rz.size,
        "range_requests": rz.range_requests,
        "network_bytes_fetched": rz.fetched_bytes,
        "max_network_bytes": MAX_NETWORK_BYTES,
        "elapsed_seconds": time.monotonic() - started,
        "human_naturalness_acceptance": None,
        "tracks": rows,
        "not_proven": [
            "85-90% human acceptance",
            "ordinary non-loop song generalization",
            "vocal semantic continuity",
            "YMM4 integration",
        ],
    }
    if total != PER_LICENSE * len(groups):
        raise RuntimeError(f"unexpected_sample_count:{total}")
    if rz.fetched_bytes > MAX_NETWORK_BYTES:
        raise RuntimeError("network_budget_exceeded")
    (out / "report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    print(json.dumps({
        "source_count": total,
        "strict_known_pair_top1": report["strict_known_pair_top1"],
        "strict_known_pair_top3": report["strict_known_pair_top3"],
        "pair_valid_rate": report["pair_valid_rate"],
        "no_edge_rate": report["no_edge_rate"],
        "fallback_usage_rate": report["fallback_usage_rate"],
        "by_license": by_license,
        "network_bytes_fetched": rz.fetched_bytes,
        "elapsed_seconds": report["elapsed_seconds"],
    }, indent=2))


if __name__ == "__main__":
    main()
