#!/usr/bin/env python3
"""Apply deterministic Ground-Truth-preserving post-processing to a phase fixture."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import shutil
import struct
import subprocess
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PHASE_BUILDER = ROOT.parent / "audio-loop-dsp-baseline-001" / "build_phase_fixture.py"

PROFILES = {
    "v1": {"low_gain": 1.18, "high_gain": 0.72, "alpha": 0.20, "drive": 1.15, "echo_ms": [31, 67], "echo_gain": [0.07, 0.04], "noise": 0.0015, "gain": 0.92},
    "v2": {"low_gain": 0.78, "high_gain": 1.20, "alpha": 0.12, "drive": 1.40, "echo_ms": [47], "echo_gain": [0.05], "noise": 0.0022, "gain": 0.86},
    "v3": {"low_gain": 1.08, "high_gain": 0.88, "alpha": 0.32, "drive": 1.65, "echo_ms": [23, 59, 101], "echo_gain": [0.08, 0.05, 0.03], "noise": 0.0010, "gain": 0.80},
    "v4": {"low_gain": 0.86, "high_gain": 1.12, "alpha": 0.18, "drive": 1.25, "echo_ms": [71, 137], "echo_gain": [0.09, 0.04], "noise": 0.0028, "gain": 0.88},
    "v5": {"low_gain": 1.25, "high_gain": 0.66, "alpha": 0.26, "drive": 1.85, "echo_ms": [37, 83], "echo_gain": [0.06, 0.04], "noise": 0.0018, "gain": 0.76},
    "v6": {"low_gain": 0.70, "high_gain": 1.28, "alpha": 0.10, "drive": 1.50, "echo_ms": [19, 43, 109], "echo_gain": [0.06, 0.05, 0.03], "noise": 0.0024, "gain": 0.82},
}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def read_wav(path: Path) -> tuple[wave._wave_params, list[float]]:
    with wave.open(str(path), "rb") as wf:
        params = wf.getparams()
        if params.nchannels != 1 or params.sampwidth != 2:
            raise ValueError(f"expected mono 16-bit PCM WAV: {path}")
        raw = wf.readframes(params.nframes)
    ints = struct.unpack("<" + "h" * (len(raw) // 2), raw)
    return params, [x / 32768.0 for x in ints]


def write_wav(path: Path, params: wave._wave_params, samples: list[float]) -> None:
    vals = [max(-32768, min(32767, int(round(x * 32767.0)))) for x in samples]
    with wave.open(str(path), "wb") as wf:
        wf.setparams(params)
        chunk = 8192
        for i in range(0, len(vals), chunk):
            block = vals[i : i + chunk]
            wf.writeframes(struct.pack("<" + "h" * len(block), *block))


def spectral_tilt(samples: list[float], alpha: float, low_gain: float, high_gain: float) -> list[float]:
    lp = 0.0
    out = []
    for x in samples:
        lp += alpha * (x - lp)
        hp = x - lp
        out.append(low_gain * lp + high_gain * hp)
    return out


def soft_compress(samples: list[float], drive: float) -> list[float]:
    denom = math.tanh(drive)
    return [math.tanh(drive * x) / denom for x in samples]


def add_echo(samples: list[float], sr: int, delays_ms: list[int], gains: list[float]) -> list[float]:
    out = list(samples)
    for delay_ms, gain in zip(delays_ms, gains):
        delay = max(1, round(sr * delay_ms / 1000.0))
        for i in range(delay, len(samples)):
            out[i] += gain * samples[i - delay]
    return out


def add_noise(samples: list[float], amplitude: float, seed: int) -> list[float]:
    rng = random.Random(seed)
    return [x + (rng.random() * 2.0 - 1.0) * amplitude for x in samples]


def apply_profile(samples: list[float], sr: int, profile: dict, seed: int) -> list[float]:
    x = spectral_tilt(samples, float(profile["alpha"]), float(profile["low_gain"]), float(profile["high_gain"]))
    x = soft_compress(x, float(profile["drive"]))
    x = add_echo(x, sr, list(profile["echo_ms"]), list(profile["echo_gain"]))
    x = add_noise(x, float(profile["noise"]), seed)
    gain = float(profile["gain"])
    # Fixed headroom keeps profiles comparable without per-file normalization.
    return [max(-0.98, min(0.98, v * gain * 0.62)) for v in x]


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--output", type=Path, default=ROOT / "out")
    args = p.parse_args()
    out = args.output.resolve()
    clean = out / "clean"
    processed = out / "processed"
    for d in (clean, processed):
        if d.exists():
            shutil.rmtree(d)
        d.mkdir(parents=True)

    subprocess.run([sys.executable, str(PHASE_BUILDER), "--output", str(clean)], check=True)
    manifest = json.loads((clean / "manifest.json").read_text(encoding="utf-8"))

    transformed = []
    for index, variant in enumerate(manifest["variants"]):
        vid = variant["id"]
        profile = PROFILES[vid]
        params, samples = read_wav(clean / variant["path"])
        result = apply_profile(samples, params.framerate, profile, 20260917 + index * 997)
        dst = processed / f"{vid}-processed.wav"
        write_wav(dst, params, result)
        item = dict(variant)
        item["path"] = dst.name
        item["sha256"] = sha256(dst)
        item["transform_profile"] = profile
        transformed.append(item)

    transformed_manifest = dict(manifest)
    transformed_manifest["schema"] = "audio-loop-transform-robustness/v1"
    transformed_manifest["variants"] = transformed
    transformed_manifest["audio_root"] = "processed"
    transformed_manifest["transform_policy"] = {
        "preserves": ["tempo", "meter", "pitch-class plan", "bar order", "phrase order", "loop phase"],
        "changes": ["level", "spectral tilt", "nonlinear dynamics", "short reverberant texture", "noise floor"],
    }
    (out / "manifest.json").write_text(json.dumps(transformed_manifest, indent=2) + "\n", encoding="utf-8")
    print(f"transformed {len(transformed)} variants")


if __name__ == "__main__":
    main()
