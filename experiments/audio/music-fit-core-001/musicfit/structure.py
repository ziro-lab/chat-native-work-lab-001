"""Bounded, model-free structural hints. Strengths are NOT probabilities.

Two-scale rectangular checkerboard novelty under a linear similarity kernel
is evaluated as squared left/right feature-mean contrast. Prefix sums avoid
an N x N self-similarity matrix. This is not a semantic phrase/outro detector.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
import math

import numpy as np
from scipy.signal import find_peaks

from .core import Analysis, Budget, Config, FitError, _ending_entries

HINT_VERSION = 'phase5-a/1'
MAX_BOUNDARIES_PER_SCALE = 32
MAX_ENDINGS = 24


@dataclass(frozen=True)
class BoundaryHint:
    frame: int
    strength: float
    scale: str
    cues: tuple[str, ...]


@dataclass(frozen=True)
class SectionHint:
    start: int
    end: int
    rms: float
    recurrence_support: float


@dataclass(frozen=True)
class EndingHint:
    frame: int
    strength: float
    kind: str
    cues: tuple[str, ...] = ()


@dataclass
class StructureHints:
    source_sha256: str
    sample_rate: int
    frames: int
    boundaries: list[BoundaryHint] = field(default_factory=list)
    sections: list[SectionHint] = field(default_factory=list)
    intro_candidates: list[dict] = field(default_factory=list)
    highlights: list[dict] = field(default_factory=list)
    ending_entries: list[EndingHint] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    def to_dict(self):
        return {'schema': 'musicfit-structure-hints/v1', 'version': HINT_VERSION,
                'calibrated_confidence': False, **asdict(self)}


def _contrast(x: np.ndarray, width: int) -> np.ndarray:
    """Signed checkerboard (dot-product SSM), rectangular temporal kernel."""
    n = len(x)
    result = np.zeros(n, dtype=np.float64)
    if 2 * width >= n:
        return result
    if x.ndim == 1:
        x = x[:, None]
    cs = np.vstack([np.zeros((1, x.shape[1])), np.cumsum(x, axis=0)])
    i = np.arange(width, n - width)
    delta = (cs[i + width] - 2 * cs[i] + cs[i - width]) / width
    result[i] = np.mean(delta * delta, axis=1)
    return result


def _normalize(v: np.ndarray, absolute_floor: float) -> np.ndarray:
    """Do not turn a flat/no-information cue into a high-confidence signal."""
    hi = float(np.max(v, initial=0))
    if hi <= absolute_floor:
        return np.zeros_like(v)
    lo = float(np.median(v))
    upper = max(float(np.percentile(v, 95)), hi * .25, absolute_floor)
    return np.clip((v - lo) / max(upper - lo, absolute_floor), 0, 1)


def _recurrence_support(a: Analysis, b: Budget) -> np.ndarray:
    """Positive support from retained edges only; absence is NOT uniqueness.

    Analysis.edges is a sparse top-edge list, not a complete recurrence matrix.
    Never claim semantic terminality/final-occurrence from missing edges.
    """
    profile = np.zeros(len(a.energy), dtype=np.float64)
    for edge in a.edges:
        b.check()
        start = max(0, math.floor(edge.start / a.sample_rate / a.hop_seconds))
        end = min(len(profile), math.ceil(
            (edge.end + edge.end - edge.start) / a.sample_rate / a.hop_seconds))
        profile[start:end] = np.maximum(profile[start:end], edge.period_score)
    return profile


def build_structure_hints(a: Analysis, config: Config | None = None,
                          budget: Budget | None = None) -> StructureHints:
    c, b = config or Config(), budget or Budget()
    c.validate(); b.check()
    x = np.asarray(a.features, dtype=np.float64)
    energy = np.asarray(a.energy, dtype=np.float64)
    if (x.ndim != 2 or x.shape[1] < 25 or len(x) != len(energy)
            or len(x) > c.max_feature_frames or energy.ndim != 1
            or not np.all(np.isfinite(x)) or not np.all(np.isfinite(energy))
            or np.any(energy < 0) or not math.isfinite(a.hop_seconds)
            or a.hop_seconds <= 0):
        raise FitError('invalid_structure_features')
    hints = StructureHints(a.source_sha256, a.sample_rate, a.frames)
    hints.warnings = ['heuristic_strength_not_probability',
                      'sparse_recurrence_support_not_semantic_terminality',
                      'intro_detection_and_highlight_not_implemented_in_checkpoint_a']
    intro = round(c.keep_intro_seconds * a.sample_rate)
    if intro <= a.frames:
        hints.intro_candidates = [{'end_frame': intro, 'strength': 0.0,
                                   'kind': 'caller_protected_prefix'}]
    support = _recurrence_support(a, b)
    # Reuse the existing whitened harmonic/timbre views; no FFT or new decoder.
    cues = [('chroma', x[:, :12], 1e-5),
            ('timbre', x[:, 12:25], 1e-6),
            ('energy', np.log(np.maximum(energy, 1e-6)), .0025)]
    audible = len(energy) >= 5 and float(np.max(energy, initial=0)) > 1e-5
    if audible:
        for scale, half_window in [('fine', .6), ('coarse', 2.4)]:
            b.check()
            width = max(2, round(half_window / a.hop_seconds))
            values = [_normalize(_contrast(v, width), floor)
                      for _, v, floor in cues]
            # Fuse only informative cues. A chroma-only boundary stays detectable.
            active = [v for v in values if np.any(v)]
            if not active:
                continue
            novelty = np.mean(active, axis=0)
            spacing = max(1, round(half_window / a.hop_seconds))
            peaks, properties = find_peaks(novelty, height=.25,
                                            prominence=.15, distance=spacing)
            ranked = sorted(zip(peaks, properties['prominences']),
                            key=lambda row: (-row[1], row[0]))
            for idx, prominence in ranked[:MAX_BOUNDARIES_PER_SCALE]:
                frame = round(int(idx) * a.hop_seconds * a.sample_rate)
                if not 0 < frame < a.frames:
                    continue
                evidence = tuple(name for (name, _, _), v in zip(cues, values)
                                 if v[idx] >= .25)
                hints.boundaries.append(BoundaryHint(
                    frame, float(min(1.0, prominence)), scale, evidence))
    hints.boundaries.sort(key=lambda h: (h.frame, h.scale))
    if not hints.boundaries:
        hints.warnings.append('no_salient_structural_boundary')
    cuts = sorted({0, a.frames, *[h.frame for h in hints.boundaries
                                if h.scale == 'coarse']})
    for start, end in zip(cuts, cuts[1:]):
        b.check()
        lo = max(0, math.floor(start / a.sample_rate / a.hop_seconds))
        hi = min(len(energy), max(lo + 1, math.ceil(
            end / a.sample_rate / a.hop_seconds)))
        rms = float(np.sqrt(np.mean(energy[lo:hi] ** 2))) if hi > lo else 0.0
        rep = float(np.mean(support[lo:hi])) if hi > lo else 0.0
        hints.sections.append(SectionHint(start, end, rms, rep))
    endings: list[EndingHint] = []
    sr, n = a.sample_rate, a.frames
    protected = round(c.keep_outro_seconds * sr)
    min_tail = max(protected, round(4 * sr))
    max_tail = max(min_tail, min(round(36 * sr), round(n * .45)))
    for boundary in hints.boundaries:
        tail = n - boundary.frame
        if boundary.scale == 'coarse' and min_tail <= tail <= max_tail:
            endings.append(EndingHint(boundary.frame, boundary.strength,
                                      'coarse_boundary', boundary.cues))
    # Fixed 8/12/16-second tails are useful hypotheses, not detected sections.
    for seconds in (8, 12, 16):
        tail = round(seconds * sr)
        if protected <= tail < n:
            endings.append(EndingHint(n - tail, 0.0, 'fixed_tail'))
    # Keep compatibility hypotheses, including the explicit caller protection.
    for entry in _ending_entries(a, c):
        if 0 < entry <= n - protected:
            endings.append(EndingHint(entry, 0.0, 'legacy_ending_entry'))
    unique: list[EndingHint] = []
    for item in sorted(endings, key=lambda e: (
            e.kind != 'coarse_boundary', e.kind == 'legacy_ending_entry',
            -e.strength, e.frame)):
        if all(abs(item.frame - other.frame) >= max(1, round(.08 * sr))
               for other in unique):
            unique.append(item)
    hints.ending_entries = unique[:MAX_ENDINGS]
    b.check()
    return hints
