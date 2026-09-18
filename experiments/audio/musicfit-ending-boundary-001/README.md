# Music Fit ending-boundary 001

## Goal

Improve Phase 4's ability to return to the **real source ending** instead of falling back to a fade, without adding a structure model.

The current core tries only three approximate ending-tail lengths (protected outro, 4 s, 8 s). On the six pinned real-derived sources, only 9/54 previously rendered candidates retained the source ending.

This experiment keeps all existing planning logic and adds only **candidate ending-entry locations** derived from the input waveform's already-computed features.

## Method

1. Reuse the normal `musicfit.analyze()` output.
2. Compute a lightweight novelty curve from frame-to-frame feature and energy change.
3. Search only the last 4–36 seconds of the source.
4. Keep separated novelty peaks as possible entry points into the source ending.
5. Add the original fixed 2/4/8-second ending candidates so the experiment cannot lose them merely because novelty is weak.
6. Run the same bounded Phase 4 beam search, changing only the set of candidate ending entries.

The search window is intentionally broad. Harmonix/SALAMI annotation-only priors show common sections in the tens-of-seconds range, but no semantic section label is inferred at runtime.

## Evaluation

The exact same six hash-pinned Colorosse-derived pseudo-songs and three target-duration factors used by `music-fit-core-001/benchmark.py --real` are compared:

- baseline Phase 4
- boundary-aware Phase 4

Report:

- requests with at least one source-ending Top3 candidate;
- source-ending candidates among all Top3;
- fade candidates;
- mean/min transition score;
- multi-jump count;
- exact rendered sample counts;
- planning/render time.

All transformed third-party audio is runtime-only and deleted. CI uploads JSON evidence only.

## PASS boundary

PASS proves the alternative planner executes on all 18 requests and renders exact sample counts. Source-ending rate is diagnostic on the first run; adoption requires a non-regressing result.

This does **not** prove an ending is perceptually natural. Source-ending preservation is only one useful proxy, and listening review remains required.
