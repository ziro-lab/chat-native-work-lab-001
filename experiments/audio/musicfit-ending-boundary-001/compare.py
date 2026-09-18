from __future__ import annotations

import importlib.util
import json
import math
import shutil
import sys
import tempfile
import time
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent
AUDIO_ROOT = ROOT.parent
CORE_ROOT = AUDIO_ROOT / "music-fit-core-001"
BUILDER_PATH = AUDIO_ROOT / "audio-real-loop-self-discovery-001" / "run_self_discovery.py"
sys.path.insert(0, str(CORE_ROOT))

from musicfit.core import (  # noqa: E402
    Budget,
    Config,
    FitError,
    Plan,
    Span,
    _merged,
    analyze,
    plans,
    target_frames,
    transition_score,
    validate_plan,
)
from musicfit.render import render  # noqa: E402


def load_builder():
    spec = importlib.util.spec_from_file_location("pinned_real_fixture", BUILDER_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


BUILDER = load_builder()


def _smooth(x: np.ndarray, width: int) -> np.ndarray:
    if width <= 1:
        return x
    kernel = np.ones(width, dtype=np.float64) / width
    return np.convolve(x, kernel, mode="same")


def ending_entries(analysis, config: Config) -> list[dict]:
    """Rank model-free candidate entries into the real source ending."""
    sr, n = analysis.sample_rate, analysis.frames
    duration = n / sr
    h = analysis.hop_seconds
    if len(analysis.features) < 5:
        return []

    x = analysis.features.astype(np.float64, copy=False)
    d = np.linalg.norm(np.diff(x, axis=0, prepend=x[:1]), axis=1)
    energy = np.maximum(analysis.energy.astype(np.float64), 1e-9)
    de = np.abs(np.diff(np.log(energy), prepend=np.log(energy[:1])))

    # Robustly normalize so one transient cannot dominate the entire track.
    def robust(v):
        lo, hi = np.percentile(v, [20, 95])
        return np.clip((v - lo) / max(1e-9, hi - lo), 0.0, 1.0)

    novelty = 0.82 * robust(d) + 0.18 * robust(de)
    novelty = _smooth(novelty, max(1, round(0.20 / h)))

    min_tail = max(4.0, config.keep_outro_seconds)
    max_tail = min(36.0, max(min_tail + 1.0, duration * 0.45))
    if duration <= min_tail + 1.0:
        return []

    candidates = []
    lo_i = max(1, round((duration - max_tail) / h))
    hi_i = min(len(novelty) - 2, round((duration - min_tail) / h))
    for i in range(lo_i, hi_i + 1):
        if novelty[i] < novelty[i - 1] or novelty[i] < novelty[i + 1]:
            continue
        entry_seconds = i * h
        tail_seconds = duration - entry_seconds
        # Broad prior only. Peak novelty remains dominant.
        prior = math.exp(-0.5 * ((tail_seconds - 18.0) / 10.0) ** 2)
        score = 0.82 * float(novelty[i]) + 0.18 * prior
        entry = round(entry_seconds * sr)
        if 0 < entry < n:
            candidates.append({
                "entry": entry,
                "tail_seconds": tail_seconds,
                "novelty": float(novelty[i]),
                "rank_score": score,
                "kind": "novelty",
            })

    candidates.sort(key=lambda row: (-row["rank_score"], row["entry"]))
    selected = []
    separation = round(1.25 * sr)
    for row in candidates:
        if any(abs(row["entry"] - other["entry"]) < separation for other in selected):
            continue
        selected.append(row)
        if len(selected) >= 12:
            break

    # Preserve old candidates exactly, so the expanded search is auditable.
    fixed = [
        ("protected_outro", max(config.keep_outro_seconds, 0.001)),
        ("fixed_4s", 4.0),
        ("fixed_8s", 8.0),
    ]
    for kind, tail_seconds in fixed:
        entry = n - round(tail_seconds * sr)
        if 0 < entry < n and not any(abs(entry - r["entry"]) < round(0.08 * sr) for r in selected):
            selected.append({
                "entry": entry,
                "tail_seconds": (n - entry) / sr,
                "novelty": None,
                "rank_score": None,
                "kind": kind,
            })
    return selected


def plans_boundary(analysis, seconds: float, config=None, budget=None):
    """Core Phase 4 clone with only the ending-entry candidate set expanded."""
    c, b = config or Config(), budget or Budget()
    c.validate()
    target = target_frames(seconds, analysis.sample_rate, c)
    sr, n = analysis.sample_rate, analysis.frames
    intro = round(c.keep_intro_seconds * sr)
    outro = round(c.keep_outro_seconds * sr)
    if intro + outro > min(n, target):
        raise FitError("protected_prefix_suffix_exceed_duration")
    minimum = min(round(c.min_run_seconds * sr), target // 4)

    eligible = [
        e for e in analysis.edges
        if e.start >= intro and e.end <= n - outro and e.end - e.start >= minimum
    ]
    edges = [(e.end, e.start, e.score, f"b{i}") for i, e in enumerate(eligible)]
    edges += [(e.start, e.end, e.score, f"f{i}") for i, e in enumerate(eligible)]

    terminal_entries = ending_entries(analysis, c)
    terminals = {}
    beam = [(0, 0, (), 0.0, ())]

    for depth in range(c.max_jumps + 1):
        b.check()
        next_states = []
        for written, cursor, spans, cost, used in beam:
            remaining = target - written
            if remaining <= 0:
                continue

            if remaining <= n - cursor:
                tail = Span(cursor, cursor + remaining)
                mode = "source_end" if tail.end == n else "fade"
                penalty = 0 if mode == "source_end" else 0.65
                route = _merged([*spans, tail])
                key = tuple((s.start, s.end) for s in route)
                terminals[key] = (
                    cost + penalty, route, mode, "boundary_graph_fit"
                )

                if remaining >= minimum + outro and n - cursor - remaining > round(0.12 * sr):
                    for ending in terminal_entries:
                        entry = ending["entry"]
                        tail_len = n - entry
                        exit_frame = cursor + remaining - tail_len
                        if (
                            exit_frame < cursor + minimum
                            or exit_frame < intro
                            or exit_frame >= entry
                            or exit_frame >= n - outro
                        ):
                            continue
                        score = transition_score(analysis, exit_frame, entry)
                        if score < c.min_similarity:
                            continue
                        route = _merged([
                            *spans,
                            Span(cursor, exit_frame),
                            Span(entry, n),
                        ])
                        key = tuple((s.start, s.end) for s in route)
                        terminals[key] = (
                            cost + 0.07 + 0.7 * (1 - score),
                            route,
                            "source_end",
                            "boundary_graph_fit",
                        )

            if depth == c.max_jumps:
                continue

            for exit_frame, entry, score, eid in edges:
                if exit_frame < cursor + minimum or exit_frame < intro:
                    continue
                total = written + exit_frame - cursor
                if total >= target - max(outro, minimum):
                    continue
                repetitions = used.count(eid)
                newcost = cost + 0.035 + 0.55 * (1 - score) + 0.008 * repetitions
                newused = (*used, eid)
                route = (*spans, Span(cursor, exit_frame))
                delta = abs(target - (total + n - entry)) / max(target, sr)
                priority = newcost + 0.5 * delta
                next_states.append((
                    priority, total, entry, route, newcost, newused
                ))

        if not next_states:
            break
        next_states.sort(key=lambda v: (v[0], v[1], v[2], v[5]))
        seen = set()
        beam = []
        for _, written, cursor, route, cost, used in next_states:
            key = (
                round(written / max(1, round(sr * 0.05))),
                cursor,
                used[-1],
            )
            if key in seen:
                continue
            seen.add(key)
            beam.append((written, cursor, route, cost, used))
            if len(beam) >= c.beam_width:
                break
        if len(terminals) > 512:
            terminals = dict(
                sorted(terminals.items(), key=lambda kv: kv[1][0])[:256]
            )

    ranked = sorted(
        terminals.values(),
        key=lambda v: (
            v[0],
            len(v[1]),
            tuple((s.start, s.end) for s in v[1]),
        ),
    )
    selected = []
    signatures = []
    for cost, route, mode, strategy in ranked:
        sig = tuple(
            (
                round(left.end / sr / 0.25),
                round(right.start / sr / 0.25),
            )
            for left, right in zip(route, route[1:])
        )
        if sig in signatures:
            continue
        signatures.append(sig)
        scores = [
            transition_score(analysis, left.end, right.start)
            for left, right in zip(route, route[1:])
        ]
        warnings = ["listening_review_required;not_a_calibrated_probability"]
        if mode == "fade":
            warnings.append("exact_duration_uses_fade_not_original_ending")
        if scores and min(scores) < c.min_similarity:
            warnings.append("weak_local_transition")
        p = Plan(
            f"candidate-{len(selected) + 1}",
            target,
            route,
            float(cost),
            mode,
            scores,
            warnings,
            strategy,
        )
        validate_plan(analysis, p, c)
        selected.append(p)
        if len(selected) == 3:
            break
    return selected


def summarize(all_requests, key):
    rows = [r[key] for r in all_requests]
    plans_flat = [p for r in rows for p in r["plans"]]
    source_end = [p for p in plans_flat if p["ending"] == "source_end"]
    transition_scores = [
        score for p in plans_flat for score in p["transition_scores"]
    ]
    return {
        "requests": len(rows),
        "requests_with_candidates": sum(bool(r["plans"]) for r in rows),
        "requests_with_source_end": sum(
            any(p["ending"] == "source_end" for p in r["plans"]) for r in rows
        ),
        "candidate_count": len(plans_flat),
        "source_end_candidates": len(source_end),
        "fade_candidates": sum(p["ending"] == "fade" for p in plans_flat),
        "multijump_candidates": sum(
            p["distinct_jump_count"] >= 2 for p in plans_flat
        ),
        "mean_transition_score": (
            float(np.mean(transition_scores)) if transition_scores else None
        ),
        "min_transition_score": (
            float(min(transition_scores)) if transition_scores else None
        ),
        "sample_exact": all(p["sample_exact"] for p in plans_flat),
        "plan_seconds": sum(r["plan_seconds"] for r in rows),
        "render_seconds": sum(
            p["render_seconds"] for r in rows for p in r["plans"]
        ),
    }


def run_variant(path, analysis, seconds, config, mode, output_dir):
    budget = Budget(120)
    before = time.monotonic()
    if mode == "baseline":
        candidate_plans = plans(analysis, seconds, config, budget, phase=4)
    else:
        candidate_plans = plans_boundary(analysis, seconds, config, budget)
    plan_seconds = time.monotonic() - before
    rows = []
    for plan in candidate_plans:
        dest = output_dir / f"{mode}-{plan.id}.wav"
        before = time.monotonic()
        meta = render(path, analysis, plan, dest, config, budget)
        render_seconds = time.monotonic() - before
        rows.append({
            "ending": plan.ending,
            "cost": plan.cost,
            "transition_scores": plan.transition_scores,
            "distinct_jump_count": len({
                (left.end, right.start)
                for left, right in zip(plan.spans, plan.spans[1:])
            }),
            "sample_exact": meta["frames"] == plan.target_frames,
            "render_seconds": render_seconds,
        })
        dest.unlink(missing_ok=True)
    return {"plan_seconds": plan_seconds, "plans": rows}


def main():
    out = ROOT / "out"
    if out.exists():
        shutil.rmtree(out)
    out.mkdir()

    builder = BUILDER
    config = Config()
    requests = []
    source_rows = []
    started = time.monotonic()

    with tempfile.TemporaryDirectory(prefix="musicfit-ending-boundary-") as td:
        temp = Path(td)
        for source in builder.SOURCES:
            pack = builder.load_pack(source)
            audio, meta = builder.build_pseudo_song(source, pack)
            path = temp / f"{source['id']}.wav"
            sf.write(path, audio, pack["sr"], subtype="PCM_24")
            analysis = analyze(path, config, Budget(120))
            source_rows.append({
                "id": source["id"],
                "source_seconds": analysis.frames / analysis.sample_rate,
                "analysis_edges": len(analysis.edges),
                "ending_candidates": ending_entries(analysis, config),
            })
            for factor in (0.68, 1.6, 2.4):
                target_seconds = round(
                    analysis.frames / analysis.sample_rate * factor + 0.137, 3
                )
                req_dir = temp / f"{source['id']}-{factor}"
                req_dir.mkdir()
                baseline = run_variant(
                    path, analysis, target_seconds, config, "baseline", req_dir
                )
                boundary = run_variant(
                    path, analysis, target_seconds, config, "boundary", req_dir
                )
                requests.append({
                    "source": source["id"],
                    "factor": factor,
                    "target_seconds": target_seconds,
                    "baseline": baseline,
                    "boundary": boundary,
                })
            path.unlink(missing_ok=True)

    baseline_summary = summarize(requests, "baseline")
    boundary_summary = summarize(requests, "boundary")
    report = {
        "schema": "musicfit-ending-boundary/v1",
        "source_count": len(source_rows),
        "request_count": len(requests),
        "baseline": baseline_summary,
        "boundary": boundary_summary,
        "delta": {
            "requests_with_source_end": (
                boundary_summary["requests_with_source_end"]
                - baseline_summary["requests_with_source_end"]
            ),
            "source_end_candidates": (
                boundary_summary["source_end_candidates"]
                - baseline_summary["source_end_candidates"]
            ),
            "fade_candidates": (
                boundary_summary["fade_candidates"]
                - baseline_summary["fade_candidates"]
            ),
        },
        "sources": source_rows,
        "requests": requests,
        "elapsed_seconds": time.monotonic() - started,
        "quality_is_diagnostic": True,
        "not_proven": [
            "human naturalness of source-ending bridges",
            "semantic outro recognition",
            "arbitrary-song generalization",
        ],
    }
    assert report["source_count"] == 6
    assert report["request_count"] == 18
    assert baseline_summary["sample_exact"]
    assert boundary_summary["sample_exact"]
    (out / "report.json").write_text(
        json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False) + "\n",
        encoding="utf-8",
    )
    print(json.dumps({
        "baseline": baseline_summary,
        "boundary": boundary_summary,
        "delta": report["delta"],
        "elapsed_seconds": report["elapsed_seconds"],
    }, indent=2))


if __name__ == "__main__":
    main()
