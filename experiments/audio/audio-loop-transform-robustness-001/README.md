# Audio loop transform robustness 001

## Goal

Test whether the current lightweight loop-analysis pipeline still recovers the known loop phase when every take is post-processed differently with **Ground-Truth-preserving audio transformations**.

The previous detected-grid experiment reached 24/24 Top-1 on clean synthetic material. This experiment intentionally makes the audio representation less identical without changing the underlying bar/phrase order.

## Pipeline under test

```text
phase-rotated synthetic loop takes
        |
        v
independent per-take post processing
  - spectral tilt / EQ
  - soft compression / saturation
  - short causal reverb / echoes
  - deterministic broadband noise
  - gain differences
        |
        v
Beat This small0
  -> detected beats / downbeats
        |
        v
2-bar beat-synchronous chroma context
        |
        v
loop candidate ranking
        |
        v
hidden Ground Truth evaluation
```

Ground Truth is not used to generate or score candidates. It is used only after predictions are produced.

## Why these transformations

They are intentionally chosen to alter waveform identity, level, spectrum and short-term temporal texture while preserving:

- tempo;
- meter;
- pitch class / harmony;
- bar order;
- phrase order;
- the known loop phase.

That keeps the original positive transition valid. More destructive edits such as key changes, time-stretching, chord replacement or inserted fills should be treated as separate negative/structure tests because they can invalidate the Ground Truth itself.

## Transform profiles

Six deterministic profiles are applied to the six phase-rotated variants. The profiles vary spectral tilt, saturation strength, short echo mix, noise level and gain independently. No processed WAV is committed to Git; all files are generated in Actions.

## Metrics

The same 24 cross-take phase-recovery queries are evaluated with:

- candidate coverage;
- Hit@1;
- Hit@3;
- MRR;
- pairwise ranking accuracy.

A detected candidate counts as the hidden positive when it is within ±70 ms of the known loop-phase target.

## PASS boundary

PASS requires:

- at least 95% query candidate coverage;
- Hit@1 >= 0.80;
- Hit@3 >= 0.95;
- pairwise ranking accuracy >= 0.90;
- all six transformed WAV hashes are distinct;
- the pinned Beat This small0 checkpoint SHA256 remains exact.

These thresholds are intentionally below the clean-fixture 1.0 result. The question is whether the approach is robust enough to survive representation changes, not whether a controlled transformation matrix remains perfect.

## NOT PROVEN

PASS does not prove ordinary-song loop extraction. It still does not cover:

- intro / verse / chorus / bridge structure;
- multiple musically plausible but semantically different recurring sections;
- vocals and dense commercial masters;
- tempo or meter changes;
- destructive edits that change harmony or phrase order;
- final seam / crossfade rendering quality;
- YMM4 integration.

If this passes comfortably, the next useful benchmark is structural ambiguity rather than more DSP cosmetics.
