#!/usr/bin/env python3
"""Run Beat This small0 on the synthetic loop fixture and record predictions/checkpoint identity."""
from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
import shutil
import subprocess
import sys
from pathlib import Path

import torch
import torchaudio
from beat_this.inference import File2Beats

ROOT = Path(__file__).resolve().parent
FOUNDATION = ROOT.parent / "audio-loop-recovery-001"


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def locate_small0_checkpoint() -> Path:
    checkpoint_dir = Path(torch.hub.get_dir()) / "checkpoints"
    candidates = sorted(checkpoint_dir.glob("*small0*.ckpt"))
    if not candidates:
        candidates = sorted(checkpoint_dir.glob("*small0*"))
    if len(candidates) != 1:
        raise RuntimeError(
            f"expected exactly one downloaded small0 checkpoint under {checkpoint_dir}, found: {candidates}"
        )
    return candidates[0]


def to_float_list(values) -> list[float]:
    if hasattr(values, "detach"):
        values = values.detach().cpu()
    if hasattr(values, "tolist"):
        values = values.tolist()
    return [float(x) for x in values]


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, default=ROOT / "out")
    args = p.parse_args()
    out = args.output.resolve()
    fixture = out / "fixture"
    out.mkdir(parents=True, exist_ok=True)
    if fixture.exists():
        shutil.rmtree(fixture)
    fixture.mkdir(parents=True)

    subprocess.run(
        [sys.executable, str(FOUNDATION / "generate_benchmark.py"), "--output", str(fixture)],
        check=True,
    )
    manifest = json.loads((fixture / "manifest.json").read_text(encoding="utf-8"))

    model = File2Beats(checkpoint_path="small0", device="cpu", dbn=False)
    takes = []
    for take in manifest["takes"]:
        audio_path = fixture / take["path"]
        beats, downbeats = model(audio_path)
        takes.append(
            {
                "id": take["id"],
                "audio": take["path"],
                "beats": to_float_list(beats),
                "downbeats": to_float_list(downbeats),
            }
        )

    checkpoint = locate_small0_checkpoint()
    result = {
        "schema": "audio-beatthis-grid/v1",
        "runtime": {
            "python": sys.version.split()[0],
            "beat_this": importlib.metadata.version("beat-this"),
            "torch": torch.__version__,
            "torchaudio": torchaudio.__version__,
            "device": "cpu",
            "dbn": False,
            "checkpoint_name": checkpoint.name,
            "checkpoint_size": checkpoint.stat().st_size,
            "checkpoint_sha256": sha256(checkpoint),
        },
        "fixture": {
            "sample_rate": manifest["generator"]["sample_rate"],
            "bpm": manifest["generator"]["bpm"],
            "beats_per_bar": manifest["generator"]["beats_per_bar"],
            "bars_per_loop": manifest["generator"]["bars_per_loop"],
            "duration_seconds": manifest["generator"]["bars_per_loop"]
            * manifest["generator"]["beats_per_bar"]
            * 60.0
            / manifest["generator"]["bpm"],
        },
        "takes": takes,
    }
    (out / "predictions.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result["runtime"], indent=2))
    for take in takes:
        print(f"{take['id']}: beats={len(take['beats'])} downbeats={len(take['downbeats'])}")


if __name__ == "__main__":
    main()
