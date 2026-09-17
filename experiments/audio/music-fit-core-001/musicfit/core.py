"""Bounded CPU music fitting. Scores are heuristics, never acceptance probabilities."""
from __future__ import annotations
import hashlib
import math
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Callable
import numpy as np
import soundfile as sf
from scipy import signal

VERSION = '0.1.0'
SCHEMA = 'musicfit/v1'

class FitError(ValueError):
    pass

class Cancelled(FitError):
    pass

@dataclass
class Budget:
    seconds: float = 120.0
    cancel: Callable[[], bool] = field(default=lambda: False, repr=False)
    started: float = field(default_factory=time.monotonic)
    def check(self):
        if self.cancel():
            raise Cancelled('cancelled')
        if time.monotonic() - self.started > self.seconds:
            raise FitError('time_budget_exceeded')

@dataclass(frozen=True)
class Config:
    analysis_rate: int = 11025
    grid_seconds: float = 0.04
    max_feature_frames: int = 6000
    min_loop_seconds: float = 2.0
    max_loop_seconds: float = 90.0
    max_input_seconds: float = 900.0
    max_decoded_bytes: int = 256 * 1024 * 1024
    max_output_seconds: float = 1800.0
    min_similarity: float = 0.78
    max_edges: int = 24
    beam_width: int = 32
    max_jumps: int = 48
    min_run_seconds: float = 1.0
    keep_intro_seconds: float = 2.0
    keep_outro_seconds: float = 2.0
    crossfade_seconds: float = 0.06
    fade_out_seconds: float = 0.75
    def validate(self):
        for k, v in asdict(self).items():
            if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) or v <= 0:
                raise FitError(f'invalid_config:{k}')
        for name in ('analysis_rate','max_feature_frames','max_decoded_bytes','max_edges','beam_width','max_jumps'):
            if type(getattr(self,name)) is not int: raise FitError(f'invalid_integer_config:{name}')
        if self.analysis_rate != 11025 or self.grid_seconds < 0.02 or self.max_feature_frames > 12000:
            raise FitError('analysis_limits_exceeded')
        if not 0 < self.min_similarity < 1 or self.min_loop_seconds >= self.max_loop_seconds:
            raise FitError('invalid_similarity_or_loop_range')
        if self.max_edges > 64 or self.beam_width > 128 or self.max_jumps > 128:
            raise FitError('search_limits_exceeded')
        if self.crossfade_seconds > 0.2 or self.max_output_seconds > 3600:
            raise FitError('render_limits_exceeded')

@dataclass(frozen=True)
class Edge:
    start: int
    end: int
    score: float
    period_score: float
    kind: str = 'recurrence'

@dataclass
class Analysis:
    source_sha256: str
    sample_rate: int
    channels: int
    frames: int
    sample_peak: float
    hop_seconds: float
    features: np.ndarray = field(repr=False)
    energy: np.ndarray = field(repr=False)
    edges: list[Edge] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    elapsed_seconds: float = 0.0
    def summary(self):
        return {k: v for k, v in asdict(self).items() if k not in ('features', 'energy')}

@dataclass(frozen=True)
class Span:
    start: int
    end: int

@dataclass
class Plan:
    id: str
    target_frames: int
    spans: list[Span]
    cost: float
    ending: str
    transition_scores: list[float]
    warnings: list[str]
    strategy: str
    def to_dict(self):
        return asdict(self)


def digest(path: Path, budget: Budget | None = None) -> str:
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        while data := f.read(1024 * 1024):
            if budget:
                budget.check()
            h.update(data)
    return h.hexdigest()


def target_frames(seconds: float, sr: int, config: Config) -> int:
    if isinstance(seconds, bool) or not isinstance(seconds, (int, float)) or not math.isfinite(seconds):
        raise FitError('invalid_target_seconds')
    if not 0.25 <= seconds <= config.max_output_seconds:
        raise FitError('target_out_of_range')
    return round(seconds * sr)


def _features(samples: np.ndarray, sr: int, c: Config, budget: Budget):
    # Choose the energetic channel, not L+R: anti-phase stereo must not disappear.
    channel = int(np.argmax(np.einsum('ij,ij->j', samples, samples, dtype=np.float64)))
    mono = samples[:, channel]
    g = math.gcd(sr, c.analysis_rate)
    y = signal.resample_poly(mono, c.analysis_rate // g, sr // g).astype(np.float32)
    hop = max(round(c.grid_seconds * c.analysis_rate), math.ceil(len(y) / c.max_feature_frames))
    nfft = 2048
    padded = np.pad(y, (nfft // 2, nfft // 2))
    views = np.lib.stride_tricks.sliding_window_view(padded, nfft)[::hop]
    views = views[:math.ceil(len(y) / hop)]
    hann = np.hanning(nfft).astype(np.float32)
    freqs = np.fft.rfftfreq(nfft, 1 / c.analysis_rate)
    bins = np.flatnonzero((freqs >= 55) & (freqs <= 4500))
    pc = np.rint(69 + 12 * np.log2(freqs[bins] / 440)).astype(int) % 12
    spectral_bins = np.clip(np.floor(np.log2(np.maximum(freqs, 55) / 55) * 2).astype(int), 0, 12)
    chroma, timbre, rms = [], [], []
    for offset in range(0, len(views), 256):
        budget.check()
        block = views[offset:offset + 256]
        mag = np.abs(np.fft.rfft(block * hann)).astype(np.float32)
        chroma.append(np.stack([mag[:, bins[pc == k]].sum(axis=1) for k in range(12)], axis=1))
        timbre.append(np.stack([mag[:, spectral_bins == k].sum(axis=1) for k in range(13)], axis=1))
        rms.append(np.sqrt(np.mean(block ** 2, axis=1)))
    ch = np.log1p(np.concatenate(chroma))
    ti = np.log1p(np.concatenate(timbre))
    energy = np.concatenate(rms)
    ch /= np.maximum(np.linalg.norm(ch, axis=1, keepdims=True), 1e-8)
    ti /= np.maximum(np.linalg.norm(ti, axis=1, keepdims=True), 1e-8)
    # Whitening emphasizes changes over the song-wide key/timbre. Cap gain on flat bands.
    def whiten(x):
        return (x - np.mean(x, axis=0)) / np.maximum(np.std(x, axis=0), 0.04)
    x = np.concatenate([whiten(ch), 0.35 * whiten(ti),
                        0.35 * whiten(np.diff(ch, axis=0, prepend=ch[:1]))], axis=1)
    return x.astype(np.float32), energy, hop / c.analysis_rate, channel


def _window_sum(x, width):
    cs = np.concatenate(([0.0], np.cumsum(x, dtype=np.float64)))
    return cs[width:] - cs[:-width]


def transition_score(a: Analysis, exit_frame: int, entry_frame: int, window_seconds=0.6) -> float:
    """Compare actual in-file context around each cut; never wrap a non-looping track."""
    h = a.hop_seconds
    i = round(exit_frame / a.sample_rate / h)
    j = round(entry_frame / a.sample_rate / h)
    radius = max(2, round(window_seconds / h))
    left = min(radius, i, j)
    right = min(radius, len(a.features) - i, len(a.features) - j)
    if left < 1 or right < 1:
        return 0.0
    x, y = a.features[i-left:i+right], a.features[j-left:j+right]
    den = float(np.linalg.norm(x) * np.linalg.norm(y))
    corr = float(np.sum(x * y)) / den if den > 1e-10 else 0.0
    ei = float(np.mean(a.energy[i-left:i+right]))
    ej = float(np.mean(a.energy[j-left:j+right]))
    db = abs(20 * math.log10(max(ei, 1e-8) / max(ej, 1e-8)))
    return float(np.clip((corr + 1) / 2 - min(db / 80, 0.3), 0, 1))


def _refine_period(mono, sr, start, end, max_shift=0.04):
    """Small waveform registration only; no key/tempo change or phase-vocoder dependency."""
    radius = round(max_shift * sr)
    width = round(0.18 * sr)
    if start < radius or end < radius or end + width + radius >= len(mono):
        return start, end
    costs = np.zeros(2 * radius + 1, dtype=np.float64)
    for shift in (0, round(0.28 * sr)):
        if end + shift + width + radius >= len(mono):
            continue
        x = mono[start+shift:start+shift+width].astype(np.float64)
        y = mono[end+shift-radius:end+shift+width+radius].astype(np.float64)
        n = signal.correlate(y, x, mode='valid', method='fft')
        den = np.sqrt(np.maximum(_window_sum(y*y, width) * np.sum(x*x), 1e-15))
        costs += n / den
    # Penalize distant cycle slips weakly; aligned stereo channels are not shifted independently.
    costs -= 0.015 * (np.arange(-radius, radius+1) / max(1, radius)) ** 2
    return start, end + int(np.argmax(costs)) - radius


def analyze(path: Path, config: Config | None = None, budget: Budget | None = None) -> Analysis:
    c, b = config or Config(), budget or Budget()
    c.validate(); b.check()
    started = time.monotonic()
    info = sf.info(str(path))
    if info.channels not in (1, 2) or not 8000 <= info.samplerate <= 96000:
        raise FitError('supported_audio:mono_or_stereo_8_to_96_kHz')
    if info.frames < info.samplerate or info.duration > c.max_input_seconds:
        raise FitError('input_duration_out_of_range')
    if info.frames * info.channels * 4 > c.max_decoded_bytes:
        raise FitError('decoded_audio_budget_exceeded')
    identity = digest(path, b)
    samples, sr = sf.read(str(path), dtype='float32', always_2d=True)
    if len(samples) != info.frames or not np.all(np.isfinite(samples)):
        raise FitError('invalid_audio_samples')
    if digest(path,b) != identity: raise FitError('source_changed_during_analysis')
    peak = float(np.max(np.abs(samples)))
    x, energy, hop, channel = _features(samples, sr, c, b)
    a = Analysis(identity, sr, samples.shape[1], len(samples), peak, hop, x, energy)
    if peak < 1e-5 or float(np.percentile(energy, 80)) < 1e-5:
        a.warnings.append('silence_or_inaudible_audio')
        return a
    if samples.shape[1] == 2:
        a.warnings.append(f'analysis_channel_{channel};render_preserves_stereo')
    lo = max(2, math.ceil(c.min_loop_seconds / hop))
    hi = min(len(x) // 2 - 1, math.floor(c.max_loop_seconds / hop))
    peaks = []
    floor = max(1e-5, float(np.percentile(energy, 65)) * 0.15)
    def lag_scores(lag):
        left, right = x[:-lag], x[lag:]
        den = np.sqrt(np.maximum(_window_sum(np.sum(left*left, axis=1), lag) *
                                _window_sum(np.sum(right*right, axis=1), lag), 1e-12))
        corrs = _window_sum(np.sum(left*right, axis=1), lag) / den
        valid = (_window_sum(energy[:-lag], lag) / lag > floor) & (_window_sum(energy[lag:], lag) / lag > floor)
        corrs[~valid] = -1
        return corrs
    for lag in range(lo, hi + 1):
        if lag % 24 == 0: b.check()
        corrs = lag_scores(lag)
        if len(corrs): peaks.append((float(np.max(corrs)), lag))
    peaks.sort(reverse=True)
    used_lags, pairs = [], []
    for recurrence, lag in peaks:
        if recurrence < c.min_similarity: break
        if any(abs(lag - other) * hop < 0.18 for other in used_lags): continue
        used_lags.append(lag)
        corrs = lag_scores(lag)
        # A long recurrent phrase need not have its best local join at the start of that window.
        # Check several actual endpoints; exclude weak seams instead of trusting recurrence alone.
        indices = sorted(range(len(corrs)), key=lambda i: -corrs[i])
        accepted_starts = []
        examined = 0
        for idx in indices:
            if corrs[idx] < c.min_similarity or examined >= 40: break
            if any(abs(idx-other)*hop < 0.24 for other in accepted_starts): continue
            examined += 1
            s, e = round(idx * hop * sr), round((idx + lag) * hop * sr)
            if s < round(0.2 * sr) or e > len(samples) - round(0.2 * sr): continue
            s, e = _refine_period(samples[:, channel], sr, s, e)
            seam = transition_score(a, e, s)
            if seam < c.min_similarity: continue
            score = 0.75 * float(corrs[idx]) + 0.25 * seam
            pairs.append(Edge(s, e, float(score), float(corrs[idx])))
            accepted_starts.append(idx)
            if len(accepted_starts) >= 3 or len(pairs) >= c.max_edges: break
        if len(pairs) >= c.max_edges: break
    pairs.sort(key=lambda edge: (-edge.score, edge.start, edge.end))
    a.edges = pairs
    if not pairs: a.warnings.append('no_strong_recurrence')
    a.elapsed_seconds = time.monotonic() - started
    return a


def validate_plan(a: Analysis, p: Plan, c: Config):
    if not p.spans or len(p.spans) > c.max_jumps + 2:
        raise FitError('invalid_plan_span_count')
    if not 1 <= p.target_frames <= round(c.max_output_seconds * a.sample_rate):
        raise FitError('invalid_plan_target')
    if any(not isinstance(s.start, int) or not isinstance(s.end, int) or not 0 <= s.start < s.end <= a.frames for s in p.spans):
        raise FitError('invalid_plan_span')
    if sum(s.end - s.start for s in p.spans) != p.target_frames:
        raise FitError('plan_duration_mismatch')
    if len(p.transition_scores) != len(p.spans) - 1 or any(not math.isfinite(v) for v in p.transition_scores):
        raise FitError('invalid_transition_scores')
    if not math.isfinite(p.cost) or p.ending not in ('source_end', 'fade'):
        raise FitError('invalid_plan_metadata')
    for prev, nxt in zip(p.spans, p.spans[1:]):
        if prev.end != nxt.start and prev.end >= a.frames:
            raise FitError('no_outgoing_crossfade_handle')


def _merged(spans):
    result = []
    for s in spans:
        if s.end <= s.start: continue
        if result and result[-1].end == s.start:
            result[-1] = Span(result[-1].start, s.end)
        else: result.append(s)
    return result


def plans(a: Analysis, seconds: float, config: Config | None = None, budget: Budget | None = None, phase=4) -> list[Plan]:
    c, b = config or Config(), budget or Budget()
    c.validate()
    if phase not in (3, 4): raise FitError('phase_must_be_3_or_4')
    target = target_frames(seconds, a.sample_rate, c)
    sr, n = a.sample_rate, a.frames
    intro = min(round(c.keep_intro_seconds * sr), n // 10, target // 4)
    outro = min(round(c.keep_outro_seconds * sr), n // 10, target // 4)
    minimum = min(round(c.min_run_seconds * sr), target // 4)
    eligible = [e for e in a.edges if e.start >= intro and e.end <= n - outro and e.end-e.start >= minimum]
    # Edge tuple: exit, entry, similarity, identity. Adjacent source playback costs no edit.
    edges = [(e.end, e.start, e.score, f'b{i}') for i,e in enumerate(eligible)]
    if phase == 4:
        edges += [(e.start, e.end, e.score, f'f{i}') for i,e in enumerate(eligible)]
    terminals = {}
    # State = output frames, source cursor, completed spans, cumulative cost, used edge IDs.
    beam = [(0, 0, (), 0.0, ())]
    for depth in range(c.max_jumps + 1):
        b.check()
        next_states = []
        for written, cursor, spans, cost, used in beam:
            remaining = target - written
            if remaining <= 0: continue
            if remaining <= n - cursor:
                tail = Span(cursor, cursor + remaining)
                mode = 'source_end' if tail.end == n else 'fade'
                penalty = 0 if mode == 'source_end' else 0.65
                route = _merged([*spans, tail])
                key = tuple((s.start, s.end) for s in route)
                terminals[key] = (cost + penalty, route, mode, 'loop_fit' if phase==3 else 'graph_fit')
                # Exact-length ending bridge: preserve the actual original ending; compare both contexts.
                if phase == 4 and remaining >= minimum + outro and n - cursor - remaining > round(0.12*sr):
                    for tail_len in (outro, min(n//5, round(4*sr)), min(n//4, round(8*sr))):
                        entry = n - tail_len
                        exit = cursor + remaining - tail_len
                        if exit < cursor + minimum or exit < intro or exit >= entry or exit >= n-outro: continue
                        score = transition_score(a, exit, entry)
                        if score < c.min_similarity: continue
                        route = _merged([*spans, Span(cursor, exit), Span(entry, n)])
                        key = tuple((s.start,s.end) for s in route)
                        terminals[key] = (cost + 0.07 + 0.7*(1-score), route, 'source_end', 'graph_fit')
            if depth == c.max_jumps: continue
            for exit, entry, score, eid in edges:
                if exit < cursor + minimum or exit < intro: continue
                total = written + exit - cursor
                if total >= target - max(outro, minimum): continue
                if phase == 3 and used and eid != used[0]: continue
                repetitions = used.count(eid)
                newcost = cost + 0.035 + 0.55*(1-score) + 0.008*repetitions
                newused = (*used, eid)
                route = (*spans, Span(cursor, exit))
                delta = abs(target - (total + n - entry)) / max(target, sr)
                priority = newcost + 0.5 * delta
                next_states.append((priority, total, entry, route, newcost, newused))
        if not next_states: break
        next_states.sort(key=lambda v: (v[0],v[1],v[2],v[5]))
        seen = set(); beam = []
        for _, written, cursor, route, cost, used in next_states:
            # Keep alternatives by duration/source location; bounded, not exponential route enumeration.
            key = (round(written / max(1, round(sr*.05))), cursor, used[-1])
            if key in seen: continue
            seen.add(key)
            beam.append((written,cursor,route,cost,used))
            if len(beam) >= c.beam_width: break
        if len(terminals) > 512:
            terminals = dict(sorted(terminals.items(), key=lambda kv: kv[1][0])[:256])
    ranked = sorted(terminals.values(), key=lambda v: (v[0],len(v[1]),tuple((s.start,s.end) for s in v[1])))
    selected = []
    signatures = []
    for cost, route, mode, strategy in ranked:
        # Deduplicate near-identical arrangements by coarse edit map, not filename or waveform checksum.
        sig = tuple((round(left.end/sr/0.25),round(right.start/sr/0.25)) for left,right in zip(route,route[1:]))
        if sig in signatures: continue
        signatures.append(sig)
        scores = [transition_score(a,l.end,r.start) for l,r in zip(route,route[1:])]
        warnings = ['listening_review_required;not_a_calibrated_probability']
        if mode == 'fade': warnings.append('exact_duration_uses_fade_not_original_ending')
        if scores and min(scores) < c.min_similarity: warnings.append('weak_local_transition')
        p = Plan(f'candidate-{len(selected)+1}', target, route, float(cost), mode, scores, warnings, strategy)
        validate_plan(a,p,c)
        selected.append(p)
        if len(selected) == 3: break
    return selected
