# Audio loop structural ambiguity benchmark 001

## Goal

Test whether the current loop-ranking approach succeeds because the synthetic song is structurally too simple.

This fixture deliberately creates **nested hard negatives**:

- the correct restart and a decoy share the same first **2 bars**;
- the correct restart and a harder decoy share the same first **4 bars**;
- only an **8-bar** context uniquely identifies the correct musical position.

The experiment keeps Beat This `small0` as the candidate-grid detector, then compares 2-bar / 4-bar / 8-bar chroma-context ranking on the same detected downbeats.

## Canonical 24-bar structure

```text
bars  1-8   : A B C D E F G H   <- true loop start
bars  9-16  : A B X Y I J K L   <- 2-bar prefix decoy
bars 17-24  : A B C D M N O P   <- 4-bar prefix decoy
```

The repeated prefixes are intentional. A short-context scorer should be unable to reliably distinguish them.

## Fixture variants

- 3 unrotated source takes whose physical start is the canonical bar 1;
- 6 target variants using different whole-bar rotations and timbre/noise profiles;
- 18 cross-take ranking queries;
- positive candidate position changes with target rotation;
- exact PCM identity is not shared across source/target takes.

## Evaluation

Candidate positions come only from Beat This detected in-file downbeats.
Ground Truth is used only after scoring to label the detected candidate nearest the canonical bar-1 position within ±70 ms.

The same candidate set is scored three ways:

- **2 bars / 8 beats**
- **4 bars / 16 beats**
- **8 bars / 32 beats**

Reported metrics:

- candidate coverage;
- Hit@1 / Hit@3;
- MRR;
- pairwise ranking accuracy;
- rank of the known 2-bar-prefix decoy;
- rank of the known 4-bar-prefix decoy.

## Expected diagnostic shape

This is a diagnostic experiment, not a benchmark where every scorer is expected to pass.

A useful result is:

```text
2 bars: ambiguous / materially below 8-bar performance
4 bars: still confused by the 4-bar-prefix decoy
8 bars: strong recovery of the true loop start
```

If 8 bars is still insufficient, that is evidence that a richer structural representation may be justified. If 8 bars solves the fixture cleanly, there is still no demonstrated need for a heavier song-structure model.

## PASS boundary

The experiment passes when:

1. every query has exactly one detected positive candidate;
2. all 18 queries are covered;
3. 8-bar context achieves Hit@1 >= 0.90 and pairwise ranking >= 0.95;
4. 8-bar Hit@1 exceeds 2-bar Hit@1 by at least 0.30;
5. 8-bar Hit@1 exceeds 4-bar Hit@1 by at least 0.15;
6. the 4-bar-prefix decoy is demonstrably competitive under 4-bar scoring in at least half the queries;
7. the pinned Beat This `small0` checkpoint digest is unchanged.

## NOT PROVEN

PASS does not prove arbitrary-song loop extraction, vocal/music semantic understanding, section labeling, or YMM4 integration. It only shows whether increasing local musical context resolves deliberately nested structural ambiguity in this controlled fixture.
