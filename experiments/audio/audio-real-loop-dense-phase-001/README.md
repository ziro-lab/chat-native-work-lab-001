# Real-audio dense chroma phase fallback 001

## Goal

Test the failure mode exposed by `audio-real-loop-ranking-001`: on real audio, chroma ranking was correct whenever the true phase appeared in the candidate set, but Beat This **downbeat-only candidate generation** covered only 5/12 queries.

This experiment removes rhythm events as a hard gate and tests a dense, beat-independent phase search on the exact same 12 pinned real-audio queries.

## Method

1. Re-run the pinned real-audio benchmark to generate the exact same source / stem-remixed / bar-rotated WAV pairs.
2. Ignore Beat/Downbeat candidates for the fallback search.
3. Build a circular chroma sequence on an approximately 40 ms uniform grid for source and target.
4. Score every possible circular target phase against the source sequence.
5. Use the full loop-cycle chroma recurrence score to select the best phase.
6. Reveal the hidden sample-exact rotation Ground Truth only after scoring.

The uniform grid is created from audio duration alone. It does **not** use BPM, bar count or Ground Truth to place candidates.

## Why dense phase search

The real-audio baseline exposed three distinct rhythm-grid problems:

- downbeat phase shifted by roughly one beat on some remixes;
- half-time / sparse beat detection on slower or cleaner material;
- near-pulse-free material (`Prism`) where Beat This abstained.

A dense chroma phase search can still propose hypotheses in all three cases. Beat This can later remain useful as a prior or fast path rather than the sole candidate generator.

## Feature representation

- circular STFT frames;
- ~186 ms Hann window at 44.1 kHz;
- pitch-class energy collapsed to 12-dimensional chroma;
- L2 normalization per frame;
- candidate grid spacing <= 40 ms;
- full-cycle mean cosine similarity for ranking.

This is intentionally simple. No learned song-structure model is added.

## PASS boundary

This first dense-search experiment is diagnostic. PASS proves:

- the same 12 pinned real-audio variants were regenerated;
- every query received a dense candidate grid whose maximum spacing is <= 40 ms;
- every query produced a ranked top candidate;
- the hidden Ground Truth was used only for post-ranking error measurement;
- per-query and aggregate Hit@1@±70ms are emitted;
- no third-party audio is committed or uploaded as an artifact.

Quality remains diagnostic on this first run. The result is compared directly against the previous downbeat-only end-to-end accuracy of 5/12.
