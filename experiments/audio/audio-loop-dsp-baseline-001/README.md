# Audio loop DSP baseline 001

## Goal

Measure how far a lightweight, dependency-free DSP baseline can recover the **known loop phase** from transformed loop material before introducing learned Beat/Downbeat or music-structure models.

This experiment deliberately compares two ideas on the same Ground Truth:

1. **seam-only** — judge the candidate mostly from the immediate source-tail / target-head tonal similarity;
2. **context recurrence** — compare two bars of beat-synchronous chroma context from the known source loop start against each target candidate, plus a small downbeat-alignment term.

The benchmark rotates every generated take by a different whole-bar amount, so the positive target is not always at sample 0. Queries are only formed across different underlying takes, which preserves the v1 requirement that exact PCM identity cannot solve the task.

## Fixture

The experiment consumes the synthetic generator from `../audio-loop-recovery-001/` and builds six phase-rotated variants:

```text
base take 1 -> rotations 0 / 3 bars
base take 2 -> rotations 2 / 6 bars
base take 3 -> rotations 1 / 5 bars
```

All ordered source/target pairs across **different base takes** are evaluated: 24 queries total.

For every query, candidates contain all eight bar downbeats plus one off-beat hard negative. The positive is the target location whose canonical musical bar equals the source file's canonical start bar.

## DSP features

No NumPy, ML runtime or third-party audio library is required.

- input is downsampled from 16 kHz to 4 kHz for analysis;
- each beat is represented by a 12-bin chroma-like vector;
- chroma energy is estimated with a Goertzel bank over MIDI C3-B5;
- vectors are L2-normalized to reduce sensitivity to gain differences;
- rhythm alignment is derived from the benchmark's known tempo/meter grid.

### Seam-only score

```text
95% cosine(chroma(source final beat), chroma(target candidate beat))
 5% downbeat alignment
```

### Context-recurrence score

```text
95% mean cosine similarity over the next 8 beats (2 bars)
 5% downbeat alignment
```

The context score is intentionally simple. It is not a learned music model; the purpose is to test whether **looking at musical context rather than a single seam** already provides a large gain.

## Reproduce

```bash
python verify.py
```

The script generates the rotated benchmark, scores both baselines, runs the shared evaluator, and writes evidence under `out/`.

## PASS boundary

PASS requires:

- 6 rotated variants and 24 cross-take queries;
- positive targets span multiple physical bar positions;
- all variant PCM hashes are deterministic and not all identical;
- context-recurrence Hit@1 >= 0.90;
- context-recurrence pairwise ranking accuracy >= 0.95;
- context recurrence beats both seam-only and deterministic random ranking;
- evidence contains `PASS_AUDIO_LOOP_DSP_BASELINE_V1`.

PASS proves that the synthetic phase-recovery fixture can distinguish a context-aware lightweight DSP baseline from a single-boundary baseline.

## NOT PROVEN

This experiment does **not** prove:

- automatic discovery of the loop start in an arbitrary ordinary song;
- Beat/Downbeat detection (the beat grid is supplied by the benchmark);
- robustness to tempo drift, key changes, vocals, dense mastering or live timing;
- perceptual naturalness of every high-scoring transition;
- crossfade/click suppression;
- YMM4 integration.

The next useful step is to remove the oracle rhythm grid by inserting a public Beat/Downbeat detector and then add stronger structure-preserving transformations such as codec, EQ and reverberation changes.
