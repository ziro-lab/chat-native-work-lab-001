# Audio Beat This loop ranking 001

## Goal

Replace the oracle rhythm grid in `audio-loop-dsp-baseline-001` with **Beat This small0 predictions**, then test whether the lightweight two-bar chroma-context scorer still recovers the known loop phase.

Ground Truth is used only for evaluation. Candidate generation and scoring do not receive the known bar positions.

## Pipeline

```text
six phase-rotated synthetic takes
        |
        v
Beat This small0 (CPU, DBN off)
        |
        +--> detected beats -> beat-synchronous chroma context
        |
        +--> detected downbeats -> loop candidates
        |
        v
2-bar context recurrence score
        |
        v
Top-1 / Top-3 / MRR / pairwise ranking vs hidden Ground Truth
```

The 16.0 s terminal boundary emitted by Beat This is excluded because it is the end of the circular fixture, not a distinct candidate inside the file.

## Candidate rules

For each variant:

- keep detected beats/downbeats inside `[0, duration)`;
- choose the first detected downbeat as the source context anchor;
- use every detected target downbeat inside the file as a candidate;
- estimate beat duration from the median detected beat interval;
- compute chroma-like features around detected beat positions;
- compare the next 8 beats (2 bars) circularly.

No oracle BPM, beat timestamps, bar timestamps, or positive target time is used by the scorer.

## Evaluation

For each of the 24 cross-take phase-recovery queries from the previous experiment, the known positive physical bar is converted to a Ground-Truth timestamp **only after predictions are produced**.

A detected candidate is positive when it lands within ±70 ms of that hidden target. Metrics:

- candidate coverage: queries containing exactly one positive detected downbeat;
- Hit@1 / Hit@3;
- MRR;
- pairwise ranking accuracy of positive against all other detected downbeats.

## PASS boundary

PASS requires:

- all 24 queries have exactly one detected positive candidate;
- Hit@1 >= 0.90;
- Hit@3 >= 0.95;
- pairwise ranking accuracy >= 0.95;
- the exact observed Beat This small0 checkpoint digest remains pinned.

## NOT PROVEN

PASS still does not prove arbitrary-song loop extraction. The fixture is deliberately controlled and circular. In particular it does not yet cover:

- intro / verse / chorus / bridge structure;
- tempo drift, meter changes, swing or expressive timing;
- vocals and dense real-world masters;
- sections that are harmonically similar but structurally wrong;
- seam/crossfade rendering quality;
- YMM4 integration.

The next benchmark should make the synthetic material less loop-like and add license-vetted real-loop transformations before considering heavier structure models.
