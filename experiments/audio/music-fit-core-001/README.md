# Music Fit Core 001 — CPU-only component candidate

**Question:** Can a bounded, reusable worker discover transitions in one audio file, render clean joins, fit a requested duration, and combine multiple jumps without a large model or YMM4 dependency?

This is the pre-plugin component, **not a YMM4 plugin**. It uses NumPy, SciPy and SoundFile. No PyTorch, LLM, GPU, source separation, cloud inference or network access is required by the core. Existing experiments stay unchanged. The separately invoked real-audio benchmark downloads the lab's already hash-pinned sources.

## Implemented scope

| Phase | Component | Current boundary |
|---|---|---|
| 1 | `analyze()` | Single-file, beat-independent recurrence; several endpoint/period hypotheses; local context check and small waveform registration. No original/reference loop or ground truth input. |
| 2 | `render()` | Original sample rate/channels, streaming PCM24 WAV, constant-sum smooth crossfades with outgoing handles, global peak attenuation only, exact sample accounting. |
| 3 | `plans(..., phase=3)` | One recurring jump type per route; protected prefix; duration fitting; explicitly identified fade ending when the original ending cannot fit. |
| 4 | `plans(..., phase=4)` | Bounded beam search over different backward/forward recurrence jumps; scored exact-length bridge to the original ending when suitable; up to three distinct edit maps. |
| Review | `fit()` | Rendered candidates, each join/ending excerpt, portable JSON edit map, local `review.html` and listening-rating export. |

The two-section integration test gives the analyzer one waveform, discovers independent 8-second and 6-second repetitions, combines them to extend the song by 14 seconds, preserves the original end, and renders the exact sample count. It does not inject loop points into the analyzer.

## Quick start

Python 3.11+; the workflow also tests Windows and Linux.

```sh
python -m pip install -r requirements.txt
python -m unittest discover -s tests -v
python -m musicfit request.example.json
```

Set `input`, `target_seconds` and a **new** `output_dir` in the request. Relative paths are relative to the process working directory. The input is never replaced. UTF-8 paths are supported by the Python interface; the eventual YMM4 launcher must pass JSON without shell interpolation.

The request can also be supplied on stdin. Stdout is a compact JSON response. A successful render returns `status: needs_listening_review`, not a claim that the audio sounds natural.

```json
{
  "schema": "musicfit/v1",
  "input": "input.wav",
  "target_seconds": 120.0,
  "output_dir": "fitted-output",
  "phase": 4,
  "budget_seconds": 120
}
```

Output: `result.json`, one to three candidate WAVs, `previews/`, and `review.html`. Open the HTML locally to compare full candidates, all edits and endings. No server or CDN is used. Listening ratings export to JSON.

## Reusable API / edit-map contract

```python
from pathlib import Path
from musicfit import Budget, Config, analyze, plans
from musicfit.render import render

config = Config()
budget = Budget(seconds=120)
a = analyze(Path("input.wav"), config, budget)
for p in plans(a, 120.0, config, budget, phase=4):
    render(Path("input.wav"), a, p, Path(p.id + ".wav"), config, budget)
```

All `Span.start/end`, target lengths and seam locations use **original-source sample frames**, not milliseconds; `end` is exclusive. Stereo has one frame per simultaneous L/R pair. The sum of span lengths equals the target frames. There is no per-edit rounding accumulation.

A jump does **not** subtract crossfade duration from the route: at the beginning of the next span the renderer blends the outgoing source's post-cut handle with the incoming span's first samples. Weights sum to one. This preserves the intended musical period instead of silently shortening every repeat. Fade length is bounded by both available handles and next-span length. Both channels always use the same timing/envelope.

The source SHA256 is checked before/after rendering. Interrupted renders remove their temporary WAV. `fit()` publishes the output directory only after all candidates, previews and JSON succeed. Existing input/output files and existing output directories are refused. API consumers must handle `FitError` and `Cancelled`; no suitable route is not disguised as success.

## Cost bounds / host contract

Defaults: at most 900 seconds input, 256 MiB decoded float32 input, mono/stereo 8–96 kHz, 6,000 analysis frames, 90-second maximum scanned period, 24 recurrence edges, beam width 32, 48 search jumps, 1,800 seconds output and a 120-second operation budget. The grid coarsens on long inputs to enforce the feature bound. This can reduce long-track boundary precision; thresholds are not silently relaxed.

Analysis uses an anti-aliased 11,025 Hz copy; rendering reads the original file at original rate. The energetic channel is used for analysis so L/R phase cancellation does not erase stereo music. Output is streamed in 65,536-frame blocks, not allocated in proportion to output duration.

SIGINT/SIGTERM and an optional `cancel_file` are checked cooperatively. A future YMM4 adapter should launch this worker out of process and enforce its own hard timeout/process-tree termination. Cooperative budgets cannot interrupt every native library call. This is not a real-time audio effect.

`keep_intro_seconds` and `keep_outro_seconds` are protected prefix/suffix constraints, **not learned semantic Intro/Outro labels**. Actual long intros may need explicit values from the host/user. A `.NET` in-process port or frozen Windows executable is not provided here. NumPy/SciPy/native decoder license and packaging notices must be retained when bundling those dependencies; their binaries are not included in this source component.

## Evaluation

```sh
python benchmark.py --output out-synthetic
# Only in the full lab checkout, with network access:
python benchmark.py --real --output out-real
```

Real inputs come from the existing `audio-real-loop-self-discovery-001` fixture builder: six Colorosse packs, exact ZIP hashes, CC0/CC-BY metadata, runtime-only audio. Attribution: Oğuzhan Girgin / Colorosse, original source/terms recorded in the earlier source-inventory experiment. No license is changed by this component. CI uploads **metadata/results only**, not these original or transformed recordings.

Reports separate:

- strict known period + body-containment recovery, Top1/Top3;
- route availability across **all** requests, including failures;
- exact output sample counts and distinct-jump execution;
- signal-level seam diagnostics, analysis/planning/render elapsed times;
- human acceptance (`null` until actual listening ratings exist).

Derived versions of the same song are not independent evidence. The six real sources are a small, single-publisher, loop-derived diagnostic set, not a representative 85–90% acceptance benchmark. No acceptance threshold is selected after seeing the real results.

## PASS vs NOT PROVEN

PASS for this component means the executed tests verify single-file discovery, Phase 3/4 route/render contracts, sample counts, stereo, overwrite/source-change protection, cancellation cleanup and playable review outputs. A green diagnostic benchmark means execution/reporting succeeded; it does not mean every track produced a good plan.

NOT PROVEN: 85–90% human acceptance, naturalness on arbitrary unmodified songs, vocal/lyric continuity, semantic phrase boundaries, all clicks/phase/energy artifacts, optimal graph routes, frozen Windows bundle size, and YMM4 native integration. Scores and margins are **not percentages or calibrated probabilities**. Low-evidence sources can correctly return no suitable plan.

The implementation does not time-stretch, alter pitch, invent new music, or claim that every noncanonical loop position is wrong. A short fade may be preferable to a weak jump. A candidate ending marked `fade` has not retained the original musical ending.

## Technical references

- SciPy polyphase anti-aliased resampling: https://docs.scipy.org/doc/scipy/reference/generated/scipy.signal.resample_poly.html
- SoundFile streaming/format interface and dependency licensing: https://python-soundfile.readthedocs.io/
- Existing lab experiments provide the recurrence and public-source investigation trail; this component introduces no blanket claim about third-party patent clearance.

Code and repository-authored synthetic fixtures: Apache-2.0, as at the repository root.
