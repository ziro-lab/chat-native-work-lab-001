# Real-audio loop self-discovery stress matrix 001

## Goal

Check whether the 6/6 single-track result from `audio-real-loop-self-discovery-001` was merely a consequence of one convenient pseudo-song layout.

Use the same six pinned real loop packs, but vary the presentation around the repeated body while keeping the analyzer blind to BPM, beat grid, loop length and source-reference audio.

## Stress dimensions

Eight deterministic scenarios are generated for every source (48 tracks total):

| scenario | repeats | intro bars | outro bars | body phase shift |
| --- | ---: | ---: | ---: | ---: |
| two-clean | 2 | 0 | 0 | 0 |
| two-framed | 2 | 1 | 1 | 1 bar |
| two-long-intro | 2 | 2 | 0 | 1 bar |
| two-long-outro | 2 | 0 | 2 | 1 bar |
| three-open | 3 | 0 | 1 | 1 bar |
| three-framed | 3 | 2 | 2 | 0 |
| four-open | 4 | 1 | 0 | 1 bar |
| four-framed | 4 | 1 | 1 | 0 |

`Prism` has only two bars per canonical loop, so phase shifts wrap modulo its bar count.

Each repeated cycle also receives a different stem balance, mild nonlinear rendering, deterministic gain jitter and low-level noise. Cycles therefore share musical timing/structure but not PCM identity.

## Discovery

The experiment reuses the lightweight single-track feature/recurrence engine:

- NumPy only;
- ~40 ms feature grid;
- STFT chroma + harmonic motion + energy;
- no Beat This;
- no BPM / bar count / source loop passed into the discovery function;
- direct lag search up to exactly half the observed track, so a two-repeat track can still expose its period.

## Fundamental vs valid multiple

With four repeated cycles, a two-cycle period (`2 × fundamental`) is also a musically valid loop. Therefore the report separates:

- **fundamental hit** — selected period matches the publisher loop period within ±70 ms;
- **valid integer-multiple hit** — selected period matches `k × fundamental` within ±70 ms and both endpoints lie inside the repeat body.

Integer multiples are accepted as valid loop periods, though the fundamental remains preferable because it gives more flexibility when fitting a target duration.

## PASS boundary

PASS requires, across all 48 real-audio stress cases:

- every case produces a period and endpoint pair;
- fundamental Hit@±70 ms >= 0.80;
- valid-multiple + body-contained rate >= 0.95;
- two-repeat scenarios valid rate >= 0.90;
- every source individually reaches valid rate >= 0.875 (at least 7/8 scenarios);
- discovery still receives no BPM, beat grid, source-reference audio or hidden period.

These thresholds are fixed before the run. A failure should be analyzed rather than relaxed.
