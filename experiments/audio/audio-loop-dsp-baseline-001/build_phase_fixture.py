#!/usr/bin/env python3
"""Build a phase-rotated loop-recovery fixture from the synthetic foundation."""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent
FOUNDATION = ROOT.parent / "audio-loop-recovery-001"

# (variant id, 1-based base take id, rotation in whole bars)
VARIANTS = [
    ("v1", 1, 0),
    ("v2", 1, 3),
    ("v3", 2, 2),
    ("v4", 2, 6),
    ("v5", 3, 1),
    ("v6", 3, 5),
]


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def read_wav(path: Path) -> tuple[wave._wave_params, bytes]:
    with wave.open(str(path), "rb") as wf:
        params = wf.getparams()
        frames = wf.readframes(wf.getnframes())
    return params, frames


def rotate_wav(src: Path, dst: Path, rotation_frames: int) -> None:
    params, frames = read_wav(src)
    frame_bytes = params.nchannels * params.sampwidth
    total_frames = len(frames) // frame_bytes
    rotation_frames %= total_frames
    cut = rotation_frames * frame_bytes
    rotated = frames[cut:] + frames[:cut]
    with wave.open(str(dst), "wb") as wf:
        wf.setparams(params)
        wf.writeframes(rotated)


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, default=ROOT / "out")
    args = p.parse_args()
    out = args.output.resolve()
    base_out = out / "foundation"
    out.mkdir(parents=True, exist_ok=True)
    if base_out.exists():
        shutil.rmtree(base_out)
    base_out.mkdir(parents=True)

    subprocess.run(
        [sys.executable, str(FOUNDATION / "generate_benchmark.py"), "--output", str(base_out)],
        check=True,
    )
    base_manifest = json.loads((base_out / "manifest.json").read_text(encoding="utf-8"))
    g = base_manifest["generator"]
    sample_rate = int(g["sample_rate"])
    bpm = float(g["bpm"])
    beats_per_bar = int(g["beats_per_bar"])
    bars_per_loop = int(g["bars_per_loop"])
    beat_frames = round((60.0 / bpm) * sample_rate)
    bar_frames = beat_frames * beats_per_bar
    loop_frames = bar_frames * bars_per_loop

    variants = []
    by_id = {}
    for variant_id, base_take, rotation_bars in VARIANTS:
        src = base_out / f"take-{base_take}.wav"
        dst = out / f"{variant_id}.wav"
        rotate_wav(src, dst, rotation_bars * bar_frames)
        item = {
            "id": variant_id,
            "base_take": base_take,
            "rotation_bars": rotation_bars,
            "canonical_bar_at_file_start": rotation_bars + 1,
            "path": dst.name,
            "sha256": sha256(dst),
            "sample_rate": sample_rate,
            "frames": loop_frames,
        }
        variants.append(item)
        by_id[variant_id] = item

    queries = []
    for src in variants:
        for tgt in variants:
            # Avoid byte-identical/same-render comparisons. Every query crosses base takes.
            if src["base_take"] == tgt["base_take"]:
                continue
            desired_canonical_bar = int(src["canonical_bar_at_file_start"])
            positive_physical_bar = ((desired_canonical_bar - 1 - int(tgt["rotation_bars"])) % bars_per_loop) + 1
            qid = f"{src['id']}-to-{tgt['id']}"
            candidates = []
            for physical_bar in range(1, bars_per_loop + 1):
                canonical_bar = ((int(tgt["rotation_bars"]) + physical_bar - 1) % bars_per_loop) + 1
                is_positive = physical_bar == positive_physical_bar
                candidates.append(
                    {
                        "id": f"{qid}:bar{physical_bar}",
                        "target_sample": (physical_bar - 1) * bar_frames,
                        "physical_bar": physical_bar,
                        "canonical_bar": canonical_bar,
                        "label": "positive" if is_positive else "hard_negative",
                        "reason": "same canonical loop phase" if is_positive else "downbeat-aligned wrong loop phase",
                    }
                )
            # One deliberately wrong beat phase at the otherwise correct physical bar.
            candidates.append(
                {
                    "id": f"{qid}:bar{positive_physical_bar}-beat3",
                    "target_sample": (positive_physical_bar - 1) * bar_frames + 2 * beat_frames,
                    "physical_bar": positive_physical_bar,
                    "canonical_bar": desired_canonical_bar,
                    "label": "hard_negative",
                    "reason": "correct canonical bar but wrong beat phase",
                }
            )
            queries.append(
                {
                    "id": qid,
                    "source_variant": src["id"],
                    "target_variant": tgt["id"],
                    "source_reference_sample": 0,
                    "source_exit_sample": loop_frames,
                    "desired_canonical_bar": desired_canonical_bar,
                    "positive_physical_bar": positive_physical_bar,
                    "candidates": candidates,
                }
            )

    manifest = {
        "schema": "audio-loop-phase-recovery-benchmark/v1",
        "generator": {
            "foundation": "../audio-loop-recovery-001/generate_benchmark.py",
            "sample_rate": sample_rate,
            "bpm": bpm,
            "beats_per_bar": beats_per_bar,
            "bars_per_loop": bars_per_loop,
            "beat_frames": beat_frames,
            "bar_frames": bar_frames,
            "loop_frames": loop_frames,
            "variant_count": len(variants),
            "query_count": len(queries),
        },
        "ground_truth": {
            "task": "recover the target location with the same canonical loop phase as the source file start",
            "candidate_grid": "all bar downbeats plus one off-beat hard negative",
            "exact_pcm_identity_allowed": False,
        },
        "variants": variants,
        "queries": queries,
    }
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"generated {len(variants)} rotated variants and {len(queries)} queries")


if __name__ == "__main__":
    main()
