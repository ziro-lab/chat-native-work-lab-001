# Music Fit Core 001 — CPU-only component candidate

**Question:** Can a bounded worker turn one audio file into up to three auditionable, exact-duration arrangements through Phase 4, without a large model or a YMM4 dependency?

This is a **pre-plugin component**, not a YMM4 plugin. The core requires NumPy, SciPy and SoundFile. No PyTorch, LLM, GPU, cloud inference, stem separation or network access is required. Earlier lab experiments are unchanged.

## Implemented

| Stage | Public interface | Scope |
|---|---|---|
| Phase 1 | `analyze()` / `audition_edges()` | Beat-independent single-file recurrence plus a UI-facing selector that prefers different period lengths before duplicate phases. No original loop, BPM or ground truth input. |
| Phase 2 | `render()` | Stereo-preserving streaming PCM24 WAV, smooth constant-sum crossfades, original sample rate and exact sample accounting. |
| Phase 3 | `plans(..., phase=3)` | Single recurring jump type per route, protected prefix, exact duration and explicit fade ending. |
| Phase 4 | `plans(..., phase=4)` | Bounded search across different backward/forward jumps; scored bridge to original ending using fixed tails plus lightweight novelty-derived ending entries; up to three distinct edit maps. |
| Phase 5 A (experimental) | `plans(..., phase=5)` / `fit(..., phase=5)` | Boundary/ending hints and forward-only Shorten Family A; one candidate. Larger/equal targets explicitly delegate to Phase 4. Full Phase 5 is not complete. |
| Audition | `fit()` | Full candidates, period-diverse `loop_hypotheses`, each join and ending excerpt, `result.json`, local `review.html` and listening-rating JSON export. |

The full integration test discovers separate 8s and 6s repetitions from a waveform, combines them into a 14s extension, preserves the original ending, and renders the exact sample count. No loop points are injected into that test's analyzer.

### v0.1.1 quality adoption

Two lightweight policies were promoted only after public-lab A/B evidence:

- **Loop audition diversity:** the analyzer's full edge list is unchanged for planning, but `audition_edges()` prefers hypotheses separated by at least 180 ms in period before filling duplicate-period slots. On the frozen 48-source commercial-safe FSLD holdout, the annotation-derived soft Top3 audition metric improved from 87.5% to 91.67%; strict publisher-period Top3 improved from 72.92% to 77.08%. This is candidate coverage, not human acceptance.
- **Ending-entry expansion:** Phase 4 now adds model-free novelty peaks in the final portion of the source while retaining the original fixed ending-tail candidates. On the six pinned real-derived sources, source-ending candidates increased from 9/54 to 12/54 with effectively unchanged mean transition score. This is not semantic outro recognition.

Neither change lowers the normal recurrence threshold or adds a model/runtime dependency.

### Phase 5 checkpoint A — v0.2.0a1

The [full Phase 5 design](PHASE5_STRUCTURE_AWARE_PLANNER_DESIGN.md) remains the architecture target. The first [experimental checkpoint](PHASE5_CHECKPOINT_A.md) is now implemented: lightweight boundary/ending hints, shared-search forward-only Shorten A and explicit source-ending-before-fade fallback. Default Phase 4 remains the baseline. Intro detection, Highlight, B/C diversity, dedicated Extend policy and a selectable Loop workflow are not implemented yet.

Use `request.phase5.example.json`, or the local test package's `run_phase5_windows.bat`. This is a listening checkpoint, not a claim of better human acceptance or full Phase 5 completion.

## Run

Python 3.11+. Run from this directory:

```sh
python -m pip install -r requirements.txt
python -m unittest discover -s tests -v
python -m musicfit request.example.json
```

Edit `input`, `target_seconds` and a **new** `output_dir` in the example request. Relative paths use the process working directory. A request can also be supplied on stdin. IPC is UTF-8 JSON, including Japanese filenames; the future launcher must not interpolate it into a shell command.

```python
from pathlib import Path
from musicfit import Budget, Config, analyze, audition_edges, plans
from musicfit.render import render
c, b = Config(), Budget(seconds=120)
a = analyze(Path('input.wav'), c, b)
print(audition_edges(a))  # UI/listening hypotheses; planner still sees all edges
for p in plans(a, 120.0, c, b, phase=4):
    render(Path('input.wav'), a, p, Path(p.id + '.wav'), c, b)
```

A successful fit returns `needs_listening_review`, not an assertion of naturalness. The source is never replaced. Existing output files/directories are refused. No suitable extension returns an explicit error rather than inventing a loop. `FitError` and `Cancelled` must be handled by API consumers.

## Audio / safety contract

All span bounds, target counts and seam positions are **original-source sample frames**, with exclusive end. Stereo uses one frame per simultaneous L/R pair. Span lengths sum exactly to target frames.

At a jump, the renderer blends the outgoing source's **post-cut handle** into the first samples of the next span. Crossfades do not subtract time from each repeat. Both channels share timing/envelopes, and weights sum to one. A global attenuation only prevents source-peak clipping. Tempo/pitch are not changed. Fade endings are explicitly distinguished from retaining the original ending.

Source SHA256 is checked before/after rendering. Temporary partial WAVs are removed on failure. `fit()` publishes its staged directory only after all WAVs, excerpts and JSON have succeeded. SIGINT/SIGTERM and an optional `cancel_file` are checked cooperatively. The eventual host should also enforce a hard timeout/process-tree termination; native library calls are not always cooperatively interruptible.

`keep_intro_seconds` / `keep_outro_seconds` protect caller-specified prefix/suffix lengths; they are **not semantic intro/outro recognition**. Impossible duration constraints fail rather than being silently shortened. Zero protection is allowed explicitly.

## Cost boundaries

Defaults: input <=900s and 256 MiB decoded float32, mono/stereo 8–96 kHz, 6,000 feature frames, maximum scanned period 90s, 24 recurrence edges, beam width 32, 128 search jumps, output <=1,800s, budget 120s. Bounds cap work; they do not guarantee a suitable route exists. Analysis grids coarsen on long tracks, and registration follows the grid's precision up to a 120ms search radius.

Analysis uses anti-aliased 11,025 Hz audio; rendering reads the original audio in 65,536-frame blocks. An energetic channel is analyzed instead of L+R so anti-phase stereo does not disappear. If joint features yield no strong edge, harmonic-only and texture-only views are tried with **unchanged thresholds**, and fallback provenance is recorded. Fallback scores are not calibrated confidence.

A frozen Windows executable, final dependency bundle size, native .NET port and native YMM4 integration are outside this component. Do not install Python manually from the eventual plugin UI; packaging is a later host-adapter task. Dependency binaries are not included in this source component; preserve their own licensing/notices when bundling.

## Evaluation and evidence

```sh
python benchmark.py --output out-synthetic
python scale_check.py
# Full lab checkout + network only:
python benchmark.py --real --output out-real
```

The workflow tests Windows and Linux. Real fixtures reuse the lab's six exact-ZIP-hash-pinned Colorosse sources and synthesize single-file tracks from their stems. Source attribution: Oğuzhan Girgin / Colorosse; original terms/identities are in `audio-real-loop-source-inventory-001` and `audio-real-loop-self-discovery-001`. Source licenses remain CC0 / CC-BY as recorded there. The core itself never downloads them. CI uploads **reports only**, not original or transformed third-party audio.

Reports distinguish known-period/body recovery Top1/Top3, availability over **all** fit requests, exact lengths, multi-jump execution, timings and signal-level seam diagnostics. Human acceptance stays `null` until listening ratings exist. Derivatives of one song are not independent samples. Six loop-derived sources from one publisher are not a general music benchmark.

**PASS:** executed component/worker tests, exact output sample counts, stereo, source/overwrite guards, cancellation cleanup, Phase 3/4 integration and review-file generation. A diagnostic run can pass execution while reporting no suitable plan on some tracks.

**NOT PROVEN:** 85–90% human acceptance, arbitrary unmodified-song generalization, lyric continuity, semantic phrase boundaries, all perceptual artifacts, globally optimal routes, executable bundle size and native YMM4 integration. Scores are never percentages. A short fade can be preferable to an unsupported jump.

See `RESULTS.md` when present for source HEAD, actual tested checkout/tree, run/artifact identities and measured outcomes. Technical references: https://docs.scipy.org/doc/scipy/reference/generated/scipy.signal.resample_poly.html and https://python-soundfile.readthedocs.io/ . Code and repository-owned synthetic fixtures use the root Apache-2.0 license. No blanket third-party patent-clearance claim is made.
