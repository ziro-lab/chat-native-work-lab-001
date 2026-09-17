# Audio loop adaptive-context benchmark 001

## Goal

Test a lightweight confidence policy before adding a heavier song-structure model.

The policy starts with short musical context and only expands when the candidate ranking is ambiguous:

```text
2 bars
  -> if Top1 - Top2 >= 0.05: accept
  -> otherwise 4 bars
       -> if Top1 - Top2 >= 0.05: accept
       -> otherwise 8 bars and decide
```

The `0.05` margin is a **provisional benchmark-derived threshold**, not a production constant. It must be revalidated on new real-song data.

## Two evaluation families

### Easy-but-transformed family

Reuse `audio-loop-transform-robustness-001`:

- Beat This detected downbeats;
- independent EQ/dynamics/reverb/noise/gain transformations;
- the existing 2-bar chroma-context ranker;
- 24 queries.

Expectation: the Top1 margin should already be large enough to stop at 2 bars.

### Structurally ambiguous family

Reuse the nested hard-negative fixture from `audio-loop-structural-ambiguity-001`:

- true phrase and decoys share 2-bar / 4-bar prefixes;
- Beat This detected downbeats only;
- 18 queries;
- Ground Truth is hidden until after the adaptive policy has selected a candidate.

Expectation: 2 and 4 bars should remain ambiguous, so the policy should escalate to 8 bars.

## PASS boundary

- easy family: Hit@1 = 1.0 and at least 95% stop at 2 bars;
- structural family: Hit@1 >= 0.90;
- structural family: at least 80% reach 8 bars;
- no query may consult Ground Truth when deciding whether to expand context;
- pinned Beat This `small0` checkpoint identity remains unchanged.

## Why this matters

If this succeeds, the next YMM4-side design does not need to run the longest context unconditionally, and a heavy semantic section model still has no demonstrated necessity on these controlled cases.

This experiment measures **decision sufficiency**, not end-to-end wall-clock speed. Beat This inference and feature extraction are still paid regardless of context length in this lab implementation.
