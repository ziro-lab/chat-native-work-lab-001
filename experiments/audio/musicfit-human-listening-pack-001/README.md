# Music Fit human listening pack 001

## Goal

Measure what the automatic benchmarks cannot: whether the Phase 4 outputs actually sound natural enough to use.

This pack deliberately uses **new CC0 FSLD sources** that do not overlap the frozen 48-source holdout or the 64-source audition-tuning set. It is an evaluation pack, not another tuning set.

## Pack

- 6 previously unused CC0 source loops;
- each is turned into one single-file pseudo-song with a short fade-in body, three mild non-byte-identical renders, and a fade-out body;
- two target durations per source: one shorter and one longer;
- up to three Music Fit Phase 4 candidates per case;
- candidate order is deterministically shuffled to A/B/C so automatic rank is hidden during listening;
- full candidate plus join/ending excerpts;
- one combined `review.html` that exports `listening-ratings.json`;
- `unblind.json` maps A/B/C back to automatic rank **after** rating.

All packed audio is derived only from FSLD records gated as **CC0-1.0**. No CC BY/NC/ND/SA audio is included in the downloadable listening artifact.

## Ratings

For each A/B/C candidate, rate separately:

- **overall**: usable / minor issue / reject;
- **seam**: clean / noticeable / bad;
- **flow**: natural / slightly repetitive/awkward / bad;
- **ending**: natural / acceptable fade / bad.

Primary product metric:

- **Top3 usable rate** = cases where at least one of A/B/C is overall=usable;
- secondary: automatic Top1 usable rate after unblinding.

This matches the intended YMM4 UX: automatic default when good, but three quick listening choices are acceptable.

## PASS boundary

A green workflow proves only that:

- 6 new CC0 sources with 12 cases are generated;
- each case has three exact-duration candidates;
- no source ID overlaps the frozen holdout or tuning set;
- blind A/B/C mapping is deterministic;
- the artifact contains the review UI and ratings exporter.

Human acceptance remains unknown until a person actually submits ratings.
